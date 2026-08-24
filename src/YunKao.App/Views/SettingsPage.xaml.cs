using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;

namespace YunKao.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            AppSettings settings = App.Services.Settings.Load();
            YunKaoUserBox.Text = settings.YunKaoUser;
            YunKaoPasswordBox.Password = "";
            RememberPasswordCheckBox.IsChecked = settings.RememberYunKaoPassword;
            ExportDirectoryBox.Text = settings.DefaultExportDirectory;
            ExportPrefixBox.Text = settings.ExportPrefix;
            SelectByTag(ExportFormatCombo, settings.DefaultExportFormat);
            AutoOpenCheckBox.IsChecked = settings.AutoOpenAfterExport;
            ExportWithoutAnswersCheckBox.IsChecked = settings.ExportWithoutAnswers;
            AiEnabledCheckBox.IsChecked = settings.AiEnabled;
            SelectByTag(ProviderCombo, settings.AiProvider);
            AiBaseUrlBox.Text = settings.AiBaseUrl;
            AiModelBox.Text = settings.AiModel;
            AiKeyBox.Password = "";
            ClearAiKeyCheckBox.IsChecked = false;
            AiSupportsImagesCheckBox.IsChecked = settings.AiSupportsImages;
            AiKeyStatusText.Text = settings.AiKeySaved ? "Key 已保存" : "未保存 Key";
            AcrylicRadio.IsChecked = settings.AppearanceMaterial == "acrylic";
            MicaRadio.IsChecked = settings.AppearanceMaterial == "mica";
            SolidRadio.IsChecked = settings.AppearanceMaterial == "solid";
            ClearRadio.IsChecked = settings.AppearanceClarity == "clear";
            StandardRadio.IsChecked = settings.AppearanceClarity == "standard";
            TransparentRadio.IsChecked = settings.AppearanceClarity == "transparent";
            ReduceMotionToggle.IsOn = settings.ReduceMotion;
            StatusText.Text = "";
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_loading || ProviderCombo.SelectedItem is not ComboBoxItem item) return;
        string provider = item.Tag?.ToString() ?? "custom";
        AiProviderPreset preset = AiProviderRegistry.Get(provider);
        if (string.IsNullOrWhiteSpace(AiBaseUrlBox.Text)) AiBaseUrlBox.Text = preset.BaseUrl;
        if (string.IsNullOrWhiteSpace(AiModelBox.Text)) AiModelBox.Text = preset.Model;
        AiSupportsImagesCheckBox.IsChecked = preset.SupportsImages;
    }

    private void OnSaveClick(object sender, RoutedEventArgs args)
    {
        try
        {
            AppSettings settings = App.Services.Settings.Load();
            settings.YunKaoUser = YunKaoUserBox.Text.Trim();
            settings.RememberYunKaoPassword = RememberPasswordCheckBox.IsChecked == true;
            settings.DefaultExportDirectory = ExportDirectoryBox.Text.Trim();
            settings.ExportPrefix = ExportPrefixBox.Text.Trim();
            settings.DefaultExportFormat = ReadTag(ExportFormatCombo, "PDF");
            settings.AutoOpenAfterExport = AutoOpenCheckBox.IsChecked == true;
            settings.ExportWithoutAnswers = ExportWithoutAnswersCheckBox.IsChecked == true;
            settings.AiEnabled = AiEnabledCheckBox.IsChecked == true;
            settings.AiProvider = ReadTag(ProviderCombo, "custom");
            settings.AiBaseUrl = AiBaseUrlBox.Text.Trim();
            settings.AiModel = AiModelBox.Text.Trim();
            settings.AiSupportsImages = AiSupportsImagesCheckBox.IsChecked == true;
            settings.AppearanceMaterial = AcrylicRadio.IsChecked == true ? "acrylic"
                : MicaRadio.IsChecked == true ? "mica" : "solid";
            settings.AppearanceClarity = ClearRadio.IsChecked == true ? "clear"
                : TransparentRadio.IsChecked == true ? "transparent" : "standard";
            settings.ReduceMotion = ReduceMotionToggle.IsOn;

            string? apiKey = ClearAiKeyCheckBox.IsChecked == true
                ? ""
                : string.IsNullOrWhiteSpace(AiKeyBox.Password) ? null : AiKeyBox.Password;
            string? password = !string.IsNullOrWhiteSpace(YunKaoPasswordBox.Password)
                ? YunKaoPasswordBox.Password
                : settings.RememberYunKaoPassword ? null : "";
            App.Services.Settings.Save(settings, apiKey, password);
            MotionService.SetReduceMotion(settings.ReduceMotion);
            AiKeyBox.Password = "";
            YunKaoPasswordBox.Password = "";
            AiKeyStatusText.Text = settings.AiKeySaved ? "Key 已保存" : "未保存 Key";
            StatusText.Text = "已保存";
        }
        catch (Exception exception)
        {
            StatusText.Text = "保存失败：" + exception.Message;
            App.Services.Diagnostics.Error("设置保存失败", exception);
        }
    }

    private void OnReduceMotionToggled(object sender, RoutedEventArgs args)
    {
        MotionService.SetReduceMotion(ReduceMotionToggle.IsOn);
    }

    private static void SelectByTag(ComboBox comboBox, string value)
    {
        for (int index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item
                && string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        if (comboBox.Items.Count > 0) comboBox.SelectedIndex = 0;
    }

    private static string ReadTag(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? fallback
            : fallback;
    }
}
