using Microsoft.Extensions.DependencyInjection;
using Neodash_Mod_Loader.Services.Core;

namespace Neodash_Mod_Loader.Services;

public static class CoreServiceRegistration
{
    public static IServiceCollection AddModLoaderCore(this IServiceCollection services)
    {
        services.AddSingleton<IAppDiagnostics, AppDiagnostics>();
        services.AddSingleton<IGameService, GameService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IModWorkspace, ModWorkspace>();
        services.AddSingleton<ICodeEditor, VisualStudioCode>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IModCatalog, ModCatalog>();
        services.AddSingleton<IRuntimeService, RuntimeService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IApplicationUpdates, ApplicationUpdates>();
        services.AddSingleton<StartupCoordinator>();
        return services;
    }
}
