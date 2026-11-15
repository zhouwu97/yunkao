using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.ViewModels;

namespace YunKao.Controls;

public sealed partial class TaskCard : UserControl
{
    public TaskCard() => InitializeComponent();

    public TaskViewModel ViewModel => App.Services.Task;

    public void SetCompact(bool compact)
    {
        ExpandedRoot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCompactClick(object sender, RoutedEventArgs args)
    {
        // 由按钮自带 Flyout 打开；这里不做额外处理。
    }

    /// <summary>弹出层里放一张展开状态的任务卡；首次打开时再创建，避免 XAML 自引用。</summary>
    private void OnCompactFlyoutOpening(object? sender, object args)
    {
        CompactFlyout.Content ??= new TaskCard { Width = 260 };
    }
}
