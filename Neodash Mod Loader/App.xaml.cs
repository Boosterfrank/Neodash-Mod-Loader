using Microsoft.UI.Xaml;

namespace Neodash_Mod_Loader
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            this.UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash(ex);
            }
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neodash Mod Loader");
                Directory.CreateDirectory(logDirectory);
                string logPath = Path.Combine(logDirectory, "crash.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now}] {ex.Message}\n{ex.StackTrace}\n\n");
            }
            catch
            {
                // Crash logging is best effort: a logging failure must not replace the original unhandled exception.
            }
        }

        private static Window? _mainWindow;
        public static Window MainWindow => _mainWindow ?? throw new InvalidOperationException("The main window has not been created yet.");

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            LaunchMainWindow();
        }

        private static void LaunchMainWindow()
        {
            _mainWindow = new MainWindow();
            _mainWindow.Activate();
        }
    }
}
