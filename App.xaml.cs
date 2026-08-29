using Microsoft.UI.Xaml;

namespace ViVeToolApp;

public partial class App : Application
{
    public App()
    {
        this.InitializeComponent();

        this.UnhandledException += (s, e) =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "crash.log");
                System.IO.File.WriteAllText(logPath, $"[UnhandledException] Message: {e.Message}\nException: {e.Exception}\nStackTrace:\n{e.Exception?.StackTrace}");
            }
            catch { /* Best effort */ }
            e.Handled = true;
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "unobserved_task.log");
                System.IO.File.WriteAllText(logPath, $"[UnobservedTaskException] Exception: {e.Exception}\nStackTrace:\n{e.Exception?.StackTrace}");
            }
            catch { /* Best effort */ }
            e.SetObserved();
        };

        System.AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "appdomain_crash.log");
                System.IO.File.WriteAllText(logPath, $"[AppDomainUnhandledException] Object: {e.ExceptionObject}");
            }
            catch { /* Best effort */ }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (System.Exception ex)
        {
            var logPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "crash.log");
            System.IO.File.WriteAllText(logPath, $"[OnLaunched Exception] Message: {ex.Message}\nException: {ex}\nStackTrace:\n{ex.StackTrace}");
            throw;
        }
    }

    private Window? _window;
}
