using Microsoft.UI.Xaml;

namespace LiquidGlassDemo;

public partial class App : Application
{
    private Window? _window;
    public App()
    {
        UnhandledException += (_, args) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "demo-error.log"),
            args.Message + "\n" + args.Exception + "\n" + string.Join("\n", args.Exception.Data.Keys.Cast<object>().Select(k => k + ": " + args.Exception.Data[k])));
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
