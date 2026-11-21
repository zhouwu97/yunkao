using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using YunKao.ViewModels;

namespace YunKao.Controls;

public sealed partial class BrowserBar : UserControl
{
    private bool _compact;

    public BrowserBar()
    {
        InitializeComponent();
    }

    public BrowserViewModel ViewModel => App.Services.Browser;

    /// <summary>地址栏提交后触发，外壳据此切回工作台。</summary>
    public event EventHandler? Navigated;

    public void SetCompact(bool compact)
    {
        _compact = compact;
        ExpandedRoot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactRoot.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    public void BeginAddressEdit()
    {
        AddressError.Visibility = Visibility.Collapsed;
        if (_compact)
        {
            AddressFlyout.ShowAt(CompactAddressButton);
            return;
        }
        AddressDisplay.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Text = ViewModel.FullAddress;
        AddressBox.Focus(FocusState.Programmatic);
        AddressBox.SelectAll();
    }

    private void OnAddressClick(object sender, RoutedEventArgs args) => BeginAddressEdit();

    private void OnAddressKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            EndAddressEdit();
            return;
        }
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        string? error = ViewModel.Navigate(AddressBox.Text);
        if (error is not null)
        {
            AddressError.Text = error;
            AddressError.Visibility = Visibility.Visible;
            return;
        }
        EndAddressEdit();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddressLostFocus(object sender, RoutedEventArgs args) => EndAddressEdit();

    private void EndAddressEdit()
    {
        AddressError.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Collapsed;
        AddressDisplay.Visibility = Visibility.Visible;
    }

    private void OnAddressFlyoutOpened(object? sender, object args)
    {
        CompactAddressError.Visibility = Visibility.Collapsed;
        CompactAddressBox.Text = ViewModel.FullAddress;
        CompactAddressBox.Focus(FocusState.Programmatic);
        CompactAddressBox.SelectAll();
    }

    private void OnCompactAddressKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        string? error = ViewModel.Navigate(CompactAddressBox.Text);
        if (error is not null)
        {
            CompactAddressError.Text = error;
            CompactAddressError.Visibility = Visibility.Visible;
            return;
        }
        AddressFlyout.Hide();
        Navigated?.Invoke(this, EventArgs.Empty);
    }
}
