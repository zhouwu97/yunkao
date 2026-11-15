using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

/// <summary>列表空状态：图标、标题、说明和一个下一步入口。</summary>
public sealed partial class EmptyState : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionLabelProperty = DependencyProperty.Register(
        nameof(ActionLabel), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public EmptyState() => InitializeComponent();

    public event RoutedEventHandler? ActionClick;

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string ActionLabel { get => (string)GetValue(ActionLabelProperty); set => SetValue(ActionLabelProperty, value); }

    private void OnActionClick(object sender, RoutedEventArgs args) => ActionClick?.Invoke(this, args);
}
