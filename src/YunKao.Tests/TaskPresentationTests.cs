using YunKao.Core.Presentation;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class TaskPresentationTests
{
    private static TaskPresentationState Map(
        ExtractionStatus status,
        int saved = 0,
        bool practice = false,
        bool recovering = false,
        bool login = false)
        => TaskPresentation.From(new TaskSnapshot(status, saved, practice, recovering, login));

    [Fact]
    public void RecoveringOverridesEveryStatus()
    {
        foreach (ExtractionStatus status in Enum.GetValues<ExtractionStatus>())
        {
            TaskPresentationState state = Map(status, saved: 3, practice: true, recovering: true);
            Assert.Equal(TaskPrimaryAction.None, state.PrimaryAction);
            Assert.False(state.IsActionEnabled);
            Assert.Equal(TaskStatusKind.Recovering, state.StatusKind);
            Assert.False(state.ShowStop);
        }
    }

    [Theory]
    [InlineData(ExtractionStatus.Running, TaskPrimaryAction.Pause, true, TaskStatusKind.Running)]
    [InlineData(ExtractionStatus.Paused, TaskPrimaryAction.Resume, true, TaskStatusKind.Paused)]
    [InlineData(ExtractionStatus.Completing, TaskPrimaryAction.None, true, TaskStatusKind.Completing)]
    [InlineData(ExtractionStatus.Completed, TaskPrimaryAction.Export, false, TaskStatusKind.Completed)]
    [InlineData(ExtractionStatus.Error, TaskPrimaryAction.Restart, false, TaskStatusKind.Error)]
    public void ActiveStatusesMapToSinglePrimaryAction(
        ExtractionStatus status,
        TaskPrimaryAction action,
        bool showStop,
        TaskStatusKind kind)
    {
        TaskPresentationState state = Map(status, saved: 5, practice: true);
        Assert.Equal(action, state.PrimaryAction);
        Assert.Equal(showStop, state.ShowStop);
        Assert.Equal(kind, state.StatusKind);
    }

    [Fact]
    public void ErrorUsesDangerToneAndAllowsRestartWithoutQuestions()
    {
        TaskPresentationState state = Map(ExtractionStatus.Error, practice: true);
        Assert.Equal(TaskActionTone.Danger, state.Tone);
        Assert.True(state.CanRestart);
        Assert.False(state.CanClear);
    }

    [Fact]
    public void IdleWithQuestionsOnPracticePageCanContinue()
    {
        TaskPresentationState state = Map(ExtractionStatus.Idle, saved: 4, practice: true);
        Assert.Equal(TaskPrimaryAction.Start, state.PrimaryAction);
        Assert.Contains("4", state.StateText);
        Assert.True(state.CanClear);
        Assert.Equal("继续提取", state.ActionLabel);
    }

    [Theory]
    [InlineData(ExtractionStatus.Paused, true)]
    [InlineData(ExtractionStatus.Paused, false)]
    [InlineData(ExtractionStatus.Error, true)]
    [InlineData(ExtractionStatus.Error, false)]
    public void UnavailablePracticePageOffersExistingQuestionsForExport(ExtractionStatus status, bool login)
    {
        TaskPresentationState state = Map(status, saved: 4, login: login);
        Assert.Equal(TaskPrimaryAction.Export, state.PrimaryAction);
        Assert.False(state.CanRestart);
        Assert.Contains("练习页", state.HintText);
    }

    [Theory]
    [InlineData(ExtractionStatus.Paused)]
    [InlineData(ExtractionStatus.Error)]
    public void UnavailablePageDoesNotOfferAnImpossibleRetryWithNoQuestions(ExtractionStatus status)
    {
        TaskPresentationState state = Map(status, login: true);
        Assert.Equal(TaskPrimaryAction.None, state.PrimaryAction);
        Assert.False(state.CanRestart);
    }

    [Fact]
    public void IdleWithQuestionsOffPracticePageOffersExport()
    {
        TaskPresentationState state = Map(ExtractionStatus.Idle, saved: 4, practice: false);
        Assert.Equal(TaskPrimaryAction.Export, state.PrimaryAction);
        Assert.Equal(TaskActionTone.Accent, state.Tone);
    }

    [Fact]
    public void IdleLoginPageWaitsForLogin()
    {
        TaskPresentationState state = Map(ExtractionStatus.Idle, login: true);
        Assert.Equal(TaskPrimaryAction.None, state.PrimaryAction);
        Assert.Equal(TaskStatusKind.Waiting, state.StatusKind);
        Assert.Equal("请先登录", state.ActionLabel);
    }

    [Fact]
    public void IdlePracticePageWithoutQuestionsCanStart()
    {
        TaskPresentationState state = Map(ExtractionStatus.Idle, practice: true);
        Assert.Equal(TaskPrimaryAction.Start, state.PrimaryAction);
        Assert.Equal(TaskStatusKind.Ready, state.StatusKind);
    }

    [Fact]
    public void IdleElsewhereWaitsForPracticePage()
    {
        TaskPresentationState state = Map(ExtractionStatus.Idle);
        Assert.Equal(TaskPrimaryAction.None, state.PrimaryAction);
        Assert.False(state.CanClear);
        Assert.False(state.CanRestart);
    }

    [Fact]
    public void NegativeSavedCountIsClamped()
    {
        TaskPresentationState state = Map(ExtractionStatus.Idle, saved: -2, practice: true);
        Assert.False(state.CanClear);
        Assert.Equal(TaskPrimaryAction.Start, state.PrimaryAction);
    }

    [Theory]
    [InlineData("acrylic", "clear", 0.10)]
    [InlineData("mica", "standard", 0.06)]
    [InlineData("acrylic", "transparent", 0.02)]
    [InlineData("solid", "transparent", 1.0)]
    public void GlassTintOpacityFollowsMaterialAndClarity(string material, string clarity, double expected)
        => Assert.Equal(expected, AppearancePolicy.GetGlassTintOpacity(material, clarity), 3);

    [Theory]
    [InlineData("dark", "dark")]
    [InlineData("LIGHT", "light")]
    [InlineData("", "system")]
    [InlineData(null, "system")]
    [InlineData("neon", "system")]
    public void ThemeIsNormalized(string? input, string expected)
        => Assert.Equal(expected, AppearancePolicy.NormalizeTheme(input));
}
