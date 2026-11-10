using System.Text.Json;
using Microsoft.Extensions.Options;
using ProyectoPatrones.Application;

namespace ProyectoPatrones.Infrastructure;

public sealed class IdempotencyOptions
{
    public const string Section = "Idempotency";
    public string FilePath { get; init; } = "data/idempotency.json";
}

public sealed class FileIdempotencyStore(IOptions<IdempotencyOptions> options) : IIdempotencyStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(options.Value.FilePath);

    public async Task<SaleResult?> GetAsync(string key, string requestFingerprint, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var entries = await ReadAsync(ct);
            if (!entries.TryGetValue(key, out var entry)) return null;
            if (!CryptographicEquals(entry.RequestFingerprint, requestFingerprint))
                throw new ProyectoPatrones.Application.ApplicationException("idempotency.key_reused", "La clave de idempotencia ya fue utilizada con una venta diferente.", 409);
            return entry.Result;
        }
        finally { Gate.Release(); }
    }

    public async Task SaveAsync(string key, string requestFingerprint, SaleResult result, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var entries = await ReadAsync(ct);
            if (entries.TryGetValue(key, out var existing) && !CryptographicEquals(existing.RequestFingerprint, requestFingerprint))
                throw new ProyectoPatrones.Application.ApplicationException("idempotency.key_reused", "La clave de idempotencia ya fue utilizada con una venta diferente.", 409);
            entries[key] = new(requestFingerprint, result);
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, ct);
            File.Move(temporary, _path, true);
        }
        finally { Gate.Release(); }
    }

    private async Task<Dictionary<string, Entry>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, Entry>>(stream, JsonOptions, ct)
            ?? new(StringComparer.Ordinal);
    }

    private static bool CryptographicEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(left), System.Text.Encoding.ASCII.GetBytes(right));

    private sealed record Entry(string RequestFingerprint, SaleResult Result);
}
