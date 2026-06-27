using System.Windows.Forms;

namespace TeamsCustomMute;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Hidden support mode: exercise the Teams web sign-in once, write the result and any
        // exception to the log file, then exit. Run from the install folder:
        //   TeamsCustomMuteWeb.exe --diagnose-signin
        if (Environment.GetCommandLineArgs().Contains("--diagnose-signin"))
        {
            RunSignInDiagnostic();
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, "TeamsCustomMuteWeb.SingleInstance", out var isNew);
        if (!isNew)
            return;

        ApplicationConfiguration.Initialize();

        // Brand toast/notification headers with the app icon and a friendly name.
        ToastBranding.Apply();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        Application.Run(new TrayAppContext());
    }

    private static void ShowFatal(Exception? ex)
    {
        try
        {
            MessageBox.Show(ex?.Message ?? "Unknown error", "Teams Custom Mute (Web)",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // nothing else we can do
        }
    }

    private static void RunSignInDiagnostic()
    {
        Log.Info("=== diagnose-signin start ===");
        var controller = new TeamsWebController();
        try
        {
            // Mimic the real app: a headless background context starts first, then the user
            // triggers an interactive (headful) sign-in which tears that down and relaunches.
            var initial = controller.IsSignedInAsync().GetAwaiter().GetResult();
            Log.Info($"diagnose-signin: initial IsSignedIn={initial}");
            var ok = controller.SignInInteractiveAsync(TimeSpan.FromSeconds(25)).GetAwaiter().GetResult();
            Log.Info($"diagnose-signin result: ok={ok}");
        }
        catch (Exception ex)
        {
            Log.Error("diagnose-signin threw", ex);
        }
        finally
        {
            try { controller.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* ignore */ }
            Log.Info("=== diagnose-signin end ===");
        }
    }
}
