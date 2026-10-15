using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private readonly IAppDiagnostics _diagnostics;
    private IServiceScope? _windowScope;
    private Window? _mainWindow;
    public App()
    {
        InitializeComponent();
        _services = DesktopServiceRegistration.BuildServices();
        _diagnostics = _services.GetRequiredService<IAppDiagnostics>();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) => _diagnostics.LogCrash(e.Exception);
    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _diagnostics.LogCrash(exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _diagnostics.Log($"Starting Neodash Mod Loader {_diagnostics.Version} (commit {_diagnostics.CommitHash ?? "unavailable"}).");
        _windowScope = _services.CreateScope();
        _mainWindow = _windowScope.ServiceProvider.GetRequiredService<MainWindow>();
        _mainWindow.Closed += MainWindow_Closed;
        _mainWindow.Activate();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        UnhandledException -= App_UnhandledException;
        _windowScope?.Dispose();
        _windowScope = null;
        _mainWindow = null;
        _services.Dispose();
    }
}
