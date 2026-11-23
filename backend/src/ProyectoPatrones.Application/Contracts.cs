using ProyectoPatrones.Domain;

namespace ProyectoPatrones.Application;

public interface IPosGateway
{
    Task<IReadOnlyList<Product>> SearchProductsAsync(string? search, CancellationToken cancellationToken);
    Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken);
    Task<Sale> RegisterSaleAsync(Sale draft, CancellationToken cancellationToken);
    Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken);
    Task<Sale?> GetSaleAsync(string id, CancellationToken cancellationToken);
    string Mode { get; }
}

public interface IIdempotencyStore
{
    Task<Sale?> GetAsync(string key, CancellationToken cancellationToken);
    Task<bool> TryReserveAsync(string key, CancellationToken cancellationToken);
    Task CompleteAsync(string key, Sale sale, CancellationToken cancellationToken);
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}

public sealed record CreateSaleLine(int ProductId, int Quantity);
public sealed record CreateSaleRequest(IReadOnlyList<CreateSaleLine> Lines, string DiscountCode,
    string PaymentMethod, string CustomerDocument = "9999999999999", string CustomerName = "Consumidor Final");

public sealed class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class OperationInProgressException(string message) : Exception(message);
