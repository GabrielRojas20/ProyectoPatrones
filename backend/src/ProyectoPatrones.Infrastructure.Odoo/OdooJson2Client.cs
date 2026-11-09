using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProyectoPatrones.Infrastructure.Odoo;

public sealed class OdooJson2Client(HttpClient httpClient, OdooJson2Options options) : IOdooJson2Client
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> CallAsync<TResponse>(string model, string method,
        object? payload, CancellationToken cancellationToken = default)
    {
        var modelPath = ValidatePathPart(model, nameof(model));
        var methodPath = ValidatePathPart(method, nameof(method));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"json/2/{Uri.EscapeDataString(modelPath)}/{Uri.EscapeDataString(methodPath)}")
        {
            Content = JsonContent.Create(payload ?? new { }, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Odoo-Database", options.Database);
        request.Headers.UserAgent.ParseAdd(options.UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OdooIntegrationException(
                "Odoo no respondió dentro del tiempo configurado; el resultado de la operación es desconocido.",
                outcomeMayHaveBeenApplied: true, innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OdooIntegrationException(
                "No se pudo confirmar la respuesta de Odoo; el resultado de la operación es desconocido.",
                outcomeMayHaveBeenApplied: true, innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var uncertain = (int)response.StatusCode >= 500;
                throw new OdooIntegrationException(
                    $"Odoo respondió HTTP {(int)response.StatusCode}; no se considera la operación exitosa.",
                    outcomeMayHaveBeenApplied: uncertain, response.StatusCode);
            }

            try
            {
                var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, timeout.Token);
                return result ?? throw new OdooIntegrationException(
                    "Odoo devolvió una respuesta vacía; el resultado de la operación es desconocido.",
                    outcomeMayHaveBeenApplied: true, response.StatusCode);
            }
            catch (JsonException exception)
            {
                throw new OdooIntegrationException(
                    "Odoo devolvió una respuesta JSON inválida; el resultado de la operación es desconocido.",
                    outcomeMayHaveBeenApplied: true, response.StatusCode, exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OdooIntegrationException(
                    "Odoo no terminó de responder dentro del tiempo configurado; el resultado es desconocido.",
                    outcomeMayHaveBeenApplied: true, response.StatusCode, exception);
            }
            catch (HttpRequestException exception)
            {
                throw new OdooIntegrationException(
                    "Se perdió la respuesta de Odoo; el resultado de la operación es desconocido.",
                    outcomeMayHaveBeenApplied: true, response.StatusCode, exception);
            }
        }
    }

    private static string ValidatePathPart(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('/'))
            throw new ArgumentException("El modelo y método deben ser nombres simples de Odoo.", parameterName);
        return value;
    }
}
