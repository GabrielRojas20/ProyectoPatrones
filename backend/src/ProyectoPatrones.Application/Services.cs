using System.Collections.Concurrent;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProyectoPatrones.Domain;

namespace ProyectoPatrones.Application;

public sealed class AuthService(IOperatorAuthenticator authenticator, ITokenIssuer tokens)
{
    public LoginResponse Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) ||
            !authenticator.Verify(request.Username, request.Password))
            throw new ApplicationException("auth.invalid_credentials", "Usuario o contraseña inválidos.", 401);
        return tokens.Issue(request.Username);
    }
}

public sealed class CatalogService(IOdooGateway gateway)
{
    public Task<PagedResult<ProductDto>> SearchAsync(string? name, string? code, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        return gateway.GetProductsAsync(Normalize(name), Normalize(code), page, pageSize, ct);
    }

    internal static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 10)
            throw new ApplicationException("pagination.invalid", "page debe ser >= 1 y pageSize debe estar entre 1 y 10.", 422);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class PosSessionService(IOdooGateway gateway)
{
    public async Task<PosSessionDto> GetActiveAsync(CancellationToken ct) =>
        await gateway.GetActiveSessionAsync(ct) is { IsOpen: true } session
            ? session
            : throw new ApplicationException("pos.no_active_session", "No existe una sesión POS abierta.", 409);
}

public sealed partial class SaleService(
    IOdooGateway gateway,
    IIdempotencyStore idempotency,
    IDiscountPolicy discounts,
    IClock clock)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public async Task<SaleResult> CreateAsync(string idempotencyKey, CreateSaleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ApplicationException("idempotency.required", "Se requiere un Idempotency-Key válido.", 422);

        var gate = Locks.GetOrAdd(idempotencyKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var fingerprint = Fingerprint(request);
            if (await idempotency.GetAsync(idempotencyKey, fingerprint, ct) is { } stored)
                return stored with { Replayed = true };

            ValidateRequest(request);
            var session = await gateway.GetActiveSessionAsync(ct);
            if (session is null || !session.IsOpen || session.Id != request.SessionId)
                throw new ApplicationException("pos.invalid_session", "La sesión POS está cerrada o no coincide con la sesión activa.", 409);
            if (session.PaymentMethods.All(x => x.Id != request.PaymentMethodId))
                throw new ApplicationException("sale.invalid_payment_method", "El método de pago no está habilitado para la sesión.", 422);

            var ids = request.Lines.Select(x => x.ProductId).Distinct().ToArray();
            var products = await gateway.GetProductsByIdsAsync(ids, ct);
            if (products.Count != ids.Length || products.Values.Any(x => !x.Available))
                throw new ApplicationException("sale.product_unavailable", "Uno o más productos no existen o no están habilitados.", 422);

            var lines = request.Lines.Select(line =>
            {
                var product = products[line.ProductId];
                if (line.ExpectedUnitPrice is { } expected && expected != product.Price)
                    throw new ApplicationException("sale.price_changed", $"El precio del producto {product.Code} cambió; actualice el carrito.", 409);
                if (!discounts.IsAuthorized(line.DiscountPercent))
                    throw new ApplicationException("sale.discount_not_authorized", "El descuento solicitado no está autorizado.", 422);
                return new SaleLine(product.Id, line.Quantity, product.Price, line.DiscountPercent);
            }).ToArray();

            SaleRules.ValidateLines(lines);
            var total = lines.Sum(x => x.Subtotal);
            var pending = new Sale(Guid.NewGuid().ToString("N"), request.OperationDate, session.Id,
                request.PaymentMethodId, lines, SaleStatus.Pending, total, null, null);

            Sale confirmed;
            try { confirmed = await gateway.CreateSaleAsync(pending, ct); }
            catch (IntegrationException ex) when (ex.Uncertain)
            {
                throw new ApplicationException("odoo.result_uncertain", "Odoo recibió la solicitud, pero no fue posible confirmar el resultado. No reintente con otra clave.", 502);
            }

            if (confirmed.Status != SaleStatus.Confirmed || string.IsNullOrWhiteSpace(confirmed.ExternalId))
                throw new IntegrationException("odoo.sale_not_confirmed", "Odoo no confirmó la venta ni el pago.");

            var result = new SaleResult(confirmed.Id, "confirmed", confirmed.Total, confirmed.ExternalId, confirmed.Message);
            await idempotency.SaveAsync(idempotencyKey, fingerprint, result, ct);
            return result;
        }
        finally { gate.Release(); }
    }

    private static string Fingerprint(CreateSaleRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public Task<PagedResult<Sale>> SearchAsync(string? name, string? id, int page, int pageSize, CancellationToken ct)
    {
        CatalogService.ValidatePage(page, pageSize);
        return gateway.GetSalesAsync(name, id, page, pageSize, ct);
    }

    public async Task<Sale> GetAsync(string id, CancellationToken ct) =>
        await gateway.GetSaleAsync(id, ct) ?? throw new ApplicationException("sale.not_found", "Venta no encontrada.", 404);

    private void ValidateRequest(CreateSaleRequest request)
    {
        if (request.OperationDate != DateOnly.FromDateTime(clock.UtcNow.LocalDateTime))
            throw new ApplicationException("sale.invalid_date", "La fecha de operación debe ser la fecha actual del servidor.", 422);
        if (request.Lines is null || request.Lines.Count == 0)
            throw new BusinessRuleException("sale.empty", "La venta debe contener al menos un producto.");
        if (request.Lines.Any(x => x.Quantity <= 0))
            throw new BusinessRuleException("sale.invalid_quantity", "Todas las cantidades deben ser mayores que cero.");
        if (request.Customer is { } customer) ValidateCustomer(customer);
    }

    private static void ValidateCustomer(CustomerRequest customer)
    {
        if (!string.IsNullOrWhiteSpace(customer.Identification) && !MyRegex().IsMatch(customer.Identification))
            throw new ApplicationException("customer.invalid_identification", "La cédula debe contener exactamente 10 dígitos.", 422);
        if (!string.IsNullOrWhiteSpace(customer.Name) && !Regex.IsMatch(customer.Name, @"^[\p{L} ]+$"))
            throw new ApplicationException("customer.invalid_name", "El nombre solo puede contener letras y espacios.", 422);
        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            if (customer.Email.Length > 40 || customer.Email.Count(x => x == '@') != 1 ||
                !MailAddress.TryCreate(customer.Email, out var address) || address.Address != customer.Email ||
                !Regex.IsMatch(customer.Email, @"^[^\s@]+@[^\s@.]+\.[A-Za-z]{2,}$"))
                throw new ApplicationException("customer.invalid_email", "El correo no tiene un formato válido o excede 40 caracteres.", 422);
        }
    }

    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex MyRegex();
}

public sealed class MaximumDiscountPolicy(decimal maximumPercent) : IDiscountPolicy
{
    public bool IsAuthorized(decimal discountPercent) => discountPercent >= 0 && discountPercent <= maximumPercent;
}
