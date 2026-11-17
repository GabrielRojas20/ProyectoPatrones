using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;
using ProyectoPatrones.Infrastructure;
using ProyectoPatrones.Infrastructure.Odoo;
using Xunit;

namespace ProyectoPatrones.Application.Tests;

public sealed class ApiIntegrationTests : IClassFixture<ApiFactory>
{
    private const string JwtSecret = "integration-tests-secret-with-at-least-32-bytes";
    private readonly ApiFactory _factory;
    public ApiIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Protected_endpoint_requires_jwt()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/products?page=1&pageSize=10", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_jwt_without_operator_role_returns_forbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ClaimTypes.Role, "cashier"));
        var response = await client.GetAsync("/api/products?page=1&pageSize=10", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_jwt_can_access_protected_endpoint()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("operator", "any"), TestContext.Current.CancellationToken);
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var response = await client.GetAsync("/api/products?page=0&pageSize=10", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Odoo_timeout_returns_504_problem_details()
    {
        using var timeoutFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOdooJson2Client>();
            services.AddSingleton<IOdooJson2Client, TimeoutJson2Client>();
        }));
        using var client = timeoutFactory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("operator", "any"), TestContext.Current.CancellationToken);
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var response = await client.GetAsync("/api/products?page=1&pageSize=10", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(TestContext.Current.CancellationToken);
        Assert.Equal("odoo.timeout", problem!["code"].GetString());
        Assert.Equal(504, problem["status"].GetInt32());
    }

    [Fact]
    public async Task Cors_allows_configured_frontend()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_problem_details()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("operator", "any"), TestContext.Current.CancellationToken);
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var response = await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest(
            DateOnly.FromDateTime(DateTime.Today), 1, 1, [], null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestContext.Current.CancellationToken);
        Assert.Contains("idempotency.required", problem!["code"].ToString());
    }

    [Fact]
    public void Composition_root_registers_real_odoo_gateway()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.IsType<OdooGateway>(scope.ServiceProvider.GetRequiredService<IOdooGateway>());
    }

    private static string CreateToken(string claimType, string claimValue)
    {
        var token = new JwtSecurityToken(
            issuer: "ProyectoPatrones",
            audience: "ProyectoPatrones.Frontend",
            claims: [new Claim(ClaimTypes.Name, "operator"), new Claim(claimType, claimValue)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:JwtSecret", "integration-tests-secret-with-at-least-32-bytes");
        builder.UseSetting("Auth:OperatorUsername", "operator");
        builder.UseSetting("Odoo:BaseUrl", "http://localhost:8069");
        builder.UseSetting("Odoo:Database", "test");
        builder.UseSetting("Odoo:ApiKey", "test-only-key");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        builder.UseSetting("Idempotency:FilePath", Path.Combine(Path.GetTempPath(), $"idempotency-{Guid.NewGuid():N}.json"));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOperatorAuthenticator>();
            services.AddSingleton<IOperatorAuthenticator, AcceptAllAuthenticator>();
        });
    }

    private sealed class AcceptAllAuthenticator : IOperatorAuthenticator
    {
        public bool Verify(string username, string password) => true;
    }
}

public sealed class TimeoutJson2Client : IOdooJson2Client
{
    public Task<TResponse> CallAsync<TResponse>(string model, string method, object? payload, CancellationToken cancellationToken = default) =>
        throw new OdooIntegrationException("Odoo timeout de prueba.", true, isTimeout: true);
}
