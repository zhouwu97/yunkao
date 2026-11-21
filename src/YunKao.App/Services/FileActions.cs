using System.Diagnostics;

namespace YunKao.Services;

/// <summary>系统文件操作的失败不会推翻已成功生成的导出文件。</summary>
internal static class FileActions
{
    public static bool TryOpen(string path, out string error)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            error = "";
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            App.Services.Diagnostics.Warning("打开文件或目录失败：" + error);
            return false;
        }
    }

    public static void OpenExportFolder(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is null || !TryOpen(directory, out _))
            App.Services.Notifications.Show("无法打开文件夹", "文件已保存：" + filePath,
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
    }
}
