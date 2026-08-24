using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace YunKao.Controls;

public sealed partial class EventList : UserControl
{
    public EventList()
    {
        InitializeComponent();
    }

    public void Add(string message, bool warning = false)
    {
        var item = new ListViewItem();
        item.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Ellipse { Width = 6, Height = 6, Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[warning ? "AmberBrush" : "CyanBrush"] },
                new TextBlock { Text = message, Style = (Style)Application.Current.Resources["SecondaryTextStyle"] },
            },
        };
        Events.Items.Insert(0, item);
        while (Events.Items.Count > 20) Events.Items.RemoveAt(Events.Items.Count - 1);
    }
}
