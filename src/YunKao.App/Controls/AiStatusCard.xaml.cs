using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

public sealed partial class AiStatusCard : UserControl
{
    public AiStatusCard()
    {
        InitializeComponent();
    }

    public void SetStatus(string status, string detail)
    {
        StatusText.Text = status;
        DetailText.Text = detail;
    }
}
