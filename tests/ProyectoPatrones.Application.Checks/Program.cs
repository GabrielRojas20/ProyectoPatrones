using ProyectoPatrones.Application;
using ProyectoPatrones.Domain;
using ProyectoPatrones.Infrastructure.Demo;

var checks = new (string Name, Func<Task> Run)[]
{
    ("P01 rechaza venta sin productos", () => Rejects("empty_sale", [], "NONE")),
    ("P02 rechaza cantidad no positiva", () => Rejects("invalid_quantity", [new(1, 0)], "NONE")),
    ("P03 rechaza sesión POS cerrada", RejectsClosedSession),
    ("P04 rechaza producto no habilitado sin enviarlo", RejectsUnavailableProduct),
    ("P06 evita duplicar una venta repetida", IsIdempotent),
    ("P10 permite cambiar política sin cambiar el caso de uso", SupportsNewPolicy),
    ("RN-04 rechaza descuento no autorizado", () => Rejects("unauthorized_discount", [new(1, 1)], "ROOT99")),
    ("RF-04 valida cédula, RUC y consumidor final", ValidatesEcuadorianIds),
    ("RF-05 desglosa IVA 15% incluido", CalculatesIncludedTax),
    ("RN-06 impide mutar una orden confirmada", EnforcesOrderState)
};
var failed = 0;
foreach (var (name, run) in checks) try { await run(); Console.WriteLine($"PASS {name}"); }
catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
Console.WriteLine($"Resultado: {checks.Length - failed}/{checks.Length} verificaciones correctas.");
return failed == 0 ? 0 : 1;

static PosService Service(FakeGateway gateway, params IDiscountPolicy[] additional) => new(gateway,
    new MemoryIdempotencyStore(), [new NoDiscountPolicy(), new PercentageDiscountPolicy("DOCENTE10", .10m), .. additional]);

static async Task Rejects(string expectedCode, IReadOnlyList<CreateSaleLine> lines, string discount)
{
    var gateway = new FakeGateway();
    var exception = await Expect<BusinessRuleException>(() => Service(gateway).CreateSaleAsync(
        new(lines, discount, "cash"), Guid.NewGuid().ToString(), default));
    Assert(exception.Code == expectedCode, $"Se esperaba {expectedCode} y llegó {exception.Code}.");
}

static async Task RejectsClosedSession()
{
    var gateway = new FakeGateway { Active = false };
    var exception = await Expect<BusinessRuleException>(() => Service(gateway).CreateSaleAsync(
        new([new(1, 1)], "NONE", "cash"), "closed", default));
    Assert(exception.Code == "closed_session" && gateway.RegisterCount == 0, "No debe registrar sin sesión.");
}

static async Task RejectsUnavailableProduct()
{
    var gateway = new FakeGateway();
    var exception = await Expect<BusinessRuleException>(() => Service(gateway).CreateSaleAsync(
        new([new(2, 1)], "NONE", "cash"), "unavailable", default));
    Assert(exception.Code == "unavailable_product" && gateway.RegisterCount == 0, "No debe enviar el producto inactivo.");
}

static async Task IsIdempotent()
{
    var gateway = new FakeGateway(); var service = Service(gateway);
    var request = new CreateSaleRequest([new(1, 2)], "NONE", "cash");
    var first = await service.CreateSaleAsync(request, "same-key", default);
    var second = await service.CreateSaleAsync(request, "same-key", default);
    Assert(first.Id == second.Id && gateway.RegisterCount == 1, "Una clave repetida generó otra venta.");
}

static async Task SupportsNewPolicy()
{
    var gateway = new FakeGateway();
    var sale = await Service(gateway, new PercentageDiscountPolicy("TEMP15", .15m)).CreateSaleAsync(
        new([new(1, 2)], "TEMP15", "cash"), "new-policy", default);
    Assert(sale.Discount == 3m && sale.Total == 17m, "La política extensible no se aplicó.");
    Assert(sale.TaxableBase == 14.78m && sale.Tax == 2.22m,
        "El descuento no se distribuyó correctamente entre base e IVA.");
}

static Task ValidatesEcuadorianIds()
{
    Assert(EcuadorianId.IsValid("1804283537"), "La cédula válida fue rechazada.");
    Assert(EcuadorianId.IsValid("1804283537001"), "El RUC de persona natural fue rechazado.");
    Assert(EcuadorianId.IsValid(EcuadorianId.FinalConsumer), "Consumidor final fue rechazado.");
    Assert(!EcuadorianId.IsValid("1804283536"), "Se aceptó una cédula con verificador incorrecto.");
    return Task.CompletedTask;
}

static Task CalculatesIncludedTax()
{
    var result = EcuadorTax.FromTaxIncluded(115m);
    Assert(result.Base == 100m && result.Tax == 15m, "El desglose fiscal no coincide con IVA 15%.");
    return Task.CompletedTask;
}

static Task EnforcesOrderState()
{
    var order = new SaleOrder();
    order.AddLine(new SaleLine(1, "A", "Producto", 1, 1m, 0, 1m));
    order.Confirm();
    try { order.AddLine(new SaleLine(2, "B", "Otro", 1, 1m, 0, 1m)); }
    catch (InvalidOperationException) { order.MarkAsSynced(); Assert(order.State == "Synced", "No transitó a Synced."); return Task.CompletedTask; }
    throw new Exception("La orden confirmada aceptó una mutación.");
}

static async Task<T> Expect<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T exception) { return exception; } throw new Exception($"Se esperaba {typeof(T).Name}."); }
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

sealed class FakeGateway : IPosGateway
{
    public bool Active { get; set; } = true; public int RegisterCount { get; private set; }
    public string Mode => "TEST";
    public Task<IReadOnlyList<Product>> SearchProductsAsync(string? search, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<Product>>([new(1, "OK", "Disponible", 10m, true), new(2, "NO", "Inactivo", 5m, false)]);
    public Task<bool> HasActiveSessionAsync(CancellationToken token) => Task.FromResult(Active);
    public Task<Sale> RegisterSaleAsync(Sale draft, CancellationToken token) { RegisterCount++; return Task.FromResult(draft with { Id = $"T-{RegisterCount}", Status = "Confirmed" }); }
    public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<Sale>>([]);
    public Task<Sale?> GetSaleAsync(string id, CancellationToken token) => Task.FromResult<Sale?>(null);
}
