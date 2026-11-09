using System.Net;
using System.Text;
using System.Text.Json;
using ProyectoPatrones.Infrastructure.Odoo;

var checks = new (string Name, Func<Task> Run)[]
{
    ("envia POST JSON-2 con autenticacion y base configurada", SendsExpectedRequest),
    ("rechaza respuestas HTTP de Odoo sin indicar exito", RejectsHttpError),
    ("marca los errores 5xx como resultado incierto", MarksServerErrorUncertain),

    ("marca los timeouts como resultado incierto", MarksTimeoutUncertain),
    ("cancela la lectura de una respuesta con cuerpo lento", MarksSlowResponseBodyTimeoutUncertain),
    ("rechaza rutas de modelo o metodo manipuladas", RejectsInvalidPath),
    ("valida configuracion obligatoria", ValidatesOptions)
};

var failures = 0;
foreach (var (name, run) in checks)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
}
Console.WriteLine($"Resultado: {checks.Length - failures}/{checks.Length} verificaciones correctas.");
return failures == 0 ? 0 : 1;

static async Task SendsExpectedRequest()
{
    string? method = null, path = null, scheme = null, key = null, database = null, bodyText = null;
    var handler = new StubHandler(request =>
    {
        method = request.Method.Method; path = request.RequestUri?.AbsolutePath;
        scheme = request.Headers.Authorization?.Scheme; key = request.Headers.Authorization?.Parameter;
        database = request.Headers.GetValues("X-Odoo-Database").Single();
        bodyText = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonResponse("{\"id\":42}");
    });
    var response = await CreateClient(handler).CallAsync<Dictionary<string, int>>("res.partner", "search_read", new { limit = 1 });
    Assert(response["id"] == 42, "No se deserializo la respuesta.");
    Assert(method == "POST", "El metodo HTTP debe ser POST.");
    Assert(path == "/json/2/res.partner/search_read", "La ruta JSON-2 no coincide.");
    Assert(scheme == "Bearer" && key == "local-test-key", "Falta autenticacion Bearer.");
    Assert(database == "pos_demo", "Falta el encabezado de base de datos.");
    using var body = JsonDocument.Parse(bodyText!);
    Assert(body.RootElement.GetProperty("limit").GetInt32() == 1, "El payload no se serializo correctamente.");
}

static async Task RejectsHttpError()
{
    var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("detalle interno") }));
    var exception = await Expect<OdooIntegrationException>(() => client.CallAsync<string>("res.partner", "search_read", new { }));
    Assert(exception.StatusCode == HttpStatusCode.BadRequest, "No se conservo el estado HTTP.");
    Assert(!exception.OutcomeMayHaveBeenApplied, "Un 4xx no debe marcarse como resultado incierto.");
    Assert(!exception.Message.Contains("detalle interno", StringComparison.Ordinal), "No debe divulgar el cuerpo de error.");
}

static async Task MarksServerErrorUncertain()
{
    var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
    var exception = await Expect<OdooIntegrationException>(() => client.CallAsync<string>("pos.order", "create", new { }));
    Assert(exception.OutcomeMayHaveBeenApplied, "Un 5xx debe dejar el resultado como incierto.");
}

static async Task MarksTimeoutUncertain()
{
    var client = new OdooJson2Client(new HttpClient(new DelayedHandler()) { BaseAddress = new Uri("https://odoo.example/") },
        new OdooJson2Options { BaseUrl = "https://odoo.example", Database = "pos_demo", ApiKey = "local-test-key", TimeoutSeconds = 1, UserAgent = "ProyectoPatrones.ContractChecks/1.0" });
    var exception = await Expect<OdooIntegrationException>(() => client.CallAsync<string>("pos.order", "create", new { }));
    Assert(exception.OutcomeMayHaveBeenApplied, "Un timeout debe dejar la operacion para conciliacion.");
}

static async Task RejectsInvalidPath()
{
    var client = CreateClient(new StubHandler(_ => JsonResponse("{}")));
    await Expect<ArgumentException>(() => client.CallAsync<string>("res.partner/../../", "search_read", new { }));
}

static Task ValidatesOptions()
{
    var options = new OdooJson2Options { BaseUrl = "not-a-url", Database = "", ApiKey = "", TimeoutSeconds = 0 };
    Assert(!options.IsValid(out var error) && error.Contains("BaseUrl"), "La URL invalida debe rechazarse primero.");
    return Task.CompletedTask;
}

static OdooJson2Client CreateClient(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://odoo.example/") },
    new OdooJson2Options { BaseUrl = "https://odoo.example", Database = "pos_demo", ApiKey = "local-test-key", TimeoutSeconds = 5, UserAgent = "ProyectoPatrones.ContractChecks/1.0" });
static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
static async Task<TException> Expect<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); } catch (TException exception) { return exception; }
    throw new InvalidOperationException($"Se esperaba {typeof(TException).Name}.");
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
sealed class DelayedHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("La espera debia cancelarse por timeout.");
    }
}


static async Task MarksSlowResponseBodyTimeoutUncertain()
{
    var client = new OdooJson2Client(
        new HttpClient(new SlowBodyHandler()) { BaseAddress = new Uri("https://odoo.example/") },
        new OdooJson2Options
        {
            BaseUrl = "https://odoo.example", Database = "pos_demo", ApiKey = "local-test-key",
            TimeoutSeconds = 1, UserAgent = "ProyectoPatrones.ContractChecks/1.0"
        });

    var call = client.CallAsync<string>("pos.order", "create", new { });
    var completed = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(3)));
    Assert(ReferenceEquals(completed, call), "El timeout debe cancelar la lectura después de recibir los headers.");

    var exception = await Expect<OdooIntegrationException>(() => call);
    Assert(exception.OutcomeMayHaveBeenApplied, "El timeout al leer el cuerpo debe dejar el resultado como incierto.");
}

sealed class SlowBodyHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new StreamContent(new SlowBodyStream());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}

sealed class SlowBodyStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WaitForCancellationAsync(cancellationToken);

    private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}
