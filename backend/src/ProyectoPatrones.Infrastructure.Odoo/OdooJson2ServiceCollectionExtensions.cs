using Microsoft.Extensions.DependencyInjection;

namespace ProyectoPatrones.Infrastructure.Odoo;

public static class OdooJson2ServiceCollectionExtensions
{
    public static IServiceCollection AddOdooJson2(this IServiceCollection services, OdooJson2Options options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid(out var error))
            throw new ArgumentException(error, nameof(options));

        services.AddSingleton(options);
        services.AddHttpClient<IOdooJson2Client, OdooJson2Client>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        return services;
    }
}
