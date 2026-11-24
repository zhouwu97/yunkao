using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;

namespace YunKao.Views;

/// <summary>
/// 设置页：普通选项改动后 600 ms 自动保存；外观即时生效；
/// 密码与 API Key 属于敏感信息，只在点击对应按钮时写入系统凭据库。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private const string SavedCredentialMask = "●●●●●●●●";
    private readonly DispatcherQueueTimer _saveTimer;
    private bool _loading;
    private bool _savePending;
    private string _previousProvider = "custom";
    private readonly List<(Grid Grid, GridLength First, GridLength Second,
        FrameworkElement Control, HorizontalAlignment Alignment)> _responsiveRows = [];

    public SettingsPage()
    {
        InitializeComponent();
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();
        Loaded += (_, _) =>
        {
            LoadSettings();
            InitializeResponsiveRows(SettingsBody);
            UpdateResponsiveRows();
        };
        Unloaded += (_, _) =>
        {
            _saveTimer.Stop();
            if (_savePending) SaveNow();
        };
    }

    private void InitializeResponsiveRows(DependencyObject parent)
    {
        if (_responsiveRows.Count > 0 && ReferenceEquals(parent, SettingsBody)) return;
        if (parent is Grid grid && ReferenceEquals(grid.Style, Resources["RowGridStyle"])
            && grid.ColumnDefinitions.Count == 2
            && grid.Children.OfType<FrameworkElement>().FirstOrDefault(child => Grid.GetColumn(child) == 1) is { } control)
        {
            _responsiveRows.Add((grid, grid.ColumnDefinitions[0].Width, grid.ColumnDefinitions[1].Width,
                control, control.HorizontalAlignment));
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            InitializeResponsiveRows(VisualTreeHelper.GetChild(parent, i));
    }

    private void OnJumpToGroup(object sender, RoutedEventArgs args)
    {
        var section = ((sender as MenuFlyoutItem)?.Tag as string) switch
        {
            "Account" => AccountGroup,
            "Export" => ExportGroup,
            "Ai" => AiGroup,
            "Data" => DataGroup,
            _ => AppearanceGroup,
        };
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(section) is Control first)
                first.Focus(FocusState.Programmatic);
            section.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = !MotionService.ReduceMotion });
        });
    }

    private void OnSettingsSizeChanged(object sender, SizeChangedEventArgs args) => UpdateResponsiveRows();

    private void UpdateResponsiveRows()
    {
        bool narrow = SettingsBody.ActualWidth < 720;
        foreach (var row in _responsiveRows)
        {
            row.Grid.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : row.First;
            row.Grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : row.Second;
            row.Grid.RowSpacing = narrow ? 8 : 0;
            row.Grid.ColumnSpacing = narrow ? 0 : 24;
            Grid.SetRow(row.Control, narrow ? 1 : 0);
            Grid.SetColumn(row.Control, narrow ? 0 : 1);
            row.Control.HorizontalAlignment = narrow ? HorizontalAlignment.Left : row.Alignment;
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            AppSettings settings = App.Services.Settings.Load();
            YunKaoUserBox.Text = settings.YunKaoUser;
            YunKaoPasswordBox.Password = "";
            RememberPasswordToggle.IsOn = settings.RememberYunKaoPassword;
            ExportDirectoryBox.Text = settings.DefaultExportDirectory;
            ExportPrefixBox.Text = settings.ExportPrefix;
            SelectByTag(ExportFormatCombo, settings.DefaultExportFormat);
            AutoOpenToggle.IsOn = settings.AutoOpenAfterExport;
            ExportWithoutAnswersToggle.IsOn = settings.ExportWithoutAnswers;
            AiEnabledToggle.IsOn = settings.AiEnabled;
            SelectByTag(ProviderCombo, settings.AiProvider);
            _previousProvider = settings.AiProvider;
            AiBaseUrlBox.Text = settings.AiBaseUrl;
            AiModelBox.Text = settings.AiModel;
            AiKeyBox.Password = "";
            AiSupportsImagesToggle.IsOn = settings.AiSupportsImages;
            UpdateCredentialIndicators(settings);

            string theme = AppearancePolicy.NormalizeTheme(settings.AppearanceTheme);
            ThemeSystemRadio.IsChecked = theme == "system";
            ThemeLightRadio.IsChecked = theme == "light";
            ThemeDarkRadio.IsChecked = theme == "dark";
            AcrylicRadio.IsChecked = settings.AppearanceMaterial == "acrylic";
            MicaRadio.IsChecked = settings.AppearanceMaterial == "mica";
            SolidRadio.IsChecked = settings.AppearanceMaterial == "solid";
            ClearRadio.IsChecked = settings.AppearanceClarity == "clear";
            StandardRadio.IsChecked = settings.AppearanceClarity == "standard";
            TransparentRadio.IsChecked = settings.AppearanceClarity == "transparent";
            ReduceMotionToggle.IsOn = settings.ReduceMotion;

            SetStatus("");
            AiTestResultText.Visibility = Visibility.Collapsed;
            ApplyPresetButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            // TextChanged 等事件在赋值后异步触发；低优先级放开标记，避免打开页面就误报"已保存"。
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _loading = false)) _loading = false;
        }
    }

    // ---------------- 自动保存 ----------------

    private void OnSettingChanged(object sender, RoutedEventArgs args) => ScheduleSave();
    private void OnTextSettingChanged(object sender, TextChangedEventArgs args) => ScheduleSave();
    private void OnSelectionSettingChanged(object sender, SelectionChangedEventArgs args) => ScheduleSave();

    private void ScheduleSave()
    {
        if (_loading) return;
        _savePending = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        SetStatus("正在保存…");
    }

    /// <summary>
    /// 写入配置。apiKey / password 为 null 时保持凭据库中的现有值，空字符串表示清除。
    /// </summary>
    private bool SaveNow(string? apiKey = null, string? password = null)
    {
        _savePending = false;
        try
        {
            AppSettings settings = App.Services.Settings.Load();
            settings.YunKaoUser = YunKaoUserBox.Text.Trim();
            settings.RememberYunKaoPassword = RememberPasswordToggle.IsOn;
            settings.DefaultExportDirectory = ExportDirectoryBox.Text.Trim();
            settings.ExportPrefix = ExportPrefixBox.Text.Trim();
            settings.DefaultExportFormat = ReadTag(ExportFormatCombo, "PDF");
            settings.AutoOpenAfterExport = AutoOpenToggle.IsOn;
            settings.ExportWithoutAnswers = ExportWithoutAnswersToggle.IsOn;
            settings.AiEnabled = AiEnabledToggle.IsOn;
            settings.AiProvider = ReadTag(ProviderCombo, "custom");
            settings.AiBaseUrl = AiBaseUrlBox.Text.Trim();
            settings.AiModel = AiModelBox.Text.Trim();
            settings.AiSupportsImages = AiSupportsImagesToggle.IsOn;
            settings.AppearanceTheme = ReadTheme();
            settings.AppearanceMaterial = ReadMaterial();
            settings.AppearanceClarity = ReadClarity();
            settings.ReduceMotion = ReduceMotionToggle.IsOn;

            // 取消"记住密码"时同步清除已保存的密码。
            password ??= settings.RememberYunKaoPassword ? null : "";
            App.Services.Settings.Save(settings, apiKey, password);
            App.Services.Task.SetPracticeMode(settings.ExportWithoutAnswers);
            UpdateCredentialIndicators(settings);
            SetStatus($"已自动保存 · {DateTime.Now:HH:mm:ss}");
            RetrySaveButton.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception exception)
        {
            _savePending = true;
            RetrySaveButton.Visibility = Visibility.Visible;
            SetStatus("保存失败：" + exception.Message);
            App.Services.Diagnostics.Error("设置保存失败", exception);
            return false;
        }
    }

    private void OnRetrySaveClick(object sender, RoutedEventArgs args)
    {
        _saveTimer.Stop();
        SaveNow();
    }

    private void OnSavePasswordClick(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(YunKaoUserBox.Text) || !RememberPasswordToggle.IsOn)
        {
            SetStatus("请先填写账号并开启记住密码，再保存密码。");
            return;
        }
        if (string.IsNullOrWhiteSpace(YunKaoPasswordBox.Password))
        {
            SetStatus("请输入密码后再保存。");
            return;
        }
        _saveTimer.Stop();
        if (!SaveNow(password: YunKaoPasswordBox.Password)) return;
        YunKaoPasswordBox.Password = "";
        SetStatus("密码已保存到系统凭据库");
    }

    private void OnSaveAiKeyClick(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(AiKeyBox.Password))
        {
            SetStatus("请输入 API Key 后再保存。");
            return;
        }
        _saveTimer.Stop();
        if (!SaveNow(apiKey: AiKeyBox.Password.Trim())) return;
        AiKeyBox.Password = "";
        SetStatus("API Key 已保存");
    }

    private async void OnClearAiKeyClick(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清除已保存的 API Key？",
            Content = "清除后 AI 补全将无法使用，直到重新填写 Key。",
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _saveTimer.Stop();
        if (!SaveNow(apiKey: "")) return;
        AiKeyBox.Password = "";
        SetStatus("API Key 已清除");
    }

    private void UpdateCredentialIndicators(AppSettings settings)
    {
        bool passwordSaved = settings.RememberYunKaoPassword
            && !string.IsNullOrWhiteSpace(settings.YunKaoUser)
            && !string.IsNullOrEmpty(App.Services.Settings.GetYunKaoPassword(settings.YunKaoUser));
        YunKaoPasswordBox.PlaceholderText = passwordSaved ? SavedCredentialMask : "输入密码";
        AutomationProperties.SetHelpText(YunKaoPasswordBox, passwordSaved ? "密码已保存，输入新密码可替换" : "未保存密码");
        UpdateAiKeyStatus(settings.AiKeySaved);
    }

    private void UpdateAiKeyStatus(bool keySaved)
    {
        AiKeyBox.PlaceholderText = keySaved ? SavedCredentialMask : "输入 API Key";
        AiKeyStatusText.Text = keySaved ? "Key 已保存" : "未保存 Key";
        AutomationProperties.SetHelpText(AiKeyBox, keySaved ? "API Key 已保存，输入新 Key 可替换" : "未保存 API Key");
    }

    // ---------------- 外观（即时生效） ----------------

    private void OnThemeChanged(object sender, RoutedEventArgs args)
    {
        if (_loading || ThemeSystemRadio is null || ThemeLightRadio is null || ThemeDarkRadio is null) return;
        App.MainWindow?.ApplyTheme(ReadTheme());
        ScheduleSave();
    }

    private void OnAppearanceChanged(object sender, RoutedEventArgs args)
    {
        if (_loading
            || AcrylicRadio is null || MicaRadio is null || SolidRadio is null
            || ClearRadio is null || StandardRadio is null || TransparentRadio is null)
        {
            return;
        }
        App.MainWindow?.ApplyAppearance(ReadMaterial(), ReadClarity());
        ScheduleSave();
    }

    private void OnReduceMotionToggled(object sender, RoutedEventArgs args)
    {
        if (_loading) return;
        MotionService.SetReduceMotion(ReduceMotionToggle.IsOn);
        ScheduleSave();
    }

    private string ReadTheme() => ThemeLightRadio.IsChecked == true ? "light"
        : ThemeDarkRadio.IsChecked == true ? "dark" : "system";

    private string ReadMaterial() => MicaRadio.IsChecked == true ? "mica"
        : SolidRadio.IsChecked == true ? "solid" : "acrylic";

    private string ReadClarity() => ClearRadio.IsChecked == true ? "clear"
        : TransparentRadio.IsChecked == true ? "transparent" : "standard";

    private void SetStatus(string text) => Header.Status = text;

    // ---------------- AI 与目录操作 ----------------

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

        AiSupportsImagesToggle.IsOn = newPreset.SupportsImages;
        _previousProvider = newProvider;

        bool hasCustomMismatch = !string.Equals(AiBaseUrlBox.Text.Trim(), newPreset.BaseUrl.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(AiModelBox.Text.Trim(), newPreset.Model.Trim(), StringComparison.OrdinalIgnoreCase);
        ApplyPresetButton.Visibility = (hasCustomMismatch && newProvider != "custom") ? Visibility.Visible : Visibility.Collapsed;
        AiKeyBox.Password = "";
        UpdateAiKeyStatus(!string.IsNullOrWhiteSpace(App.Services.Settings.GetAiKey(newProvider)));
        ScheduleSave();
    }

    private void OnApplyPresetClick(object sender, RoutedEventArgs args)
    {
        string provider = ReadTag(ProviderCombo, "custom");
        AiProviderPreset preset = AiProviderRegistry.Get(provider);
        AiBaseUrlBox.Text = preset.BaseUrl;
        AiModelBox.Text = preset.Model;
        AiSupportsImagesToggle.IsOn = preset.SupportsImages;
        ApplyPresetButton.Visibility = Visibility.Collapsed;
        AiTestResultText.Text = $"已应用 {preset.Label} 推荐配置";
        AiTestResultText.Visibility = Visibility.Visible;
        ScheduleSave();
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
            SupportsImages = AiSupportsImagesToggle.IsOn,
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
                ScheduleSave();
            }
        }
        catch (Exception ex)
        {
            SetStatus("选择目录失败：" + ex.Message);
        }
    }

    private void OnOpenExportFolderClick(object sender, RoutedEventArgs args)
    {
        string dir = string.IsNullOrWhiteSpace(ExportDirectoryBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : ExportDirectoryBox.Text.Trim();
        OpenDirectory(dir);
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs args)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YunKaoDesktop");
        OpenDirectory(dir);
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs args)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YunKaoDesktop", "Logs");
        OpenDirectory(dir);
    }

    private void OpenDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            SetStatus(FileActions.TryOpen(directory, out string error) ? "已打开目录" : "打开目录失败：" + error);
        }
        catch (Exception exception)
        {
            SetStatus("打开目录失败：" + exception.Message);
        }
    }

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var package = new DataPackage();
            string info = $"YunKao v2.0\nOS: {Environment.OSVersion}\nRuntime: .NET {Environment.Version}\nWebView2: {App.Services.Workspace.BrowserVersion}\nTime: {DateTimeOffset.Now}";
            package.SetText(info);
            Clipboard.SetContent(package);
            SetStatus("已复制诊断信息到剪贴板");
        }
        catch (Exception ex)
        {
            SetStatus("复制失败：" + ex.Message);
        }
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
