using Microsoft.UI.Xaml.Controls;
using YunKao.Services;

namespace YunKao.Views;

public sealed partial class WorkspacePage : Page
{
    private ExtractionCoordinator? _coordinator;

    public WorkspacePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        _coordinator ??= new ExtractionCoordinator(
            App.Services,
            Browser,
            Extraction,
            Progress,
            AiStatus,
            Events);
    }

    private async void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (_coordinator is null) return;
        await _coordinator.DisposeAsync();
        _coordinator = null;
    }
}
