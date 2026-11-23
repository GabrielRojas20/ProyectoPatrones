using System.Globalization;
using System.Text.Json;
using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;

namespace ProyectoPatrones.Infrastructure.Odoo;

public sealed class OdooPosGateway(IOdooJson2Client client) : IPosGateway
{
    public string Mode => "ODOO 20 - integración real";

    public async Task<IReadOnlyList<Product>> SearchProductsAsync(string? search, CancellationToken cancellationToken)
    {
        object[] domain = string.IsNullOrWhiteSpace(search)
            ? [new object[] { "available_in_pos", "=", true }]
            : ["&", new object[] { "available_in_pos", "=", true }, "|",
                new object[] { "name", "ilike", search.Trim() },
                new object[] { "default_code", "ilike", search.Trim() }];
        var rows = await client.CallAsync<JsonElement>("product.product", "search_read", new
        {
            domain, fields = new[] { "id", "name", "default_code", "lst_price", "active", "taxes_id" }, limit = 100
        }, cancellationToken);
        return rows.EnumerateArray().Select(row => new Product(
            row.GetProperty("id").GetInt32(),
            Text(row, "default_code", $"ODOO-{row.GetProperty("id").GetInt32()}"),
            Text(row, "name", "Producto"),
            Decimal(row, "lst_price"),
            Bool(row, "active", true), TaxIds: row.GetProperty("taxes_id").EnumerateArray().Select(value => value.GetInt32()).ToArray())).ToArray();
    }

    public async Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken) =>
        (await GetContextAsync(cancellationToken)).SessionId > 0;

    public async Task<Sale> RegisterSaleAsync(Sale draft, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        var uuid = Guid.NewGuid().ToString();
        var discountPercent = draft.Subtotal == 0 ? 0 : decimal.Round(draft.Discount / draft.Subtotal * 100m, 4);
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var lines = draft.Lines.Select(line => new object[] { 0, 0, new
        {
            price_unit = line.UnitPrice, product_id = line.ProductId,
            price_subtotal = EcuadorTax.FromTaxIncluded(decimal.Round(line.UnitPrice * line.Quantity * (1 - discountPercent / 100m), 2)).Base,
            price_subtotal_incl = decimal.Round(line.UnitPrice * line.Quantity * (1 - discountPercent / 100m), 2),
            tax_ids = new object[] { new object[] { 6, false, line.TaxIds ?? Array.Empty<int>() } },
            qty = line.Quantity, discount = discountPercent, uuid = Guid.NewGuid().ToString()
        }}).ToArray();
        var payments = new object[] { new object[] { 0, 0, new
        {
            amount = draft.Total, name = now, payment_method_id = context.PaymentMethodId,
            uuid = Guid.NewGuid().ToString()
        }}};
        object pricelistId = context.PricelistId > 0 ? context.PricelistId : false;
        var fiscal = EcuadorTax.FromTaxIncluded(draft.Total);
        var order = new
        {
            amount_paid = draft.Total, amount_return = 0m, amount_tax = fiscal.Tax, amount_total = draft.Total,
            date_order = now, fiscal_position_id = false, pricelist_id = pricelistId,
            lines, name = $"Order {uuid[..8]}", session_id = context.SessionId, payment_ids = payments,
            uuid, user_id = context.UserId, state = "paid"
        };
        var payload = new Dictionary<string, object> { ["orders"] = new object[] { order } };
        var response = await client.CallAsync<JsonElement>("pos.order", "sync_from_ui", payload, cancellationToken);
        var saved = response.GetProperty("pos.order").EnumerateArray().First();
        var id = saved.GetProperty("id").GetInt32();
        return draft with
        {
            Id = id.ToString(CultureInfo.InvariantCulture), Status = Text(saved, "state", "paid"),
            IntegrationMode = Mode, ExternalId = Text(saved, "pos_reference", Text(saved, "name", $"Odoo #{id}"))
        };
    }

    public async Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken)
    {
        var rows = await client.CallAsync<JsonElement>("pos.order", "search_read", new
        {
            domain = Array.Empty<object>(),
            fields = new[] { "id", "date_order", "state", "amount_total", "amount_paid", "pos_reference", "name" },
            limit = 100, order = "date_order desc"
        }, cancellationToken);
        return rows.EnumerateArray().Select(MapSummary).ToArray();
    }

    public async Task<Sale?> GetSaleAsync(string id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(id, out var numericId)) return null;
        var rows = await client.CallAsync<JsonElement>("pos.order", "search_read", new
        {
            domain = new object[] { new object[] { "id", "=", numericId } },
            fields = new[] { "id", "date_order", "state", "amount_total", "amount_paid", "pos_reference", "name" }, limit = 1
        }, cancellationToken);
        return rows.GetArrayLength() == 0 ? null : MapSummary(rows[0]);
    }

    private async Task<PosContext> GetContextAsync(CancellationToken cancellationToken)
    {
        var sessions = await client.CallAsync<JsonElement>("pos.session", "search_read", new
        {
            domain = new object[] { new object[] { "state", "=", "opened" } },
            fields = new[] { "id", "config_id", "user_id" }, limit = 1
        }, cancellationToken);
        if (sessions.GetArrayLength() == 0) throw new BusinessRuleException("closed_session", "No existe una sesión POS abierta en Odoo.");
        var session = sessions[0];
        var configId = RelationId(session, "config_id");
        var configs = await client.CallAsync<JsonElement>("pos.config", "search_read", new
        {
            domain = new object[] { new object[] { "id", "=", configId } },
            fields = new[] { "payment_method_ids", "pricelist_id" }, limit = 1
        }, cancellationToken);
        var config = configs[0];
        var paymentIds = config.GetProperty("payment_method_ids").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (paymentIds.Length == 0) throw new BusinessRuleException("invalid_payment", "La caja Odoo no tiene métodos de pago configurados.");
        return new PosContext(session.GetProperty("id").GetInt32(), RelationId(config, "pricelist_id"),
            paymentIds[0], RelationId(session, "user_id"));
    }

    private Sale MapSummary(JsonElement row)
    {
        var total = Decimal(row, "amount_total");
        var id = row.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture);
        DateTimeOffset.TryParse(Text(row, "date_order", ""), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var created);
        return new Sale(id, created, Text(row, "state", "unknown"), "Odoo",
            total, 0, total, [], Mode, Text(row, "pos_reference", Text(row, "name", id)));
    }

    private static int RelationId(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.False or JsonValueKind.Null) return 0;
        return value.ValueKind == JsonValueKind.Array ? value[0].GetInt32() : value.GetInt32();
    }
    private static string Text(JsonElement row, string name, string fallback) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static decimal Decimal(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0m;
    private static bool Bool(JsonElement row, string name, bool fallback) =>
        row.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private sealed record PosContext(int SessionId, int PricelistId, int PaymentMethodId, int UserId);
}
