using Microsoft.UI.Xaml;
namespace SwitchGlassDemo;
public partial class App:Application
{
    private Window? _window;
    public App()
    {
        InitializeComponent();
        UnhandledException+=(_,args)=>File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"demo-error.log"),args.Exception.ToString());
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window=new MainWindow();_window.Activate();
    }
}
