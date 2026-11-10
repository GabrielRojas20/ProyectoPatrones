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
            [new SaleLine(7, 2, 2.5m, 0)], SaleStatus.Pending, 5m, null, null);
        var result = await gateway.CreateSaleAsync(pending, TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Confirmed, result.Status);
        Assert.Equal("42", result.ExternalId);
        Assert.Equal(("pos.order", "sync_from_ui"), client.Calls.Single());
        Assert.Equal(3, client.LastPayload.GetProperty("orders")[0].GetProperty("session_id").GetInt64());
        Assert.Equal(9, client.LastPayload.GetProperty("orders")[0].GetProperty("payment_ids")[0][2]
            .GetProperty("payment_method_id").GetInt64());
    }

    private sealed class RecordingClient(params object[] responses) : IOdooJson2Client
    {
        private readonly Queue<object> _responses = new(responses);
        public List<(string Model, string Method)> Calls { get; } = [];
        public JsonElement LastPayload { get; private set; }

        public Task<TResponse> CallAsync<TResponse>(string model, string method, object? payload,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((model, method));
            LastPayload = JsonSerializer.SerializeToElement(payload);
            var response = JsonSerializer.SerializeToElement(_responses.Dequeue()).Deserialize<TResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(response!);
        }
    }
}
