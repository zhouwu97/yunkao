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

    /// <summary>弹出层里放一张展开状态的任务卡；首次打开时再创建，避免 XAML 自引用。</summary>
    private void OnCompactFlyoutOpening(object? sender, object args)
    {
        var scene = LiquidGlassScope.Find(this, outermost: true);
        ElementTheme theme = scene?.Element.ActualTheme ?? ActualTheme;
        if (CompactFlyout.Content is not TaskCard card)
        {
            card = new TaskCard { Width = 260, RequestedTheme = theme };
            YunKao.Services.ButtonMotion.Attach(card);
            CompactFlyout.Content = card;
        }
        card.RequestedTheme = theme;
        // A popup has its own visual ancestry. Carry the shared decorative source across it.
        if (scene is not null) LiquidGlassScope.SetScene(card, scene);
    }
}
