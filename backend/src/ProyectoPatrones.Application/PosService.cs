using ProyectoPatrones.Domain;

namespace ProyectoPatrones.Application;

public sealed class PosService(IPosGateway gateway, IIdempotencyStore idempotencyStore,
    IEnumerable<IDiscountPolicy> discountPolicies)
{
    private readonly IReadOnlyDictionary<string, IDiscountPolicy> _policies = discountPolicies
        .ToDictionary(policy => policy.Code, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<Product>> SearchProductsAsync(string? search, CancellationToken cancellationToken) =>
        gateway.SearchProductsAsync(search, cancellationToken);

    public async Task<Sale> CreateSaleAsync(CreateSaleRequest request, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new BusinessRuleException("invalid_idempotency_key", "La clave de idempotencia es obligatoria.");
        if (request.Lines is null || request.Lines.Count == 0)
            throw new BusinessRuleException("empty_sale", "La venta debe contener al menos un producto.");
        if (request.Lines.Any(line => line.Quantity is < 1 or > 1000))
            throw new BusinessRuleException("invalid_quantity", "Las cantidades deben estar entre 1 y 1000.");
        if (!EcuadorianId.IsValid(request.CustomerDocument))
            throw new BusinessRuleException("invalid_customer_document", "La cédula o RUC ecuatoriano no es válido.");
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Length > 120)
            throw new BusinessRuleException("invalid_customer_name", "El nombre del cliente es obligatorio y admite máximo 120 caracteres.");
        if (!string.Equals(request.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("invalid_payment", "El único método configurado actualmente es efectivo.");
        if (!_policies.TryGetValue(request.DiscountCode ?? "NONE", out var policy))
            throw new BusinessRuleException("unauthorized_discount", "El descuento solicitado no está autorizado.");

        var completed = await idempotencyStore.GetAsync(idempotencyKey, cancellationToken);
        if (completed is not null) return completed;
        if (!await idempotencyStore.TryReserveAsync(idempotencyKey, cancellationToken))
            throw new OperationInProgressException("Ya existe una solicitud con esta clave en procesamiento.");

        try
        {
            if (!await gateway.HasActiveSessionAsync(cancellationToken))
                throw new BusinessRuleException("closed_session", "No existe una sesión POS abierta.");

            var products = await gateway.SearchProductsAsync(null, cancellationToken);
            var byId = products.ToDictionary(product => product.Id);
            var lines = new List<SaleLine>();
            foreach (var requested in request.Lines)
            {
                if (!byId.TryGetValue(requested.ProductId, out var product) || !product.Available)
                    throw new BusinessRuleException("unavailable_product", "Uno de los productos no está habilitado.");
                var lineSubtotal = product.Price * requested.Quantity;
                var (taxableBase, tax) = product.PriceIncludesTax
                    ? EcuadorTax.FromTaxIncluded(lineSubtotal, product.TaxRate)
                    : (lineSubtotal, decimal.Round(lineSubtotal * product.TaxRate, 2, MidpointRounding.AwayFromZero));
                lines.Add(new SaleLine(product.Id, product.Code, product.Name, requested.Quantity,
                    product.Price, 0, lineSubtotal, taxableBase, tax, product.TaxIds));
            }

            var subtotal = lines.Sum(line => line.Subtotal);
            var discount = policy.Calculate(subtotal);
            var fiscalFactor = subtotal == 0 ? 0 : (subtotal - discount) / subtotal;
            var saleTaxableBase = decimal.Round(lines.Sum(line => line.TaxableBase) * fiscalFactor, 2,
                MidpointRounding.AwayFromZero);
            var saleTax = subtotal - discount - saleTaxableBase;
            var draft = new Sale("pending", DateTimeOffset.UtcNow, "Pending", request.PaymentMethod,
                subtotal, discount, subtotal - discount, lines, gateway.Mode, null,
                saleTaxableBase, saleTax,
                request.CustomerDocument, request.CustomerName.Trim());
            var sale = await gateway.RegisterSaleAsync(draft, cancellationToken);
            await idempotencyStore.CompleteAsync(idempotencyKey, sale, cancellationToken);
            return sale;
        }
        catch
        {
            await idempotencyStore.ReleaseAsync(idempotencyKey, cancellationToken);
            throw;
        }
    }

    public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken) =>
        gateway.GetSalesAsync(cancellationToken);

    public async Task<Sale> GetSaleAsync(string id, CancellationToken cancellationToken) =>
        await gateway.GetSaleAsync(id, cancellationToken) ??
        throw new BusinessRuleException("sale_not_found", "No se encontró la venta solicitada.");
}
