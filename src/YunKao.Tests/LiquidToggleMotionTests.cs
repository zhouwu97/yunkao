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
    public void ExpandedLensAndTravelStayBoundedAtDifferentFrameRates(int fps)
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Pull(2000);
        for(int i=0;i<fps;i++)
        {
            m.Advance(1d/fps);
            Assert.InRange(m.Progress,0,1);
            Assert.InRange(30*m.ScaleX,30,58);
            Assert.InRange(24*m.ScaleY,23,34);
        }
        m.Release(); Settle(m); Assert.Equal(1,m.Progress); Assert.Equal(1,m.ScaleX);
    }

    [Fact]
    public void HeldLensGrowsBeyondTheRailAndReturnsToTheOrdinaryThumb()
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); Settle(m);
        Assert.Equal(52,30*m.ScaleX,6); Assert.Equal(32,24*m.ScaleY,6);
        var held=(m.ScaleX,m.ScaleY); m.Release(); Assert.Equal(held,(m.ScaleX,m.ScaleY));
        Settle(m); Assert.Equal(1,m.ScaleX); Assert.Equal(1,m.ScaleY); Assert.Equal(0,m.Pressure);
    }

    [Fact]
    public void RailColorWaitsForTheVisibleThumbToCrossTheMiddleInBothDirections()
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Pull(9); Settle(m);
        Assert.Equal(0,m.TrackProgress);
        m.Pull(20);
        while(m.Position<10)
        {
            Assert.Equal(0,m.TrackProgress); m.Advance(1d/240);
        }
        Settle(m); Assert.Equal(1,m.TrackProgress);
        m.Pull(11); Settle(m); Assert.Equal(1,m.TrackProgress);
        m.Pull(0);
        while(m.Position>=10)
        {
            Assert.Equal(1,m.TrackProgress); m.Advance(1d/240);
        }
        Settle(m); Assert.Equal(0,m.TrackProgress);
    }

    [Fact]
    public void TapWaitsForTheVisibleThumbBeforeColoringTheRail()
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Release();
        m.Advance(1d/120); Assert.Equal(0,m.TrackProgress);
        Settle(m); Assert.Equal(1,m.TrackProgress);
    }

    [Fact]
    public void RegrabPreservesTheCurrentPositionAndLensSize()
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Advance(.05);
        m.Release(); m.Advance(.03); var pose=(m.ScaleX,m.ScaleY,m.Position);
        m.Press(0); Assert.Equal(pose,(m.ScaleX,m.ScaleY,m.Position));
    }
}
