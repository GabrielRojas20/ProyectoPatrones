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
            throw new ApplicationException("pagination.invalid", "La página debe ser mayor que cero y mostrar entre 1 y 10 resultados.", 422);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class CustomerSearchService(IOdooGateway gateway)
{
    public Task<PagedResult<CustomerDto>> SearchAsync(string? name, int page, int pageSize, CancellationToken ct)
    {
        CatalogService.ValidatePage(page, pageSize);
        var normalized = string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length is < 2 or > 35 || !Regex.IsMatch(normalized, @"^[\p{L} ]+$"))
            throw new ApplicationException("customer.search_invalid", "Escribe entre 2 y 35 letras y espacios para buscar al cliente.", 422);
        return gateway.SearchCustomersAsync(normalized, page, pageSize, ct);
    }
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
    // Este POS opera detrás de una única API local. Serializar las confirmaciones
    // y reservar temporalmente las unidades cubre el retraso de Odoo al reflejar ventas.
    private static readonly SemaphoreSlim SaleGate = new(1, 1);
    private static readonly Dictionary<long, StockState> StockByProduct = [];
    private static readonly Dictionary<string, Dictionary<long, decimal>> ReservationsBySale = new(StringComparer.Ordinal);

    public async Task<SaleResult> CreateAsync(string idempotencyKey, CreateSaleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ApplicationException("idempotency.required", "Se requiere un Idempotency-Key válido.", 422);

        await SaleGate.WaitAsync(ct);
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

            if (request.Lines.Any(x => x.ProductId <= 0))
                throw new ApplicationException("sale.invalid_product", "Selecciona productos válidos del catálogo.", 422);
            if (request.Lines.Any(x => x.DiscountPercent is < 0 or > 100))
                throw new ApplicationException("sale.invalid_discount", "El descuento debe estar entre 0 % y 100 %.", 422);

            var ids = request.Lines.Select(x => x.ProductId).Distinct().ToArray();
            var products = await gateway.GetProductsByIdsAsync(ids, ct);
            if (products.Count != ids.Length || products.Values.Any(x => !x.Available))
                throw new ApplicationException("sale.product_unavailable", "Uno o más productos no existen o no están habilitados.", 422);

            var requestedByProduct = request.Lines.GroupBy(x => x.ProductId)
                .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
            foreach (var (productId, quantity) in requestedByProduct)
            {
                if (quantity > 40)
                    throw new BusinessRuleException("sale.invalid_quantity", "La cantidad total de un producto no puede superar 40 unidades.");
                var product = products[productId];
                var available = GetAvailableQuantity(product);
                if (quantity > available)
                    throw new ApplicationException("sale.insufficient_stock",
                        $"No hay existencias suficientes de {product.Name}. Disponibles: {available:0.##}.", 409);
            }

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
            SaleCustomer? customer = null;
            if (request.Customer is { } requestedCustomer)
            {
                var partner = await gateway.FindCustomerAsync(requestedCustomer, ct);
                if (partner is null)
                    throw new ApplicationException("customer.not_found", "El cliente no existe en Odoo; selecciónelo o regístrelo antes de vender.", 422);
                customer = new SaleCustomer(partner.Id, partner.Name, partner.Identification, partner.Email);
            }
            var pending = new Sale(Guid.NewGuid().ToString("N"), request.OperationDate, session.Id,
                request.PaymentMethodId, lines, customer, SaleStatus.Pending, total, null, null);

            Sale confirmed;
            try { confirmed = await gateway.CreateSaleAsync(pending, ct); }
            catch (IntegrationException ex) when (ex.Uncertain)
            {
                ReserveStock(idempotencyKey, requestedByProduct);
                throw new ApplicationException("odoo.result_uncertain", "Odoo recibió la solicitud, pero no fue posible confirmar el resultado. No reintente con otra clave.", 502);
            }

            if (confirmed.Status != SaleStatus.Confirmed || string.IsNullOrWhiteSpace(confirmed.ExternalId))
                throw new IntegrationException("odoo.sale_not_confirmed", "Odoo no confirmó la venta ni el pago.");

            ReserveStock(idempotencyKey, requestedByProduct);
            var result = new SaleResult(confirmed.Id, "confirmed", confirmed.Total, confirmed.ExternalId, confirmed.Message);
            await idempotency.SaveAsync(idempotencyKey, fingerprint, result, ct);
            return result;
        }
        finally { SaleGate.Release(); }
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
        if (request.Lines.Any(x => x.Quantity is < 1 or > 40))
            throw new BusinessRuleException("sale.invalid_quantity", "Cada cantidad debe estar entre 1 y 40 unidades.");
        if (request.Customer is not { } customer)
            throw new ApplicationException("customer.required", "Selecciona o busca un cliente antes de confirmar la venta.", 422);
        ValidateCustomer(customer);
    }

    private static void ValidateCustomer(CustomerRequest customer)
    {
        if (customer.PartnerId is <= 0)
            throw new ApplicationException("customer.invalid_id", "Selecciona un cliente válido.", 422);
        if (string.IsNullOrWhiteSpace(customer.Identification) ||
            !Regex.IsMatch(customer.Identification, @"^([0-9]{10}|[0-9]{13})$"))
            throw new ApplicationException("customer.invalid_identification", "La cédula debe tener 10 dígitos o el RUC 13 dígitos.", 422);
        var normalizedName = string.IsNullOrWhiteSpace(customer.Name)
            ? string.Empty
            : Regex.Replace(customer.Name.Trim(), @"\s+", " ");
        if (normalizedName.Length is 0 or > 35 || !Regex.IsMatch(normalizedName, @"^[\p{L}]+(?: [\p{L}]+)*$"))
            throw new ApplicationException("customer.invalid_name", "El nombre es obligatorio, admite hasta 35 letras y espacios, sin números ni signos.", 422);
        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            if (customer.Email.Length > 35 || !IsValidEmail(customer.Email))
                throw new ApplicationException("customer.invalid_email", "El correo debe tener un formato válido y máximo 35 caracteres.", 422);
        }
        else
            throw new ApplicationException("customer.email_required", "El correo del cliente es obligatorio.", 422);
    }

    private static bool IsValidEmail(string value)
    {
        if (value.Any(char.IsWhiteSpace) || value.Count(character => character == '@') != 1) return false;
        var parts = value.Split('@');
        var local = parts[0];
        var domain = parts[1];
        if (local.Length == 0 || local.Length > 64 || local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return false;
        if (!Regex.IsMatch(local, @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+$")) return false;
        var labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length is > 0 and <= 63 &&
            Regex.IsMatch(label, @"^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?$")) &&
            Regex.IsMatch(labels[^1], @"^[A-Za-z]{2,}$");
    }

    private static decimal GetAvailableQuantity(ProductDto product)
    {
        if (!StockByProduct.TryGetValue(product.Id, out var state))
            state = new StockState(product.AvailableQuantity, 0);
        else
        {
            var stockDecrease = Math.Max(0, state.ObservedQuantity - product.AvailableQuantity);
            var remainingReservation = Math.Max(0, state.ReservedQuantity - stockDecrease);
            state = new StockState(product.AvailableQuantity, remainingReservation);
        }
        StockByProduct[product.Id] = state;
        return Math.Max(0, product.AvailableQuantity - state.ReservedQuantity);
    }

    private static void ReserveStock(string saleKey, IReadOnlyDictionary<long, int> quantities)
    {
        if (ReservationsBySale.ContainsKey(saleKey)) return;
        var reservation = quantities.ToDictionary(entry => entry.Key, entry => (decimal)entry.Value);
        foreach (var (productId, quantity) in reservation)
        {
            var current = StockByProduct.GetValueOrDefault(productId, new StockState(0, 0));
            StockByProduct[productId] = current with { ReservedQuantity = current.ReservedQuantity + quantity };
        }
        ReservationsBySale[saleKey] = reservation;
    }

    private sealed record StockState(decimal ObservedQuantity, decimal ReservedQuantity);

}

public sealed class MaximumDiscountPolicy(decimal maximumPercent) : IDiscountPolicy
{
    public bool IsAuthorized(decimal discountPercent) => discountPercent >= 0 && discountPercent <= maximumPercent;
}
