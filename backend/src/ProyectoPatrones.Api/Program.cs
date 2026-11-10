using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;
using ProyectoPatrones.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.Configure<OdooOptions>(builder.Configuration.GetSection(OdooOptions.Section));

var auth = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new();
if (Encoding.UTF8.GetByteCount(auth.JwtSecret) < 32)
    throw new InvalidOperationException("Auth:JwtSecret debe tener al menos 32 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = auth.Issuer,
        ValidateAudience = true,
        ValidAudience = auth.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.JwtSecret)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IOperatorAuthenticator, Pbkdf2OperatorAuthenticator>();
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddSingleton<IOdooGateway, UnconfiguredOdooGateway>();
builder.Services.AddSingleton<IDiscountPolicy>(_ => new MaximumDiscountPolicy(builder.Configuration.GetValue("Sales:MaximumDiscountPercent", 20m)));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<PosSessionService>();
builder.Services.AddScoped<SaleService>();

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, code, title) = exception switch
    {
        ProyectoPatrones.Application.ApplicationException ex => (ex.StatusCode, ex.Code, ex.Message),
        BusinessRuleException ex => (StatusCodes.Status422UnprocessableEntity, ex.Code, ex.Message),
        IntegrationException ex => (StatusCodes.Status502BadGateway, ex.Code, ex.Message),
        OperationCanceledException => (StatusCodes.Status504GatewayTimeout, "request.timeout", "La operación excedió el tiempo permitido."),
        _ => (StatusCodes.Status500InternalServerError, "server.error", "Ocurrió un error interno.")
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = status,
        Title = title,
        Type = $"https://proyectopatrones/errors/{code}",
        Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
    });
}));
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/login", (LoginRequest request, AuthService service) => Results.Ok(service.Login(request)))
    .AllowAnonymous().WithTags("Auth");
var secured = app.MapGroup("/api").RequireAuthorization();
secured.MapGet("/products", (string? name, string? code, int page, int pageSize, CatalogService service, CancellationToken ct) =>
    service.SearchAsync(name, code, page, pageSize, ct)).WithTags("Products");
secured.MapGet("/pos/sessions/active", (PosSessionService service, CancellationToken ct) => service.GetActiveAsync(ct)).WithTags("POS");
secured.MapPost("/sales", (HttpRequest http, CreateSaleRequest request, SaleService service, CancellationToken ct) =>
    service.CreateAsync(http.Headers["Idempotency-Key"].ToString(), request, ct)).WithTags("Sales");
secured.MapGet("/sales", (string? name, string? id, int page, int pageSize, SaleService service, CancellationToken ct) =>
    service.SearchAsync(name, id, page, pageSize, ct)).WithTags("Sales");
secured.MapGet("/sales/{id}", (string id, SaleService service, CancellationToken ct) => service.GetAsync(id, ct)).WithTags("Sales");

app.MapOpenApi();
app.Run();

public partial class Program;
