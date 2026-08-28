using CTMS.Application.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace CTMS.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services (use-case orchestrators).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        return services;
    }
}
