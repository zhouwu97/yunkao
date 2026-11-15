using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

/// <summary>统一的页面标题区：标题、说明、实时状态和右侧操作。</summary>
public sealed partial class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public PageHeader() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
}
