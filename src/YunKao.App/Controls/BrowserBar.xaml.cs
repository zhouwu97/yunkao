using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using YunKao.ViewModels;

namespace YunKao.Controls;

public sealed partial class BrowserBar : UserControl
{
    internal void ConnectGlass(LiquidGlassScene scene) => ToolbarGlass.Connect(scene, ToolbarShell);

    public BrowserBar()
    {
        InitializeComponent();
    }

    public BrowserViewModel ViewModel => App.Services.Browser;

    /// <summary>地址栏提交后触发，外壳据此切回工作台。</summary>
    public event EventHandler? Navigated;

    private void OnBarSizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 540;
        ZoomButton.Visibility = ForwardButton.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        ZoomMenu.Visibility = ForwardMenu.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
    }

    public void BeginAddressEdit()
    {
        if (AddressBox.Visibility == Visibility.Collapsed)
        {
            AddressError.Visibility = Visibility.Collapsed;
            AddressBox.Text = ViewModel.FullAddress;
        }
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Focus(FocusState.Programmatic);
        AddressDisplay.Visibility = Visibility.Collapsed;
        AddressBox.SelectAll();
    }

    private void OnAddressClick(object sender, RoutedEventArgs args) => BeginAddressEdit();

    private void OnAddressKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var editor = (TextBox)sender;
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            CloseAddressEditor();
            return;
        }
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        string? error = ViewModel.Navigate(editor.Text);
        if (error is not null)
        {
            AddressError.Text = error;
            AddressError.Visibility = Visibility.Visible;
            return;
        }
        CloseAddressEditor();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    private void CloseAddressEditor()
    {
        AddressDisplay.Visibility = Visibility.Visible;
        AddressDisplay.Focus(FocusState.Programmatic);
        EndAddressEdit();
    }

    private void OnAddressLostFocus(object sender, RoutedEventArgs args) => EndAddressEdit();

    private void EndAddressEdit()
    {
        AddressError.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Collapsed;
        AddressDisplay.Visibility = Visibility.Visible;
    }
}
