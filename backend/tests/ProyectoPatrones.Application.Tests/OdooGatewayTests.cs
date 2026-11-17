using System.Text.Json;
using ProyectoPatrones.Domain;
using ProyectoPatrones.Infrastructure;
using ProyectoPatrones.Infrastructure.Odoo;
using Xunit;

namespace ProyectoPatrones.Application.Tests;

public sealed class OdooGatewayTests
{
    [Fact]
    public async Task Product_query_uses_real_json2_contract_and_maps_page()
    {
        var client = new RecordingClient(
            new[] { new { id = 7, default_code = "P-7", display_name = "Café", lst_price = 2.5m, available_in_pos = true, active = true } },
            1);
        var gateway = new OdooGateway(client);
        var result = await gateway.GetProductsAsync("Café", "P-7", 1, 10, TestContext.Current.CancellationToken);
        Assert.Single(result.Items);
        Assert.Equal("P-7", result.Items[0].Code);
        Assert.Equal(2.5m, result.Items[0].Price);
        Assert.Equal(("product.product", "search_read"), client.Calls[0]);
        Assert.Equal(("product.product", "search_count"), client.Calls[1]);
    }

    [Fact]
    public async Task Sale_uses_sync_from_ui_and_requires_confirmed_odoo_identifier()
    {
        var client = new RecordingClient(new Dictionary<string, object>
        {
            ["pos.order"] = new[] { new { id = 42, state = "paid" } }
        });
        var gateway = new OdooGateway(client);
        var pending = new Sale(Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.Today), 3, 9,
            [new SaleLine(7, 2, 2.5m, 0)], new SaleCustomer(21, "Ana Pérez", "1712345678", "ana@example.com"),
            SaleStatus.Pending, 5m, null, null);
        var result = await gateway.CreateSaleAsync(pending, TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Confirmed, result.Status);
        Assert.Equal("42", result.ExternalId);
        Assert.Equal(("pos.order", "sync_from_ui"), client.Calls.Single());
        Assert.Equal(3, client.LastPayload.GetProperty("orders")[0].GetProperty("session_id").GetInt64());
        Assert.Equal(9, client.LastPayload.GetProperty("orders")[0].GetProperty("payment_ids")[0][2]
            .GetProperty("payment_method_id").GetInt64());
        Assert.Equal(21, client.LastPayload.GetProperty("orders")[0].GetProperty("partner_id").GetInt64());
    }

    [Fact]
    public async Task Customer_is_resolved_from_res_partner_by_identification()
    {
        var client = new RecordingClient((object)new[]
        {
            new { id = 21, name = "Ana Pérez", vat = "1712345678", email = "ana@example.com" }
        });
        var gateway = new OdooGateway(client);
        var customer = await gateway.FindCustomerAsync(
            new("1712345678", "ana@example.com", "Ana Pérez"), TestContext.Current.CancellationToken);
        Assert.NotNull(customer);
        Assert.Equal(21, customer.Id);
        Assert.Equal(("res.partner", "search_read"), client.Calls.Single());
        Assert.Equal("vat", client.LastPayload.GetProperty("domain")[0][0].GetString());
    }

    [Fact]
    public async Task Sales_name_filter_uses_partner_relation()
    {
        var client = new RecordingClient(Array.Empty<object>(), 0);
        var gateway = new OdooGateway(client);
        await gateway.GetSalesAsync("Ana", null, 1, 10, TestContext.Current.CancellationToken);
        Assert.Equal("partner_id.name", client.Payloads[0].GetProperty("domain")[0][0].GetString());
    }

    private sealed class RecordingClient(params object[] responses) : IOdooJson2Client
    {
        private readonly Queue<object> _responses = new(responses);
        public List<(string Model, string Method)> Calls { get; } = [];
        public List<JsonElement> Payloads { get; } = [];
        public JsonElement LastPayload { get; private set; }

        public Task<TResponse> CallAsync<TResponse>(string model, string method, object? payload,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((model, method));
            LastPayload = JsonSerializer.SerializeToElement(payload);
            Payloads.Add(LastPayload);
            var response = JsonSerializer.SerializeToElement(_responses.Dequeue()).Deserialize<TResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(response!);
        }
    }
}
