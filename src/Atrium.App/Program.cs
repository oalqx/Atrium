using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Atrium.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atrium", "startup.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, $"[{DateTime.Now}] Starting Atrium...\n");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            File.AppendAllText(logPath, $"APPDOMAIN UNHANDLED EXCEPTION:\n{e.ExceptionObject}\n");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            File.AppendAllText(logPath, $"UNOBSERVED TASK EXCEPTION:\n{e.Exception}\n");
        };

        try
        {
            File.AppendAllText(logPath, "Initializing ComWrappers...\n");
            WinRT.ComWrappersSupport.InitializeComWrappers();

            File.AppendAllText(logPath, "Running Velopack bootstrap...\n");
            VelopackApp.Build().Run();

            File.AppendAllText(logPath, "Starting Xaml Application...\n");
            Application.Start((p) =>
            {
                try
                {
                    File.AppendAllText(logPath, "Inside Application.Start callback...\n");
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    File.AppendAllText(logPath, "Instantiating App...\n");
                    _ = new App();
                    File.AppendAllText(logPath, "App instantiated successfully.\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"EXCEPTION in Application.Start callback:\n{ex}\n");
                }
            });
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"CRASH in Main:\n{ex}\n");
        }
    }
}
