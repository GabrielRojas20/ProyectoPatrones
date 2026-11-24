using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;
using Xunit;

namespace ProyectoPatrones.Application.Tests;

public sealed class SaleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public async Task P01_Rejects_sale_without_products()
    {
        var (service, gateway) = CreateService();
        var request = Request([]);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync("key-1", request, TestContext.Current.CancellationToken));
        Assert.Equal("sale.empty", error.Code);
        Assert.Equal(0, gateway.CreateCalls);
    }

    [Fact]
    public async Task P02_Rejects_non_positive_quantity()
    {
        var (service, gateway) = CreateService();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync("key-2", Request([new(1, 0, 10)]), TestContext.Current.CancellationToken));
        Assert.Equal("sale.invalid_quantity", error.Code);
        Assert.Equal(0, gateway.CreateCalls);
    }

    [Fact]
    public async Task Rejects_quantity_above_40()
    {
        var (service, gateway) = CreateService();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync("key-max-qty", Request([new(1, 41, 10)]), TestContext.Current.CancellationToken));
        Assert.Equal("sale.invalid_quantity", error.Code);
        Assert.Equal(0, gateway.CreateCalls);
    }

    [Fact]
    public async Task Rejects_invalid_customer_name_and_accepts_plus_email()
    {
        var (service, gateway) = CreateService();
        var invalidName = Request([new(1, 1, 10)]) with { Customer = new("1712345678", "user@example.com", "Ana123 Pérez") };
        var nameError = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync("key-name", invalidName, TestContext.Current.CancellationToken));
        Assert.Equal("customer.invalid_name", nameError.Code);

        var tooLongEmail = Request([new(1, 1, 10)]) with { Customer = new("1712345678", $"{new string('a', 30)}@example.com", "Ana Pérez") };
        var emailError = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync("key-long-email", tooLongEmail, TestContext.Current.CancellationToken));
        Assert.Equal("customer.invalid_email", emailError.Code);

        var plusEmail = Request([new(1, 1, 10)]) with { Customer = new("1712345678", "ana+ventas@ejemplo.com", "Ana Pérez") };
        var result = await service.CreateAsync("key-email-plus", plusEmail, TestContext.Current.CancellationToken);
        Assert.Equal("confirmed", result.Status);
        Assert.Equal(1, gateway.CreateCalls);
    }

    [Fact]
    public async Task Concurrent_sales_cannot_sell_the_same_last_unit_twice()
    {
        var (service, gateway) = CreateService(stock: 1);
        gateway.DecrementStockOnCreate = false;
        var first = service.CreateAsync("stock-first", Request([new(1, 1, 10)]), TestContext.Current.CancellationToken);
        var second = service.CreateAsync("stock-second", Request([new(1, 1, 10)]), TestContext.Current.CancellationToken);
        var outcomes = await Task.WhenAll(first.ContinueWith(task => (task.IsCompletedSuccessfully, task.Exception?.GetBaseException())),
            second.ContinueWith(task => (task.IsCompletedSuccessfully, task.Exception?.GetBaseException())));
        Assert.Single(outcomes.Where(x => x.Item1));
        Assert.Equal("sale.insufficient_stock", Assert.IsType<ApplicationException>(outcomes.Single(x => !x.Item1).Item2).Code);
        Assert.Equal(1, gateway.CreateCalls);
    }

    [Fact]
    public async Task P03_Rejects_closed_session()
    {
        var (service, gateway) = CreateService(sessionOpen: false);
        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync("key-3", Request([new(1, 1, 10)]), TestContext.Current.CancellationToken));
        Assert.Equal("pos.invalid_session", error.Code);
        Assert.Equal(0, gateway.CreateCalls);
    }

    [Fact]
    public async Task P04_Rejects_unavailable_product_before_sending()
    {
        var (service, gateway) = CreateService(productAvailable: false);
        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync("key-4", Request([new(1, 1, 10)]), TestContext.Current.CancellationToken));
        Assert.Equal("sale.product_unavailable", error.Code);
        Assert.Equal(0, gateway.CreateCalls);
    }

    [Fact]
    public async Task Rejects_customer_that_does_not_exist_in_odoo()
    {
        var (service, gateway) = CreateService();
        gateway.CustomerExists = false;
        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync(
            "missing-customer", Request([new(1, 1, 10)]), TestContext.Current.CancellationToken));
        Assert.Equal("customer.not_found", error.Code);
        Assert.Equal(0, gateway.CreateCalls);
    }

    [Fact]
    public async Task P05_Does_not_claim_success_when_Odoo_fails()
    {
        var (service, gateway) = CreateService();
        gateway.CreateError = new IntegrationException("odoo.timeout", "timeout");
        await Assert.ThrowsAsync<IntegrationException>(() => service.CreateAsync("key-5", Request([new(1, 1, 10)]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task P06_Repeated_key_returns_same_sale_without_duplicate()
    {
        var (service, gateway) = CreateService();
        var request = Request([new(1, 2, 10)]);
        var first = await service.CreateAsync("same-key", request, TestContext.Current.CancellationToken);
        var second = await service.CreateAsync("same-key", request, TestContext.Current.CancellationToken);
        Assert.Equal(first.Id, second.Id);
        Assert.True(second.Replayed);
        Assert.Equal(1, gateway.CreateCalls);
    }

    [Fact]
    public async Task Reused_key_with_different_sale_is_rejected()
    {
        var (service, gateway) = CreateService();
        await service.CreateAsync("conflicting-key", Request([new(1, 1, 10)]), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync(
            "conflicting-key", Request([new(1, 2, 10)]), TestContext.Current.CancellationToken));
        Assert.Equal("idempotency.key_reused", error.Code);
        Assert.Equal(1, gateway.CreateCalls);
    }

    [Fact]
    public async Task Rejects_price_changed_by_Odoo()
    {
        var (service, gateway) = CreateService();
        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync("key-price", Request([new(1, 1, 9)]), TestContext.Current.CancellationToken));
        Assert.Equal("sale.price_changed", error.Code);
        Assert.Equal(0, gateway.CreateCalls);
    }

    [Fact]
    public async Task P10_Discount_policy_can_change_without_modifying_use_case()
    {
        var gateway = new FakeGateway();
        var service = new SaleService(gateway, new MemoryStore(), new MaximumDiscountPolicy(5), new FakeClock());
        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.CreateAsync("key-discount", Request([new(1, 1, 10, 10)]), TestContext.Current.CancellationToken));
        Assert.Equal("sale.discount_not_authorized", error.Code);
    }

    private static (SaleService Service, FakeGateway Gateway) CreateService(bool sessionOpen = true, bool productAvailable = true, decimal stock = 100)
    {
        var gateway = new FakeGateway { SessionOpen = sessionOpen, ProductAvailable = productAvailable, Stock = stock };
        return (new(gateway, new MemoryStore(), new MaximumDiscountPolicy(20), new FakeClock()), gateway);
    }

    private static CreateSaleRequest Request(IReadOnlyList<CreateSaleLineRequest> lines) =>
        new(DateOnly.FromDateTime(Now.DateTime), 100, 200, lines, new("1712345678", "user@example.com", "José Vaca"));

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class MemoryStore : IIdempotencyStore
    {
        private readonly Dictionary<string, SaleResult> _items = [];
        private readonly Dictionary<string, string> _fingerprints = [];
        public Task<SaleResult?> GetAsync(string key, string requestFingerprint, CancellationToken ct)
        {
            if (_fingerprints.TryGetValue(key, out var existing) && existing != requestFingerprint)
                throw new ApplicationException("idempotency.key_reused", "conflict", 409);
            return Task.FromResult(_items.GetValueOrDefault(key));
        }
        public Task SaveAsync(string key, string requestFingerprint, SaleResult result, CancellationToken ct)
        {
            _fingerprints[key] = requestFingerprint;
            _items[key] = result;
            return Task.CompletedTask;
        }
    }
    private sealed class FakeGateway : IOdooGateway
    {
        public bool SessionOpen { get; init; } = true;
        public bool ProductAvailable { get; init; } = true;
        public bool CustomerExists { get; set; } = true;
        public bool DecrementStockOnCreate { get; set; } = true;
        public decimal Stock { get; set; } = 100;
        public int CreateCalls { get; private set; }
        public Exception? CreateError { get; set; }
        public Task<PosSessionDto?> GetActiveSessionAsync(CancellationToken ct) => Task.FromResult<PosSessionDto?>(new(100, "Main", SessionOpen, [new(200, "Cash")]));
        public Task<CustomerDto?> FindCustomerAsync(CustomerRequest customer, CancellationToken ct) =>
            Task.FromResult(CustomerExists
                ? new CustomerDto(300, customer.Name ?? "Customer", customer.Identification, customer.Email)
                : null);
        public Task<IReadOnlyDictionary<long, ProductDto>> GetProductsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<long, ProductDto>>(ids.ToDictionary(x => x, x => new ProductDto(x, $"P{x}", "Product", 10, ProductAvailable, Stock)));
        public Task<Sale> CreateSaleAsync(Sale sale, CancellationToken ct)
        {
            CreateCalls++;
            if (CreateError is not null) return Task.FromException<Sale>(CreateError);
            if (DecrementStockOnCreate)
                foreach (var line in sale.Lines) Stock -= line.Quantity;
            return Task.FromResult(sale with { Status = SaleStatus.Confirmed, ExternalId = "ODOO-42" });
        }
        public Task<PagedResult<CustomerDto>> SearchCustomersAsync(string name, int page, int pageSize, CancellationToken ct) => Task.FromResult(new PagedResult<CustomerDto>([], page, pageSize, 0));
        public Task<PagedResult<ProductDto>> GetProductsAsync(string? name, string? code, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<PagedResult<Sale>> GetSalesAsync(string? name, string? id, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<Sale?> GetSaleAsync(string id, CancellationToken ct) => throw new NotImplementedException();
    }
}
