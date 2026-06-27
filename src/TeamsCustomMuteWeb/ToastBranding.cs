using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace TeamsCustomMute;

/// <summary>
/// Windows shows the icon and display name in a toast/notification header based on the
/// calling process's AppUserModelID and its matching Start-menu shortcut — not the tray
/// icon. Without this, toasts fall back to the raw exe name and a generic icon. This helper
/// registers an explicit AppUserModelID and installs a Start-menu shortcut (pointing at the
/// app's own icon) so notifications are branded with the app logo and a friendly name.
/// </summary>
internal static class ToastBranding
{
    // Stable, app-unique id. Format: CompanyName.ProductName.
    private const string AppId = "Vermaat.TeamsCustomMute";

    // The toast header text comes from the shortcut file name (without extension).
    private const string ShortcutName = "Teams Custom Mute";

    public static void Apply()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            EnsureShortcut();
        }
        catch
        {
            // Branding is best-effort; the app works fine without it.
        }
    }

    private static void EnsureShortcut()
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcutPath = Path.Combine(startMenu, ShortcutName + ".lnk");

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return;

        if (File.Exists(shortcutPath) && ShortcutIsCurrent(shortcutPath, exePath))
            return;

        Directory.CreateDirectory(startMenu);

        var link = (IShellLinkW)new CShellLink();
        link.SetPath(exePath);
        link.SetIconLocation(exePath, 0);
        link.SetArguments(string.Empty);
        link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);

        // Stamp the AppUserModelID onto the shortcut so Windows associates this process's
        // notifications with it.
        var store = (IPropertyStore)link;
        var key = PKEY_AppUserModel_ID;
        var pv = new PROPVARIANT { vt = VT_LPWSTR, p = Marshal.StringToCoTaskMemUni(AppId) };
        try
        {
            store.SetValue(ref key, ref pv);
            store.Commit();
        }
        finally
        {
            // Frees the LPWSTR allocated above via CoTaskMemFree.
            PropVariantClear(ref pv);
        }

        ((IPersistFile)link).Save(shortcutPath, true);
    }

    private static bool ShortcutIsCurrent(string shortcutPath, string exePath)
    {
        try
        {
            var link = (IShellLinkW)new CShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var sb = new System.Text.StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return string.Equals(sb.ToString(), exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private const ushort VT_LPWSTR = 31;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    // PKEY_AppUserModel_ID
    private static PROPERTYKEY PKEY_AppUserModel_ID => new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p;
        public IntPtr p2;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }
}
