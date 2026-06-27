using System.Drawing;
using System.Reflection;

namespace TeamsCustomMute;

/// <summary>Loads the embedded application icon once and shares it across the tray and dialogs.</summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon> Lazy = new(Load);

    public static Icon Value => Lazy.Value;

    private static Icon Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (name is not null)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null)
                    return new Icon(stream);
            }
        }
        catch
        {
            // fall through to the system default
        }
        return SystemIcons.Application;
    }
}
