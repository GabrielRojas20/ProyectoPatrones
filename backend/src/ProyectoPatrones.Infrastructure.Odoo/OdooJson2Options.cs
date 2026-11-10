namespace ProyectoPatrones.Infrastructure.Odoo;

public sealed class OdooJson2Options
{
    public const string SectionName = "Odoo";

    public string BaseUrl { get; set; } = "";
    public string Database { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 15;
    public string UserAgent { get; set; } = "ProyectoPatrones/1.0";

    public bool IsValid(out string error)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Odoo:BaseUrl debe ser una URL absoluta HTTP o HTTPS.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Database))
        {
            error = "Odoo:Database es obligatorio.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            error = "Odoo:ApiKey es obligatorio.";
            return false;
        }

        if (TimeoutSeconds is < 1 or > 300)
        {
            error = "Odoo:TimeoutSeconds debe estar entre 1 y 300.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            error = "Odoo:UserAgent es obligatorio.";
            return false;
        }

        error = "";
        return true;
    }
}
