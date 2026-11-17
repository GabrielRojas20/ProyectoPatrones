using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;
using ProyectoPatrones.Infrastructure;
using Xunit;

namespace ProyectoPatrones.Application.Tests;

public sealed class ApiIntegrationTests : IClassFixture<ApiFactory>
{
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
