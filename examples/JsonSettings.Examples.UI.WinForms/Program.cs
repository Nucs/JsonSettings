using System.Windows.Forms;

namespace Nucs.JsonSettings.Examples.UI.WinForms;

internal static class Program {
    [STAThread]
    private static void Main() {
#if NET10_0_OR_GREATER
        // Source-generated on modern .NET: visual styles, DPI mode and default font in one call.
        ApplicationConfiguration.Initialize();
#else
        // The classic pair — this branch IS the point of the net48 target: the same woven
        // settings class running on .NET Framework through the package's netstandard2.0 asset.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#endif
        Application.Run(new MainForm());
    }
}
