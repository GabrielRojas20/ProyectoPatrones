using System.Net;

namespace ProyectoPatrones.Infrastructure.Odoo;

public class OdooIntegrationException : Exception
{
    public OdooIntegrationException(string message, bool outcomeMayHaveBeenApplied,
        HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        OutcomeMayHaveBeenApplied = outcomeMayHaveBeenApplied;
        StatusCode = statusCode;
    }

    public bool OutcomeMayHaveBeenApplied { get; }
    public HttpStatusCode? StatusCode { get; }
}
