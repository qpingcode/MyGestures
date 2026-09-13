using Velopack;

namespace MyGestures;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        // Handle installer activation before WPF and the single-instance mutex.
        // Releases are installed in full; no downloaded updates are auto-applied.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
