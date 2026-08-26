using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;

namespace YunKao.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading;
    private string _previousProvider = "custom";

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
            _previousProvider = settings.AiProvider;
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
            AiTestResultText.Visibility = Visibility.Collapsed;
            ApplyPresetButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_loading || ProviderCombo.SelectedItem is not ComboBoxItem item) return;
        string newProvider = item.Tag?.ToString() ?? "custom";
        AiProviderPreset newPreset = AiProviderRegistry.Get(newProvider);
        AiProviderPreset oldPreset = AiProviderRegistry.Get(_previousProvider);

        bool urlMatchesPreviousDefault = string.IsNullOrWhiteSpace(AiBaseUrlBox.Text)
            || string.Equals(AiBaseUrlBox.Text.Trim(), oldPreset.BaseUrl.Trim(), StringComparison.OrdinalIgnoreCase);
        bool modelMatchesPreviousDefault = string.IsNullOrWhiteSpace(AiModelBox.Text)
            || string.Equals(AiModelBox.Text.Trim(), oldPreset.Model.Trim(), StringComparison.OrdinalIgnoreCase);

        if (urlMatchesPreviousDefault)
        {
            AiBaseUrlBox.Text = newPreset.BaseUrl;
        }
        if (modelMatchesPreviousDefault)
        {
            AiModelBox.Text = newPreset.Model;
        }

        AiSupportsImagesCheckBox.IsChecked = newPreset.SupportsImages;
        _previousProvider = newProvider;

        bool hasCustomMismatch = !string.Equals(AiBaseUrlBox.Text.Trim(), newPreset.BaseUrl.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(AiModelBox.Text.Trim(), newPreset.Model.Trim(), StringComparison.OrdinalIgnoreCase);
        ApplyPresetButton.Visibility = (hasCustomMismatch && newProvider != "custom") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnApplyPresetClick(object sender, RoutedEventArgs args)
    {
        string provider = ReadTag(ProviderCombo, "custom");
        AiProviderPreset preset = AiProviderRegistry.Get(provider);
        AiBaseUrlBox.Text = preset.BaseUrl;
        AiModelBox.Text = preset.Model;
        AiSupportsImagesCheckBox.IsChecked = preset.SupportsImages;
        ApplyPresetButton.Visibility = Visibility.Collapsed;
        AiTestResultText.Text = $"已应用 {preset.Label} 推荐配置";
        AiTestResultText.Visibility = Visibility.Visible;
    }

    private async void OnTestAiConnectionClick(object sender, RoutedEventArgs args)
    {
        string provider = ReadTag(ProviderCombo, "custom");
        string baseUrl = AiBaseUrlBox.Text.Trim();
        string model = AiModelBox.Text.Trim();
        string apiKey = !string.IsNullOrWhiteSpace(AiKeyBox.Password)
            ? AiKeyBox.Password.Trim()
            : App.Services.Settings.GetAiKey(provider);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            AiTestResultText.Text = "请先填写 Base URL 和 API Key 再进行测试。";
            AiTestResultText.Visibility = Visibility.Visible;
            return;
        }

        AiTestResultText.Text = "正在连接 AI 接口测试...";
        AiTestResultText.Visibility = Visibility.Visible;

        var config = new AiRequestConfiguration
        {
            Provider = provider,
            BaseUrl = baseUrl,
            Model = model,
            ApiKey = apiKey,
            SupportsImages = AiSupportsImagesCheckBox.IsChecked == true,
        };

        AiConnectionTestResult result = await App.Services.Ai.TestConnectionAsync(config);
        AiTestResultText.Text = result.Success ? $"✓ {result.Message}" : $"✗ {result.Message}";
    }

    private async void OnGetAiModelsClick(object sender, RoutedEventArgs args)
    {
        string provider = ReadTag(ProviderCombo, "custom");
        string baseUrl = AiBaseUrlBox.Text.Trim();
        string apiKey = !string.IsNullOrWhiteSpace(AiKeyBox.Password)
            ? AiKeyBox.Password.Trim()
            : App.Services.Settings.GetAiKey(provider);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            AiTestResultText.Text = "请先填写 Base URL 和 API Key。";
            AiTestResultText.Visibility = Visibility.Visible;
            return;
        }

        AiTestResultText.Text = "正在获取可用模型列表...";
        AiTestResultText.Visibility = Visibility.Visible;

        var config = new AiRequestConfiguration
        {
            Provider = provider,
            BaseUrl = baseUrl,
            Model = "",
            ApiKey = apiKey,
            SupportsImages = false,
        };

        AiModelsResult result = await App.Services.Ai.GetModelsAsync(config);
        if (result.Success && result.Models.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(AiModelBox.Text))
            {
                AiModelBox.Text = result.Models[0];
            }
            string preview = string.Join(", ", result.Models.Take(6));
            AiTestResultText.Text = $"✓ {result.Message} (例如: {preview}{(result.Models.Count > 6 ? "..." : "")})";
        }
        else
        {
            AiTestResultText.Text = result.Success ? "✓ 未返回模型列表" : $"✗ {result.Message}";
        }
    }

    private async void OnBrowseExportFolderClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            if (App.MainWindow is not null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                ExportDirectoryBox.Text = folder.Path;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "选择目录失败：" + ex.Message;
        }
    }

    private void OnOpenExportFolderClick(object sender, RoutedEventArgs args)
    {
        string dir = string.IsNullOrWhiteSpace(ExportDirectoryBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : ExportDirectoryBox.Text.Trim();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs args)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YunKaoDesktop");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs args)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YunKaoDesktop", "Logs");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var package = new DataPackage();
            string info = $"YunKao v2.0\nOS: {Environment.OSVersion}\nRuntime: .NET {Environment.Version}\nWebView2: {App.Services.Workspace.BrowserVersion}\nTime: {DateTimeOffset.Now}";
            package.SetText(info);
            Clipboard.SetContent(package);
            StatusText.Text = "已复制诊断信息到剪贴板";
        }
        catch (Exception ex)
        {
            StatusText.Text = "复制失败：" + ex.Message;
        }
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
