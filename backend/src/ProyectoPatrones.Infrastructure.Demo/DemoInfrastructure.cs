using System.Collections.Concurrent;
using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;

namespace ProyectoPatrones.Infrastructure.Demo;

public sealed class DemoPosGateway : IPosGateway
{
    private readonly List<Product> _products =
    [
        new(1, "CAF-001", "Café americano", 2.50m, true),
        new(2, "SND-002", "Sándwich de pollo", 4.75m, true),
        new(3, "JUG-003", "Jugo natural", 3.25m, true),
        new(4, "INA-004", "Producto no disponible", 1.00m, false)
    ];
    private readonly ConcurrentDictionary<string, Sale> _sales = new();

    public string Mode => "DEMO - no registrado en Odoo";

    public Task<IReadOnlyList<Product>> SearchProductsAsync(string? search, CancellationToken cancellationToken)
    {
        IEnumerable<Product> result = _products;
        if (!string.IsNullOrWhiteSpace(search))
            result = result.Where(product => product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                product.Code.Contains(search, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<Product>>(result.ToArray());
    }

    public Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<Sale> RegisterSaleAsync(Sale draft, CancellationToken cancellationToken)
    {
        var id = $"DEMO-{Guid.NewGuid():N}"[..17].ToUpperInvariant();
        var sale = draft with { Id = id, Status = "DemoConfirmed", ExternalId = null };
        _sales[id] = sale;
        return Task.FromResult(sale);
    }

    public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Sale>>(_sales.Values.OrderByDescending(sale => sale.CreatedAt).ToArray());

    public Task<Sale?> GetSaleAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_sales.GetValueOrDefault(id));
}

public sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    public Task<Sale?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.TryGetValue(key, out var entry) ? entry.Sale : null);
    public Task<bool> TryReserveAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.TryAdd(key, new Entry(null)));
    public Task CompleteAsync(string key, Sale sale, CancellationToken cancellationToken)
    {
        _entries[key] = new Entry(sale);
        return Task.CompletedTask;
    }
    public Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
    private sealed record Entry(Sale? Sale);
}
