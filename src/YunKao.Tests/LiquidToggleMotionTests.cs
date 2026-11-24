using YunKao.Core.Presentation;
namespace YunKao.Tests;
public sealed class LiquidToggleMotionTests
{
    private static void Settle(LiquidToggleMotion m) { for(int i=0;i<900&&m.Moving;i++)m.Advance(1d/120); Assert.False(m.Moving); }
    [Fact]
    public void PreviewFollowsBothDirectionsButCommitsOnlyOnRelease()
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Pull(22); Settle(m);
        Assert.Equal(1,m.Progress); Assert.True(m.Held); Assert.Equal(1,m.Pressure);
        m.Pull(0); Settle(m); Assert.Equal(0,m.Progress);
        m.Pull(18); Assert.True(m.Release()); Settle(m); Assert.Equal(1,m.Progress); Assert.Equal(0,m.Pressure);
        m.Press(22); m.Pull(0); Assert.False(m.Release()); Settle(m); Assert.Equal(0,m.Progress);
    }
    [Fact]
    public void CancelRestoresOriginalStateAndTapTogglesExactlyOnce()
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Pull(22); m.Advance(.05);
        Assert.False(m.Release(true)); Settle(m); Assert.Equal(0,m.Progress);
        m.Press(10); Assert.True(m.Release()); Assert.True(m.Release()); Settle(m); Assert.Equal(1,m.Progress);
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)] [InlineData(144)]
    public void PreviewAndStretchStayWithinTheTrack(int fps)
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Pull(2000);
        for(int i=0;i<fps;i++) {m.Advance(1d/fps);Assert.InRange(m.Progress,0,1);Assert.InRange(m.ScaleX,1,1.18);}
        m.Release(); Settle(m); Assert.Equal(1,m.Progress); Assert.Equal(1,m.ScaleX);
    }
}
