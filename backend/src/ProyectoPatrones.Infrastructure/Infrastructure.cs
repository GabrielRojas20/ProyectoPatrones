using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProyectoPatrones.Application;

namespace ProyectoPatrones.Infrastructure;

public sealed class AuthOptions
{
    public const string Section = "Auth";
    public string OperatorUsername { get; init; } = string.Empty;
    public string OperatorPasswordHash { get; init; } = string.Empty;
    public string JwtSecret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "ProyectoPatrones";
    public string Audience { get; init; } = "ProyectoPatrones.Frontend";
    public int TokenMinutes { get; init; } = 60;
}

public sealed class OdooOptions
{
    public const string Section = "Odoo";
    public string BaseUrl { get; init; } = string.Empty;
    public string Database { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 10;
}

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Now; }

public sealed class Pbkdf2OperatorAuthenticator(IOptions<AuthOptions> options) : IOperatorAuthenticator
{
    public bool Verify(string username, string password)
    {
        var settings = options.Value;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(username), Encoding.UTF8.GetBytes(settings.OperatorUsername)))
            return false;
        var parts = settings.OperatorPasswordHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

public sealed class JwtTokenIssuer(IOptions<AuthOptions> options, IClock clock) : ITokenIssuer
{
    public LoginResponse Issue(string username)
    {
        var settings = options.Value;
        var expires = clock.UtcNow.AddMinutes(settings.TokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSecret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, username), new Claim(ClaimTypes.Role, "operator")],
            clock.UtcNow.UtcDateTime, expires.UtcDateTime, credentials);
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, SaleResult> _values = new(StringComparer.Ordinal);
    public Task<SaleResult?> GetAsync(string key, CancellationToken ct)
    {
        _values.TryGetValue(key, out var result);
        return Task.FromResult(result);
    }
    public Task SaveAsync(string key, SaleResult result, CancellationToken ct)
    {
        _values[key] = result;
        return Task.CompletedTask;
    }
}

// Lara reemplazará este puerto por el adaptador Odoo validado. Fallar explícitamente evita presentar datos simulados como reales.
public sealed class UnconfiguredOdooGateway : IOdooGateway
{
    private static IntegrationException Error() => new("odoo.not_configured", "El adaptador Odoo real todavía no está configurado.");
    public Task<PagedResult<ProductDto>> GetProductsAsync(string? name, string? code, int page, int pageSize, CancellationToken ct) => Task.FromException<PagedResult<ProductDto>>(Error());
    public Task<IReadOnlyDictionary<long, ProductDto>> GetProductsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => Task.FromException<IReadOnlyDictionary<long, ProductDto>>(Error());
    public Task<PosSessionDto?> GetActiveSessionAsync(CancellationToken ct) => Task.FromException<PosSessionDto?>(Error());
    public Task<Domain.Sale> CreateSaleAsync(Domain.Sale sale, CancellationToken ct) => Task.FromException<Domain.Sale>(Error());
    public Task<PagedResult<Domain.Sale>> GetSalesAsync(string? name, string? id, int page, int pageSize, CancellationToken ct) => Task.FromException<PagedResult<Domain.Sale>>(Error());
    public Task<Domain.Sale?> GetSaleAsync(string id, CancellationToken ct) => Task.FromException<Domain.Sale?>(Error());
}
