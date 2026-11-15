using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using YunKao.Services;
using Windows.Foundation;

namespace YunKao.Controls;

public enum NavigationTarget
{
    Workspace,
    History,
    Export,
    Diagnostics,
    Settings
}

public sealed class NavigationRequestEventArgs(NavigationTarget target) : EventArgs
{
    public NavigationTarget Target { get; } = target;
}

/// <summary>
/// 侧边导航。选中指示是一块玻璃：移动时纵向拉伸、横向收窄，再弹回原形。
/// </summary>
public sealed partial class NavigationRail : UserControl
{
    private NavigationTarget _activeTarget = NavigationTarget.Workspace;
    private bool _isLoaded;
    private bool _compact;
    private Storyboard? _moveStoryboard;

    public NavigationRail()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            ApplyStyles();
            MoveSelection(animate: false);
        };
        SizeChanged += OnLayoutChanged;
    }

    public event EventHandler<NavigationRequestEventArgs>? NavigationRequested;

    public void SetActive(NavigationTarget target)
    {
        bool changed = _activeTarget != target;
        _activeTarget = target;
        ApplyStyles();
        if (_isLoaded) MoveSelection(changed);
    }

    /// <summary>紧凑模式只显示图标，按钮与选中块收为方形。</summary>
    public void SetCompact(bool compact)
    {
        _compact = compact;
        ApplyStyles();
        SelectionBlob.Width = compact ? 44 : double.NaN;
        SelectionBlob.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        if (_isLoaded) DispatcherQueue.TryEnqueue(() => MoveSelection(animate: false));
    }

    private void OnLayoutChanged(object sender, SizeChangedEventArgs args)
    {
        if (_isLoaded) MoveSelection(animate: false);
    }

    private void ApplyStyles()
    {
        var normal = (Style)Application.Current.Resources["SidebarNavItemStyle"];
        var selected = (Style)Application.Current.Resources["SidebarNavItemSelectedStyle"];
        foreach (NavigationTarget target in Enum.GetValues<NavigationTarget>())
        {
            Button button = GetButton(target);
            Style style = target == _activeTarget ? selected : normal;
            if (!ReferenceEquals(button.Style, style)) button.Style = style;

            // 样式切换会重置本地值，紧凑尺寸在样式之后应用。
            button.Width = _compact ? 44 : double.NaN;
            button.HorizontalAlignment = _compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = _compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.Padding = _compact ? new Thickness(0) : new Thickness(12, 0, 12, 0);
            if (button.Content is StackPanel content && content.Children.Count > 1)
            {
                content.Children[1].Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, target == _activeTarget ? "当前页" : "");
        }
    }

    private void MoveSelection(bool animate)
    {
        Button button = GetButton(_activeTarget);
        if (button.ActualHeight <= 0) return;
        double translation = button.TransformToVisual(NavigationItems).TransformPoint(new Point(0, 0)).Y;

        _moveStoryboard?.Stop();
        if (!animate || MotionService.ReduceMotion)
        {
            SelectionTransform.TranslateY = translation;
            SelectionTransform.ScaleX = 1;
            SelectionTransform.ScaleY = 1;
            return;
        }

        double distance = Math.Abs(translation - SelectionTransform.TranslateY);
        double stretch = 1 + Math.Min(0.12, distance / 800);
        var storyboard = new Storyboard();
        var move = new DoubleAnimation
        {
            To = translation,
            Duration = new Duration(TimeSpan.FromMilliseconds(340)),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 },
        };
        Storyboard.SetTarget(move, SelectionTransform);
        Storyboard.SetTargetProperty(move, "TranslateY");
        storyboard.Children.Add(move);
        storyboard.Children.Add(Wobble("ScaleY", stretch));
        _moveStoryboard = storyboard;
        storyboard.Begin();
    }

    /// <summary>形变关键帧：拉伸 → 轻微过冲 → 复原。</summary>
    private DoubleAnimationUsingKeyFrames Wobble(string property, double peak)
    {
        var frames = new DoubleAnimationUsingKeyFrames();
        frames.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
        frames.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(120),
            Value = peak,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        frames.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(260),
            Value = 1 - (peak - 1) * 0.3,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
        frames.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(400),
            Value = 1,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        Storyboard.SetTarget(frames, SelectionTransform);
        Storyboard.SetTargetProperty(frames, property);
        return frames;
    }

    private Button GetButton(NavigationTarget target) => target switch
    {
        NavigationTarget.History => HistoryButton,
        NavigationTarget.Export => ExportButton,
        NavigationTarget.Diagnostics => DiagnosticsButton,
        NavigationTarget.Settings => SettingsButton,
        _ => WorkspaceButton,
    };

    private void OnItemClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse(tag, out NavigationTarget target))
        {
            NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(target));
        }
    }
}
