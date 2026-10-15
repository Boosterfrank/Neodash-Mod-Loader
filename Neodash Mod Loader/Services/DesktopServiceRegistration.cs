using Microsoft.Extensions.DependencyInjection;

namespace Neodash_Mod_Loader.Services;

public static class DesktopServiceRegistration
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddModLoaderCore();
        services.AddScoped<IWindowContext, WindowContext>();
        services.AddScoped<IUiDispatcher, WindowDispatcher>();
        services.AddScoped<IInstallerLauncher>(provider => provider.GetRequiredService<IWindowContext>());
        services.AddScoped<IUserNotifications, UserNotifications>();
        services.AddScoped<IDesktopDialogs, UiOperations>();
        services.AddScoped<IUserDialogs>(provider => provider.GetRequiredService<IDesktopDialogs>());
        services.AddScoped<IDesktopFiles, UiFiles>();
        services.AddScoped<IConfigurationEditor, UiConfigurationEditor>();
        services.AddScoped<IModEditSession, ModEditSession>();
        services.AddScoped<INavigationGuard, NavigationGuard>();
        services.AddScoped<IPageActivation, PageActivation>();
        services.AddScoped<IAppNavigator, AppNavigator>();
        services.AddScoped<AppUpdateCoordinator>();
        services.AddScoped<GameFolderPicker>();
        services.AddScoped<PreferencesController>();
        services.AddScoped<RuntimeActions>();
        services.AddScoped<DiagnosticActions>();
        services.AddScoped<SettingsPageController>();
        services.AddScoped<OnboardingController>();
        services.AddScoped<ModLibraryController>();
        services.AddScoped<HomeProfileActions>();
        services.AddScoped<HomeModActions>();
        services.AddScoped<HomePageController>();
        services.AddScoped<ShellController>();
        services.AddScoped<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
