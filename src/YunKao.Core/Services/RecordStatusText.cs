namespace YunKao.Core.Services;

public static class RecordStatusText
{
    public static string ForSession(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "idle" => "已保存",
        "running" => "提取中",
        "paused" => "已暂停",
        "completing" => "等待补全",
        "completed" => "已完成",
        "stopped" => "已停止",
        "interrupted" => "已中断",
        "error" or "failed" => "异常",
        _ => string.IsNullOrWhiteSpace(status) ? "未知状态" : status.Trim(),
    };

    public static string ForExport(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "completed" or "success" => "已导出",
        "failed" or "error" => "导出失败",
        "cancelled" or "canceled" => "已取消",
        "running" => "导出中",
        _ => string.IsNullOrWhiteSpace(status) ? "未知状态" : status.Trim(),
    };
}
