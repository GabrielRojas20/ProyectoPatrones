using ProyectoPatrones.Domain;

namespace ProyectoPatrones.Application;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => Total == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);
}

public sealed record ProductDto(long Id, string Code, string Name, decimal Price, bool Available);
public sealed record PosSessionDto(long Id, string Name, bool IsOpen, IReadOnlyList<PaymentMethodDto> PaymentMethods);
public sealed record PaymentMethodDto(long Id, string Name);
public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string TokenType = "Bearer");
public sealed record CreateSaleLineRequest(long ProductId, int Quantity, decimal? ExpectedUnitPrice, decimal DiscountPercent = 0);
public sealed record CustomerRequest(string? Identification, string? Email, string? Name);
public sealed record CreateSaleRequest(DateOnly OperationDate, long SessionId, long PaymentMethodId,
    IReadOnlyList<CreateSaleLineRequest> Lines, CustomerRequest? Customer);
public sealed record SaleResult(string Id, string Status, decimal Total, string? ExternalId, string? Message, bool Replayed = false);

public interface IOdooGateway
{
    Task<PagedResult<ProductDto>> GetProductsAsync(string? name, string? code, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyDictionary<long, ProductDto>> GetProductsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<PosSessionDto?> GetActiveSessionAsync(CancellationToken ct);
    Task<Sale> CreateSaleAsync(Sale sale, CancellationToken ct);
    Task<PagedResult<Sale>> GetSalesAsync(string? name, string? id, int page, int pageSize, CancellationToken ct);
    Task<Sale?> GetSaleAsync(string id, CancellationToken ct);
}

public interface IIdempotencyStore
{
    Task<SaleResult?> GetAsync(string key, CancellationToken ct);
    Task SaveAsync(string key, SaleResult result, CancellationToken ct);
}

public interface IOperatorAuthenticator { bool Verify(string username, string password); }
public interface ITokenIssuer { LoginResponse Issue(string username); }
public interface IClock { DateTimeOffset UtcNow { get; } }
public interface IDiscountPolicy { bool IsAuthorized(decimal discountPercent); }

public sealed class ApplicationException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class IntegrationException(string code, string message, bool uncertain = false, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public bool Uncertain { get; } = uncertain;
}
