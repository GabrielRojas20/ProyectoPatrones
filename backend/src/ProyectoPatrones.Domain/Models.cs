namespace ProyectoPatrones.Domain;

public sealed record Product(int Id, string Code, string Name, decimal Price, bool Available,
    decimal Stock = 0, decimal TaxRate = 0.15m, bool PriceIncludesTax = true,
    IReadOnlyList<int>? TaxIds = null);

public sealed record SaleLine(int ProductId, string Code, string Name, int Quantity,
    decimal UnitPrice, decimal Discount, decimal Subtotal, decimal TaxableBase = 0, decimal Tax = 0,
    IReadOnlyList<int>? TaxIds = null);

public sealed record Sale(string Id, DateTimeOffset CreatedAt, string Status, string PaymentMethod,
    decimal Subtotal, decimal Discount, decimal Total, IReadOnlyList<SaleLine> Lines,
    string IntegrationMode, string? ExternalId, decimal TaxableBase = 0, decimal Tax = 0,
    string CustomerDocument = "9999999999999", string CustomerName = "Consumidor Final");

public static class EcuadorianId
{
    public const string FinalConsumer = "9999999999999";

    public static bool IsValid(string? value)
    {
        if (value == FinalConsumer) return true;
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => !char.IsDigit(c))) return false;
        if (value.Length == 13) return value.EndsWith("001", StringComparison.Ordinal) && IsValidCedula(value[..10]);
        return value.Length == 10 && IsValidCedula(value);
    }

    private static bool IsValidCedula(string value)
    {
        var province = int.Parse(value[..2]);
        if (province is not (>= 1 and <= 24) and not 30 || value[2] > '5') return false;
        int[] coefficients = [2, 1, 2, 1, 2, 1, 2, 1, 2];
        var sum = 0;
        for (var index = 0; index < coefficients.Length; index++)
        {
            var product = (value[index] - '0') * coefficients[index];
            sum += product > 9 ? product - 9 : product;
        }
        var verifier = (10 - sum % 10) % 10;
        return verifier == value[9] - '0';
    }
}

public static class EcuadorTax
{
    public static (decimal Base, decimal Tax) FromTaxIncluded(decimal amount, decimal rate = 0.15m)
    {
        var taxableBase = decimal.Round(amount / (1 + rate), 2, MidpointRounding.AwayFromZero);
        return (taxableBase, amount - taxableBase);
    }
}

public sealed class SaleOrder
{
    private readonly List<SaleLine> _lines = [];
    public string State { get; private set; } = "Draft";
    public IReadOnlyList<SaleLine> Lines => _lines;
    public void AddLine(SaleLine line) { EnsureDraft(); _lines.Add(line); }
    public void Confirm() { EnsureDraft(); if (_lines.Count == 0) throw new InvalidOperationException("La venta no puede estar vacía."); State = "Confirmed"; }
    public void MarkAsSynced() { if (State != "Confirmed") throw new InvalidOperationException("Solo una venta confirmada puede sincronizarse."); State = "Synced"; }
    private void EnsureDraft() { if (State != "Draft") throw new InvalidOperationException("Una venta confirmada es inmutable."); }
}

public interface IDiscountPolicy
{
    string Code { get; }
    decimal Calculate(decimal subtotal);
}

public sealed class NoDiscountPolicy : IDiscountPolicy
{
    public string Code => "NONE";
    public decimal Calculate(decimal subtotal) => 0;
}

public sealed class PercentageDiscountPolicy(string code, decimal percentage) : IDiscountPolicy
{
    public string Code { get; } = code;
    public decimal Calculate(decimal subtotal) => decimal.Round(subtotal * percentage, 2,
        MidpointRounding.AwayFromZero);
}
