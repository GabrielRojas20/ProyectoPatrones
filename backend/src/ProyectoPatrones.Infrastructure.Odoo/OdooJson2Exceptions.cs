using System.Net;

namespace ProyectoPatrones.Infrastructure.Odoo;

public class OdooIntegrationException : Exception
{
    public OdooIntegrationException(string message, bool outcomeMayHaveBeenApplied,
        HttpStatusCode? statusCode = null, Exception? innerException = null, bool isTimeout = false)
        : base(message, innerException)
    {
        OutcomeMayHaveBeenApplied = outcomeMayHaveBeenApplied;
        StatusCode = statusCode;
        IsTimeout = isTimeout;
    }

    public bool OutcomeMayHaveBeenApplied { get; }
    public HttpStatusCode? StatusCode { get; }
    public bool IsTimeout { get; }
}
