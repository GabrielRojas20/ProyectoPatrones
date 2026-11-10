using System.Text.Json;
using System.Text.Json.Serialization;
using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;
using ProyectoPatrones.Infrastructure.Odoo;

namespace ProyectoPatrones.Infrastructure;

public sealed class OdooGateway(IOdooJson2Client client) : IOdooGateway
{
    public async Task<PagedResult<ProductDto>> GetProductsAsync(
        string? name, string? code, int page, int pageSize, CancellationToken ct)
    {
        var domain = ProductDomain(name, code);
        var records = await CallAsync<List<ProductRecord>>("product.product", "search_read", new
        {
            domain,
            fields = new[] { "id", "default_code", "display_name", "lst_price", "available_in_pos", "active" },
            offset = (page - 1) * pageSize,
            limit = pageSize,
            order = "display_name,id"
        }, ct);
        var total = await CallAsync<int>("product.product", "search_count", new { domain }, ct);
        return new(records.Select(MapProduct).ToArray(), page, pageSize, total);
    }

    public async Task<IReadOnlyDictionary<long, ProductDto>> GetProductsByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<long, ProductDto>();
        var records = await CallAsync<List<ProductRecord>>("product.product", "search_read", new
        {
            domain = new object[] { new object[] { "id", "in", ids }, new object[] { "active", "=", true } },
            fields = new[] { "id", "default_code", "display_name", "lst_price", "available_in_pos", "active" },
            limit = ids.Count
        }, ct);
        return records.Select(MapProduct).ToDictionary(x => x.Id);
    }

    public async Task<PosSessionDto?> GetActiveSessionAsync(CancellationToken ct)
    {
        var sessions = await CallAsync<List<SessionRecord>>("pos.session", "search_read", new
        {
            domain = new object[] { new object[] { "state", "=", "opened" } },
            fields = new[] { "id", "name", "state", "payment_method_ids" },
            limit = 1,
            order = "id desc"
        }, ct);
        var session = sessions.FirstOrDefault();
        if (session is null) return null;
        var methods = session.PaymentMethodIds.Length == 0
            ? []
            : await CallAsync<List<PaymentMethodRecord>>("pos.payment.method", "search_read", new
            {
                domain = new object[] { new object[] { "id", "in", session.PaymentMethodIds } },
                fields = new[] { "id", "name" },
                limit = session.PaymentMethodIds.Length
            }, ct);
        return new(session.Id, session.Name, session.State == "opened",
            methods.Select(x => new PaymentMethodDto(x.Id, x.Name)).ToArray());
    }

    public async Task<CustomerDto?> FindCustomerAsync(CustomerRequest customer, CancellationToken ct)
    {
        object[] criterion = !string.IsNullOrWhiteSpace(customer.Identification)
            ? ["vat", "=", customer.Identification]
            : !string.IsNullOrWhiteSpace(customer.Email)
                ? ["email", "=ilike", customer.Email]
                : ["name", "=ilike", customer.Name ?? string.Empty];
        var records = await CallAsync<List<CustomerRecord>>("res.partner", "search_read", new
        {
            domain = new object[] { criterion, new object[] { "active", "=", true } },
            fields = new[] { "id", "name", "vat", "email" },
            limit = 2,
            order = "id"
        }, ct);
        if (records.Count != 1) return null;
        var partner = records[0];
        return new(partner.Id, partner.Name, partner.Identification, partner.Email);
    }

    public async Task<Sale> CreateSaleAsync(Sale sale, CancellationToken ct)
    {
        var uuid = Guid.ParseExact(sale.Id, "N");
        var lines = sale.Lines.Select(line => new object[] { 0, 0, new
        {
            uuid = Guid.NewGuid(),
            product_id = line.ProductId,
            qty = line.Quantity,
            price_unit = line.UnitPrice,
            discount = line.DiscountPercent,
            price_subtotal = line.Subtotal,
            price_subtotal_incl = line.Subtotal,
            tax_ids = Array.Empty<long>()
        }}).ToArray();
        var payments = new object[] { new object[] { 0, 0, new
        {
            uuid = Guid.NewGuid(),
            payment_method_id = sale.PaymentMethodId,
            amount = sale.Total,
            payment_status = "done"
        }}};
        var response = await CallAsync<Dictionary<string, JsonElement>>("pos.order", "sync_from_ui", new
        {
            orders = new[] { new
            {
                uuid,
                session_id = sale.SessionId,
                partner_id = sale.Customer?.PartnerId,
                date_order = sale.OperationDate.ToDateTime(TimeOnly.MinValue),
                state = "paid",
                amount_paid = sale.Total,
                amount_total = sale.Total,
                amount_tax = 0m,
                amount_return = 0m,
                lines,
                payment_ids = payments
            }}
        }, ct);

        if (!response.TryGetValue("pos.order", out var orders) || orders.ValueKind != JsonValueKind.Array)
            throw new IntegrationException("odoo.invalid_sale_response", "Odoo no devolvió la orden POS confirmada.", true);
        var confirmed = orders.EnumerateArray().FirstOrDefault();
        if (confirmed.ValueKind != JsonValueKind.Object || !confirmed.TryGetProperty("id", out var id))
            throw new IntegrationException("odoo.invalid_sale_response", "Odoo no devolvió el identificador de la venta.", true);
        return sale with { Status = SaleStatus.Confirmed, ExternalId = id.ToString(), Message = "Venta confirmada por Odoo." };
    }

    public async Task<PagedResult<Sale>> GetSalesAsync(
        string? name, string? id, int page, int pageSize, CancellationToken ct)
    {
        var domain = SaleDomain(name, id);
        var records = await ReadSalesAsync(domain, (page - 1) * pageSize, pageSize, ct);
        var total = await CallAsync<int>("pos.order", "search_count", new { domain }, ct);
        return new(records, page, pageSize, total);
    }

    public async Task<Sale?> GetSaleAsync(string id, CancellationToken ct)
    {
        var domain = long.TryParse(id, out var numericId)
            ? new object[] { new object[] { "id", "=", numericId } }
            : new object[] { new object[] { "pos_reference", "=", id } };
        return (await ReadSalesAsync(domain, 0, 1, ct)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<Sale>> ReadSalesAsync(object[] domain, int offset, int limit, CancellationToken ct)
    {
        var records = await CallAsync<List<SaleRecord>>("pos.order", "search_read", new
        {
            domain,
            fields = new[] { "id", "pos_reference", "date_order", "session_id", "partner_id", "state", "amount_total", "lines", "payment_ids" },
            offset,
            limit,
            order = "date_order desc,id desc"
        }, ct);
        var results = new List<Sale>(records.Count);
        foreach (var record in records)
        {
            var lines = record.Lines.Length == 0 ? [] : await ReadLinesAsync(record.Lines, ct);
            var paymentMethodId = record.PaymentIds.Length == 0 ? 0 : await ReadPaymentMethodAsync(record.PaymentIds[0], ct);
            var customer = MapCustomer(record.PartnerId);
            results.Add(new(record.Id.ToString(), DateOnly.FromDateTime(record.DateOrder), ManyToOneId(record.SessionId),
                paymentMethodId, lines, customer, MapStatus(record.State), record.AmountTotal, record.Id.ToString(), record.PosReference));
        }
        return results;
    }

    private async Task<IReadOnlyList<SaleLine>> ReadLinesAsync(long[] ids, CancellationToken ct)
    {
        var records = await CallAsync<List<SaleLineRecord>>("pos.order.line", "search_read", new
        {
            domain = new object[] { new object[] { "id", "in", ids } },
            fields = new[] { "product_id", "qty", "price_unit", "discount" },
            limit = ids.Length
        }, ct);
        return records.Select(x => new SaleLine(ManyToOneId(x.ProductId), Convert.ToInt32(x.Quantity), x.UnitPrice, x.Discount)).ToArray();
    }

    private async Task<long> ReadPaymentMethodAsync(long paymentId, CancellationToken ct)
    {
        var records = await CallAsync<List<PaymentRecord>>("pos.payment", "search_read", new
        {
            domain = new object[] { new object[] { "id", "=", paymentId } },
            fields = new[] { "payment_method_id" },
            limit = 1
        }, ct);
        return records.Count == 0 ? 0 : ManyToOneId(records[0].PaymentMethodId);
    }

    private async Task<T> CallAsync<T>(string model, string method, object payload, CancellationToken ct)
    {
        try { return await client.CallAsync<T>(model, method, payload, ct); }
        catch (OdooIntegrationException ex)
        {
            throw new IntegrationException("odoo.request_failed", ex.Message, ex.OutcomeMayHaveBeenApplied, ex);
        }
    }

    private static ProductDto MapProduct(ProductRecord x) =>
        new(x.Id, x.Code ?? string.Empty, x.Name, x.Price, x.Active && x.AvailableInPos);

    private static object[] ProductDomain(string? name, string? code)
    {
        var domain = new List<object> { new object[] { "active", "=", true }, new object[] { "available_in_pos", "=", true } };
        if (!string.IsNullOrWhiteSpace(name)) domain.Add(new object[] { "display_name", "ilike", name });
        if (!string.IsNullOrWhiteSpace(code)) domain.Add(new object[] { "default_code", "ilike", code });
        return domain.ToArray();
    }

    private static object[] SaleDomain(string? name, string? id)
    {
        var domain = new List<object>();
        if (!string.IsNullOrWhiteSpace(name)) domain.Add(new object[] { "partner_id.name", "ilike", name });
        if (!string.IsNullOrWhiteSpace(id))
            domain.Add(long.TryParse(id, out var value)
                ? new object[] { "id", "=", value }
                : new object[] { "pos_reference", "ilike", id });
        return domain.ToArray();
    }

    private static long ManyToOneId(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array when value.GetArrayLength() > 0 => value[0].GetInt64(),
        JsonValueKind.Number => value.GetInt64(),
        _ => 0
    };

    private static SaleCustomer? MapCustomer(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2) return null;
        return new(value[0].GetInt64(), value[1].GetString() ?? string.Empty, null, null);
    }

    private static SaleStatus MapStatus(string state) => state switch
    {
        "paid" or "done" or "invoiced" => SaleStatus.Confirmed,
        "cancel" => SaleStatus.Rejected,
        _ => SaleStatus.Pending
    };

    private sealed record ProductRecord(
        long Id,
        [property: JsonPropertyName("default_code")] string? Code,
        [property: JsonPropertyName("display_name")] string Name,
        [property: JsonPropertyName("lst_price")] decimal Price,
        [property: JsonPropertyName("available_in_pos")] bool AvailableInPos,
        bool Active);
    private sealed record SessionRecord(long Id, string Name, string State,
        [property: JsonPropertyName("payment_method_ids")] long[] PaymentMethodIds);
    private sealed record PaymentMethodRecord(long Id, string Name);
    private sealed record CustomerRecord(long Id, string Name,
        [property: JsonPropertyName("vat")] string? Identification,
        string? Email);
    private sealed record SaleRecord(long Id,
        [property: JsonPropertyName("pos_reference")] string? PosReference,
        [property: JsonPropertyName("date_order")] DateTime DateOrder,
        [property: JsonPropertyName("session_id")] JsonElement SessionId,
        [property: JsonPropertyName("partner_id")] JsonElement PartnerId,
        string State,
        [property: JsonPropertyName("amount_total")] decimal AmountTotal,
        long[] Lines,
        [property: JsonPropertyName("payment_ids")] long[] PaymentIds);
    private sealed record SaleLineRecord(
        [property: JsonPropertyName("product_id")] JsonElement ProductId,
        [property: JsonPropertyName("qty")] decimal Quantity,
        [property: JsonPropertyName("price_unit")] decimal UnitPrice,
        decimal Discount);
    private sealed record PaymentRecord([property: JsonPropertyName("payment_method_id")] JsonElement PaymentMethodId);
}
