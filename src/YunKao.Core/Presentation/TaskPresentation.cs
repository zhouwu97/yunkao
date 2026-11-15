using YunKao.Core.Services;

namespace YunKao.Core.Presentation;

/// <summary>任务面板当前唯一的主操作。</summary>
public enum TaskPrimaryAction
{
    None,
    Start,
    Pause,
    Resume,
    Export,
    Restart,
}

/// <summary>主操作按钮的视觉强度。</summary>
public enum TaskActionTone
{
    /// <summary>深蓝主按钮。</summary>
    Accent,

    /// <summary>玻璃次按钮。</summary>
    Neutral,

    /// <summary>警示（可重试的异常）。</summary>
    Danger,
}

/// <summary>标题栏与工具栏使用的简短状态类别。</summary>
public enum TaskStatusKind
{
    Ready,
    Waiting,
    Running,
    Paused,
    Completing,
    Completed,
    Error,
    Recovering,
}

/// <summary>派生界面所需的任务输入。全部来自真实会话与页面状态。</summary>
public readonly record struct TaskSnapshot(
    ExtractionStatus Status,
    int SavedCount,
    bool IsPracticeReady,
    bool IsBrowserRecovering,
    bool IsLoginPage);

/// <summary>任务面板、工具栏状态条和标题栏共用的一份展示状态。</summary>
public sealed record TaskPresentationState(
    string Glyph,
    string StateText,
    string StepText,
    string HintText,
    string ActionLabel,
    TaskPrimaryAction PrimaryAction,
    TaskActionTone Tone,
    bool ShowStop,
    bool CanClear,
    bool CanRestart,
    TaskStatusKind StatusKind,
    string StatusLabel)
{
    public bool IsActionEnabled => PrimaryAction != TaskPrimaryAction.None;
}

/// <summary>
/// 把任务状态映射为界面文案与操作。纯函数，保证任何时刻只有一个明确的主操作。
/// </summary>
public static class TaskPresentation
{
    public static TaskPresentationState From(TaskSnapshot snapshot)
    {
        int saved = Math.Max(0, snapshot.SavedCount);
        bool canClear = saved > 0;
        bool canRestart = saved > 0 || snapshot.Status == ExtractionStatus.Error;

        if (snapshot.IsBrowserRecovering)
        {
            return new(
                "", "页面恢复中", "正在恢复",
                "正在重建页面组件，恢复完成前暂不能提取",
                "正在恢复…", TaskPrimaryAction.None, TaskActionTone.Neutral,
                ShowStop: false, canClear, canRestart,
                TaskStatusKind.Recovering, "页面恢复中");
        }

        return snapshot.Status switch
        {
            ExtractionStatus.Running => new(
                "", $"提取中 · 已保存 {saved} 题", "正在连续提取",
                "保持页面打开即可，提取完成将自动提示",
                "暂停提取", TaskPrimaryAction.Pause, TaskActionTone.Neutral,
                ShowStop: true, canClear, canRestart,
                TaskStatusKind.Running, $"正在提取 · {saved} 题"),

            ExtractionStatus.Paused => new(
                "", $"已暂停 · 已存 {saved} 题", "任务暂停中",
                "当前不会自动跳转下一题，点击可随时继续",
                "继续提取", TaskPrimaryAction.Resume, TaskActionTone.Accent,
                ShowStop: true, canClear, canRestart,
                TaskStatusKind.Paused, $"已暂停 · {saved} 题"),

            ExtractionStatus.Completing => new(
                "", "等待 AI 补全", "题目已提取完毕",
                "正在等待后台 AI 补全解析，请稍候…",
                "AI 处理中…", TaskPrimaryAction.None, TaskActionTone.Neutral,
                ShowStop: false, canClear, canRestart,
                TaskStatusKind.Completing, "等待 AI 完成"),

            ExtractionStatus.Completed => new(
                "", "提取完成 · 题库就绪", "下一步：导出题库",
                $"{saved} 道题目已完整保存在本机，可直接导出",
                "导出题库", TaskPrimaryAction.Export, TaskActionTone.Accent,
                ShowStop: false, canClear, canRestart,
                TaskStatusKind.Completed, $"已完成 · {saved} 题"),

            ExtractionStatus.Error => new(
                "", "提取异常 · 可重试", "遇到异常",
                "提取过程中断，点击重新开始或检查网页状态",
                "重新开始", TaskPrimaryAction.Restart, TaskActionTone.Danger,
                ShowStop: false, canClear, canRestart,
                TaskStatusKind.Error, "任务异常"),

            _ => FromIdle(snapshot, saved, canClear, canRestart),
        };
    }

    private static TaskPresentationState FromIdle(TaskSnapshot snapshot, int saved, bool canClear, bool canRestart)
    {
        if (saved > 0)
        {
            return snapshot.IsPracticeReady
                ? new(
                    "", $"已就绪 · 本地 {saved} 题", "可以继续提取",
                    "当前页面为练习页，可继续提取新题",
                    "开始提取", TaskPrimaryAction.Start, TaskActionTone.Accent,
                    ShowStop: false, canClear, canRestart,
                    TaskStatusKind.Ready, $"本地 {saved} 题")
                : new(
                    "", $"已就绪 · 本地 {saved} 题", "可直接导出",
                    "可直接在下方导出，或进入练习页继续提取",
                    "导出题库", TaskPrimaryAction.Export, TaskActionTone.Accent,
                    ShowStop: false, canClear, canRestart,
                    TaskStatusKind.Ready, $"本地 {saved} 题");
        }

        if (snapshot.IsLoginPage)
        {
            return new(
                "", "待机 · 请先登录", "第一步：登录系统",
                "在右侧网页中登录，进入练习页面后即可提取",
                "请先登录", TaskPrimaryAction.None, TaskActionTone.Neutral,
                ShowStop: false, canClear, canRestart,
                TaskStatusKind.Waiting, "等待登录");
        }

        if (snapshot.IsPracticeReady)
        {
            return new(
                "", "页面已就绪", "下一步：可以开始提取",
                "当前页面已识别为练习页面，点击开始提取",
                "开始提取", TaskPrimaryAction.Start, TaskActionTone.Accent,
                ShowStop: false, canClear, canRestart,
                TaskStatusKind.Ready, "练习页已就绪");
        }

        return new(
            "", "待机 · 等待进入练习页面", "下一步：进入练习页",
            "检测到练习页面后才能开始连续提取",
            "等待进入练习页", TaskPrimaryAction.None, TaskActionTone.Neutral,
            ShowStop: false, canClear, canRestart,
            TaskStatusKind.Waiting, "等待练习页");
    }
}
