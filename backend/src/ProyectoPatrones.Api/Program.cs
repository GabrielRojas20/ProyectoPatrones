using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;
using ProyectoPatrones.Infrastructure.Demo;
using ProyectoPatrones.Infrastructure.Odoo;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader().AllowAnyMethod()));
var posMode = builder.Configuration["Pos:Mode"] ?? "Odoo";
if (string.Equals(posMode, "Demo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPosGateway, DemoPosGateway>();
}
else
{
    var odooOptions = builder.Configuration.GetSection(OdooJson2Options.SectionName)
        .Get<OdooJson2Options>() ?? new OdooJson2Options();
    builder.Services.AddOdooJson2(odooOptions);
    builder.Services.AddSingleton<IPosGateway, OdooPosGateway>();
}
builder.Services.AddSingleton<IIdempotencyStore, MemoryIdempotencyStore>();
builder.Services.AddSingleton<IDiscountPolicy, NoDiscountPolicy>();
builder.Services.AddSingleton<IDiscountPolicy>(_ => new PercentageDiscountPolicy("DOCENTE10", 0.10m));
builder.Services.AddSingleton<PosService>();

var app = builder.Build();
var frontendPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "frontend/pos"));
if (!Directory.Exists(frontendPath))
    frontendPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../../frontend/pos"));
if (!Directory.Exists(frontendPath)) throw new DirectoryNotFoundException($"No se encontró el frontend en {frontendPath}");
var frontendFiles = new PhysicalFileProvider(frontendPath);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontendFiles });
app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendFiles });
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title, code) = error switch
    {
        BusinessRuleException business when business.Code == "sale_not_found" =>
            (StatusCodes.Status404NotFound, business.Message, business.Code),
        BusinessRuleException business =>
            (StatusCodes.Status400BadRequest, business.Message, business.Code),
        OperationInProgressException progress =>
            (StatusCodes.Status409Conflict, progress.Message, "operation_in_progress"),
        OdooIntegrationException odoo when odoo.StatusCode is System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.BadRequest =>
            (StatusCodes.Status422UnprocessableEntity, odoo.Message, "odoo_rejected_operation"),
        OdooIntegrationException odoo when odoo.StatusCode is System.Net.HttpStatusCode.GatewayTimeout || odoo.InnerException is TimeoutException =>
            (StatusCodes.Status504GatewayTimeout, odoo.Message, "odoo_timeout"),
        OdooIntegrationException odoo =>
            (StatusCodes.Status502BadGateway, odoo.Message, odoo.OutcomeMayHaveBeenApplied ? "odoo_uncertain_result" : "odoo_error"),
        _ => (StatusCodes.Status500InternalServerError, "Ocurrió un error inesperado.", "unexpected_error")
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = status, Title = title, Type = $"https://proyectopatrones.local/problems/{code}"
    });
}));
app.UseCors();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Path.StartsWithSegments("/api/auth/login") &&
        !context.Request.Path.StartsWithSegments("/api/status"))
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!string.Equals(authorization, "Bearer demo-pos-token", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 401, Title = "Se requiere una sesión válida.",
                Type = "https://proyectopatrones.local/problems/unauthorized"
            });
            return;
        }
    }
    await next();
});

app.MapGet("/api/status", (IPosGateway gateway) => Results.Ok(new
{
    status = "ready", integrationMode = gateway.Mode,
    warning = gateway.Mode.Contains("demo", StringComparison.OrdinalIgnoreCase)
        ? "El modo demo permite validar el flujo, pero no registra operaciones en Odoo."
        : null
}));

app.MapPost("/api/auth/login", (LoginRequest request) =>
{
    if (request.User is not { Length: > 0 and <= 40 } ||
        request.Password is not { Length: > 0 and <= 80 })
        return Results.BadRequest(new ProblemDetails { Status = 400, Title = "Credenciales inválidas." });
    if (!string.Equals(request.User, "operator", StringComparison.OrdinalIgnoreCase) || request.Password != "pos-demo")
        return Results.Unauthorized();
    return Results.Ok(new { token = "demo-pos-token", operatorName = "Operador Demo", mode = "DEMO" });
});

app.MapGet("/api/products", async (string? search, PosService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.SearchProductsAsync(search, cancellationToken)));

app.MapGet("/api/pos/sessions/active", async (IPosGateway gateway, CancellationToken cancellationToken) =>
    Results.Ok(new { active = await gateway.HasActiveSessionAsync(cancellationToken), mode = gateway.Mode }));

app.MapPost("/api/sales", async (HttpRequest httpRequest, CreateSaleRequest request,
    PosService service, CancellationToken cancellationToken) =>
{
    var key = httpRequest.Headers["Idempotency-Key"].ToString();
    var sale = await service.CreateSaleAsync(request, key, cancellationToken);
    return Results.Created($"/api/sales/{sale.Id}", sale);
});

app.MapGet("/api/sales", async (PosService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetSalesAsync(cancellationToken)));

app.MapGet("/api/sales/{id}", async (string id, PosService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetSaleAsync(id, cancellationToken)));

app.Run();

public sealed record LoginRequest(string User, string Password);

public partial class Program;
