namespace ProyectoPatrones.Domain;

public enum SaleStatus { Pending, Confirmed, Rejected, Uncertain }

public sealed record SaleLine(long ProductId, int Quantity, decimal UnitPrice, decimal DiscountPercent)
{
    public decimal Subtotal => decimal.Round(Quantity * UnitPrice * (1 - DiscountPercent / 100m), 2);
}

public sealed record SaleCustomer(long PartnerId, string Name, string? Identification, string? Email);

public sealed record Sale(
    string Id,
    DateOnly OperationDate,
    long SessionId,
    long PaymentMethodId,
    IReadOnlyList<SaleLine> Lines,
    SaleCustomer? Customer,
    SaleStatus Status,
    decimal Total,
    string? ExternalId,
    string? Message);

public static class SaleRules
{
    public static void ValidateLines(IReadOnlyCollection<SaleLine> lines)
    {
        if (lines.Count == 0)
            throw new BusinessRuleException("sale.empty", "La venta debe contener al menos un producto.");

        if (lines.Any(line => line.Quantity <= 0))
            throw new BusinessRuleException("sale.invalid_quantity", "Todas las cantidades deben ser mayores que cero.");

        if (lines.Any(line => line.UnitPrice < 0))
            throw new BusinessRuleException("sale.invalid_price", "Los precios no pueden ser negativos.");
    }
}

public sealed class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
