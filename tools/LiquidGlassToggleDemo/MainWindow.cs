using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Automation;
using Windows.UI;
using YunKao.Controls;

namespace SwitchGlassDemo;

public sealed class MainWindow:Window
{
    private readonly List<LiquidGlassScene> _scenes=[];
    private readonly List<LiquidGlassToggleSwitch> _toggles=[];
    public MainWindow()
    {
        Title="液态玻璃开关 · 放大 Demo";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120,840));
        var root=new Grid{Padding=new Thickness(30),RowSpacing=20,Background=Brush(245,244,248)};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var heading=new StackPanel{Spacing=7};
        heading.Children.Add(new TextBlock{Text="液态玻璃开关 · 按住放大",FontSize=28,Foreground=Brush(37,34,48)});
        heading.Children.Add(new TextBlock{Text="按住时玻璃滑块变大；拖过中点才变色，松手缩回普通滑块。",FontSize=15,Foreground=Brush(98,92,110)});
        root.Children.Add(heading);
        var replay=new Button{Content="自动演示",HorizontalAlignment=HorizontalAlignment.Left};
        replay.Click+=async(_,_)=>
        {
            replay.IsEnabled=false;
            try{await Task.WhenAll(_toggles.Select(t=>t.ReplayAsync()));}
            finally{replay.IsEnabled=true;}
        };
        Grid.SetRow(replay,1);root.Children.Add(replay);
        var samples=new Grid{ColumnSpacing=22};
        samples.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        samples.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        samples.Children.Add(CreateSample(true));
        var light=CreateSample(false);Grid.SetColumn(light,1);samples.Children.Add(light);
        Grid.SetRow(samples,2);root.Children.Add(samples);
        Content=root;
        Closed+=(_,_)=>{foreach(var scene in _scenes)scene.Dispose();};
    }

    private Grid CreateSample(bool dark)
    {
        var host=new Grid{RequestedTheme=dark?ElementTheme.Dark:ElementTheme.Light};
        var backdrop=new Grid{IsHitTestVisible=false,Background=dark?Brush(28,28,30):Brush(237,223,232)};
        host.Children.Add(backdrop);
        var scene=new LiquidGlassScene(backdrop);_scenes.Add(scene);LiquidGlassScope.SetScene(host,scene);
        var body=new Grid{Padding=new Thickness(28),RowSpacing=18};
        foreach(var size in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})body.RowDefinitions.Add(new(){Height=size});
        var ink=dark?Brush(247,247,249):Brush(42,36,48);
        body.Children.Add(new TextBlock{Text=dark?"深色":"浅色",FontSize=21,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=ink});
        var actual=new Grid{Height=86,ColumnSpacing=18};
        actual.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        actual.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        actual.Children.Add(new TextBlock{Text="实际尺寸",Foreground=ink,VerticalAlignment=VerticalAlignment.Center,FontSize=16});
        var small=NewToggle(dark,dark?"DarkActual":"LightActual");
        Grid.SetColumn(small,1);actual.Children.Add(small);Grid.SetRow(actual,1);body.Children.Add(actual);
        var preview=new Grid{RowSpacing=15,VerticalAlignment=VerticalAlignment.Center};
        preview.RowDefinitions.Add(new(){Height=GridLength.Auto});preview.RowDefinitions.Add(new(){Height=new GridLength(220)});
        preview.Children.Add(new TextBlock{Text="放大查看",Foreground=ink,Opacity=.65,FontSize=13});
        var enlarged=NewToggle(dark,dark?"DarkPreview":"LightPreview",4);
        enlarged.HorizontalAlignment=HorizontalAlignment.Center;
        Grid.SetRow(enlarged,1);preview.Children.Add(enlarged);
        Grid.SetRow(preview,2);body.Children.Add(preview);
        var hint=new TextBlock{Text="按住放大  →  过中点变色  →  松手恢复",FontSize=13,Foreground=ink,Opacity=.65,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0)};
        Grid.SetRow(hint,3);body.Children.Add(hint);host.Children.Add(body);
        return host;
    }
    private LiquidGlassToggleSwitch NewToggle(bool on,string id,double scale=1)
    {
        var toggle=new LiquidGlassToggleSwitch{IsOn=on,PreviewScale=scale,VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetAutomationId(toggle,id);AutomationProperties.SetName(toggle,"液态开关");
        _toggles.Add(toggle);return toggle;
    }
    private static SolidColorBrush Brush(byte r,byte g,byte b)=>new(Color.FromArgb(255,r,g,b));
}
