namespace ProyectoPatrones.Infrastructure.Odoo;

public interface IOdooJson2Client
{
    Task<TResponse> CallAsync<TResponse>(string model, string method,
        object? payload, CancellationToken cancellationToken = default);
}
