using System.Windows.Forms;

namespace TeamsCustomMute;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
}
