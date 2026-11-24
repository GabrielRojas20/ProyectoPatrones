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

        if (lines.Any(line => line.Quantity is < 1 or > 40))
            throw new BusinessRuleException("sale.invalid_quantity", "Cada cantidad debe estar entre 1 y 40 unidades.");

        if (lines.Any(line => line.UnitPrice < 0))
            throw new BusinessRuleException("sale.invalid_price", "Los precios no pueden ser negativos.");

        if (lines.Any(line => line.DiscountPercent is < 0 or > 100))
            throw new BusinessRuleException("sale.invalid_discount", "El descuento debe estar entre 0 % y 100 %.");
    }
}

public sealed class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
