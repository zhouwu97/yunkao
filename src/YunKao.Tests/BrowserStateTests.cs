using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class BrowserStateTests
{
    [Fact]
    public void Practice_ready_requires_a_non_login_practice_page()
    {
        Assert.True(new BrowserPageState(false, true).IsPracticeReady);
        Assert.False(new BrowserPageState(true, true).IsPracticeReady);
        Assert.False(new BrowserPageState(false, false).IsPracticeReady);
    }

    [Theory]
    [InlineData("https://www.cctrcloud.net/api/login", "Fetch", BrowserResourceKind.AuthenticationApi)]
    [InlineData("https://www.cctrcloud.net/practice/123/questions", "XmlHttpRequest", BrowserResourceKind.QuestionApi)]
    [InlineData("https://www.cctrcloud.net/api/practice.json", "Fetch", BrowserResourceKind.QuestionApi)]
    [InlineData("https://www.cctrcloud.net/practice/123/banner.png", "Image", BrowserResourceKind.Other)]
    [InlineData("https://example.com/practice/123", "Fetch", BrowserResourceKind.Other)]
    public void Classifies_only_cloud_auth_and_question_resources(
        string url,
        string context,
        BrowserResourceKind expected)
    {
        Assert.Equal(expected, CloudResourceClassifier.Classify(new Uri(url), context));
    }

    [Fact]
    public void Main_document_errors_are_reportable_but_unrelated_statuses_are_not()
    {
        Assert.Equal(
            BrowserResourceKind.MainDocument,
            CloudResourceClassifier.Classify(new Uri("https://www.cctrcloud.net/"), "Document"));
        Assert.Equal(
            BrowserResourceKind.MainDocument,
            CloudResourceClassifier.Classify(new Uri("https://www.cctrcloud.net/"), ""));
        Assert.True(CloudResourceClassifier.ShouldReportStatus(504));
        Assert.False(CloudResourceClassifier.ShouldReportStatus(404));
    }

    [Fact]
    public void Recovery_cannot_be_completed_before_navigation_bridge_and_page_state()
    {
        var recovery = new BrowserRecoveryStateMachine();
        Assert.True(recovery.Begin());
        Assert.False(recovery.CanContinue);

        recovery.MarkNavigationCompleted(true);
        recovery.MarkBridgeReady();
        Assert.Equal(BrowserRecoveryPhase.Recovering, recovery.Phase);
        Assert.False(recovery.CanContinue);

        recovery.MarkPageState(true);
        Assert.Equal(BrowserRecoveryPhase.Completed, recovery.Phase);
        Assert.True(recovery.CanContinue);
    }

    [Fact]
    public void Recovery_of_a_non_practice_page_completes_without_opening_continue()
    {
        var recovery = new BrowserRecoveryStateMachine();
        recovery.Begin();
        recovery.MarkNavigationCompleted(true);
        recovery.MarkBridgeReady();
        recovery.MarkPageState(false);

        Assert.Equal(BrowserRecoveryPhase.Completed, recovery.Phase);
        Assert.False(recovery.CanContinue);
    }

    [Theory]
    [InlineData(ExtractionStatus.Idle, true, false, true)]
    [InlineData(ExtractionStatus.Idle, false, false, false)]
    [InlineData(ExtractionStatus.Paused, true, false, false)]
    [InlineData(ExtractionStatus.Idle, true, true, false)]
    public void Start_button_is_gated_by_practice_readiness_and_recovery(
        ExtractionStatus status,
        bool isPracticeReady,
        bool isBrowserRecovering,
        bool expected)
    {
        Assert.Equal(expected, ExtractionControlPolicy.CanStart(status, isPracticeReady, isBrowserRecovering));
    }

    [Fact]
    public void Resume_button_is_disabled_while_recovery_or_page_navigation_is_incomplete()
    {
        Assert.True(ExtractionControlPolicy.CanResume(ExtractionStatus.Paused, true, false));
        Assert.False(ExtractionControlPolicy.CanResume(ExtractionStatus.Paused, false, false));
        Assert.False(ExtractionControlPolicy.CanResume(ExtractionStatus.Paused, true, true));
    }
}
