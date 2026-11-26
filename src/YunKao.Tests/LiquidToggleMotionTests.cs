using YunKao.Core.Presentation;
namespace YunKao.Tests;
public sealed class LiquidToggleMotionTests
{
    private static void Settle(LiquidToggleMotion m) { for(int i=0;i<900&&m.Moving;i++)m.Advance(1d/120); Assert.False(m.Moving); }
    [Theory]
    [InlineData(60, false)] [InlineData(120, false)]
    [InlineData(60, true)] [InlineData(120, true)]
    public void RapidRetapsContinueTowardTheNewEndWithoutRepeatingTheLift(int fps, bool on)
    {
        foreach (double elapsed in new[] { .18, .30, .42, .60 })
        {
            var m = new LiquidToggleMotion { Elastic = true };
            m.SetState(on, true); m.Press(10); m.Release();
            for (int i = 0; i < (int)Math.Round(elapsed * fps); i++) m.Advance(1d / fps);
            var pose = (m.Position, m.Pressure, m.Stretch, m.BendX);
            m.Press(10);
            Assert.Equal(pose, (m.Position, m.Pressure, m.Stretch, m.BendX));
            m.Advance(.02);
            double endpoint = on ? LiquidToggleMotion.Travel : 0;
            double distance = Math.Abs(m.Position - endpoint);
            Assert.Equal(on, m.Release());
            for (int i = 0; i < fps / 10; i++) m.Advance(1d / fps);
            Assert.True(Math.Abs(m.Position - endpoint) < distance * .85,
                $"Retap at {elapsed}s must turn toward its new endpoint within 100ms.");
            Settle(m);
            Assert.Equal(endpoint, m.Position); Assert.Equal(0, m.Pressure);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RepressWithSmallPointerJitterDoesNotBrakeTheGuidedJourney(bool on)
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(on, true); m.Press(10); m.Release();
        for (int i = 0; i < 30; i++) m.Advance(1d / 120);
        double before = on ? 1 - m.Progress : m.Progress;
        m.Press(10); m.Pull(11, 1);
        for (int i = 0; i < 8; i++) m.Advance(1d / 120);
        double after = on ? 1 - m.Progress : m.Progress;
        Assert.True(after > before + .10, "A new press must keep the current journey until drag takeover.");
        m.Release(true); Settle(m);
        Assert.Equal(on ? 0 : 1, m.Progress);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReversingOutsideTheTrackDiscardsExcessTravel(bool on)
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(on, true); m.Press(0);
        double direction = on ? -1 : 1;
        m.Pull(100 * direction); Settle(m);
        m.Pull(76 * direction); Settle(m);
        Assert.Equal(on ? 1 : 0, m.Progress);
        Assert.True(m.Held); Assert.Equal(1, m.Pressure);
        m.Pull(100 * direction); Settle(m);
        Assert.Equal(on ? 0 : 1, m.Progress);
        Assert.Equal(!on, m.Release()); Settle(m);
    }

    [Fact]
    public void RepressedLensStaysExpandedWhenItsPreviousJourneyArrives()
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(false, true); m.Press(10); m.Release();
        for (int i = 0; i < 30; i++) m.Advance(1d / 120);
        m.Press(10); Settle(m);
        Assert.True(m.Held); Assert.Equal(1, m.Progress); Assert.Equal(1, m.Pressure);
        m.Pull(-20); Settle(m);
        Assert.True(m.Held); Assert.Equal(0, m.Progress); Assert.Equal(1, m.Pressure);
        Assert.True(m.Release(true)); Settle(m);
        Assert.Equal(1, m.Progress); Assert.Equal(0, m.Pressure);
    }

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
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(120, false)] [InlineData(144, false)]
    [InlineData(30, true)] [InlineData(60, true)] [InlineData(120, true)] [InlineData(144, true)]
    public void ExpandedLensAndTravelStayBoundedAtDifferentFrameRates(int fps, bool elastic)
    {
        var m=new LiquidToggleMotion { Elastic = elastic }; m.SetState(false,true); m.Press(0); m.Pull(2000);
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

    [Theory]
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(120, false)]
    [InlineData(30, true)] [InlineData(60, true)] [InlineData(120, true)]
    public void TapLiftsAtTheStartingEndTravelsWhileGlassAndReleasesAtTheDestination(int fps, bool on)
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(on, true); m.Press(10); Assert.Equal(!on, m.Release());
        double previous = 0;
        bool liftedAtStart = false, arrivedWhileGlass = false, shrankAtDestination = false;
        int frames = 0;
        while (m.Moving && frames++ < fps * 2)
        {
            m.Advance(1d / fps);
            double travel = on ? 1 - m.Progress : m.Progress;
            Assert.True(travel >= previous - .001, "A tap must not reverse halfway through its travel.");
            if (travel < .02 && m.Pressure >= .8) liftedAtStart = true;
            if (travel > .94 && m.Pressure >= .9) arrivedWhileGlass = true;
            if (arrivedWhileGlass && m.Pressure < .5)
            {
                Assert.True(travel > .97); shrankAtDestination = true;
            }
            previous = travel;
        }
        Assert.True(liftedAtStart, "The lens must visibly rise at the original end before sliding.");
        Assert.True(arrivedWhileGlass, "The lens must remain expanded until it reaches the other end.");
        Assert.True(shrankAtDestination); Assert.False(m.Moving);
        Assert.InRange(frames / (double)fps, .55, 1.5);
        Assert.Equal(on ? 0 : 1, m.Progress); Assert.Equal(0, m.Pressure);
    }

    [Fact]
    public void DraggingAfterRepressTakesOverWithoutChangingTheCurrentPose()
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(false, true); m.Press(0); m.Release();
        for (int i = 0; i < 30; i++) m.Advance(1d / 120);
        var pose = (m.Position, m.Pressure, m.Stretch, m.BendX);
        m.Press(10);
        Assert.Equal(pose, (m.Position, m.Pressure, m.Stretch, m.BendX));
        m.Pull(-20); Assert.False(m.Release()); Settle(m);
        Assert.Equal(0, m.Progress); Assert.Equal(0, m.Pressure);
    }

    [Fact]
    public void RegrabPreservesTheCurrentPositionAndLensSize()
    {
        var m=new LiquidToggleMotion(); m.SetState(false,true); m.Press(0); m.Advance(.05);
        m.Release(); m.Advance(.03); var pose=(m.ScaleX,m.ScaleY,m.Position);
        m.Press(0); Assert.Equal(pose,(m.ScaleX,m.ScaleY,m.Position));
    }

    [Fact]
    public void ElasticHoldSettlesAtTheSameSizeAndStopsAllOpticalMotion()
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(false, true); m.Press(0, 22); m.Pull(20, 22); Settle(m);
        Assert.Equal(52, 30 * m.ScaleX, 6); Assert.Equal(32, 24 * m.ScaleY, 6);
        Assert.Equal(0, m.Energy); Assert.Equal(0, m.BendX); Assert.Equal(0, m.BendY);
        Assert.True(m.Held);
    }

    [Fact]
    public void ElasticReversalAndReleasePreserveThePoseAndRecoil()
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(false, true); m.Press(0, 22); Settle(m);
        m.Pull(20, 22); m.Advance(.05);
        Assert.True(m.BendX > .05); Assert.True(m.Stretch > 0);
        var pose = (m.Position, m.ScaleX, m.ScaleY, m.BendX, m.Energy);
        m.Pull(0, 22);
        Assert.Equal(pose, (m.Position, m.ScaleX, m.ScaleY, m.BendX, m.Energy));
        for (int i = 0; i < 15; i++) m.Advance(1d / 120);
        Assert.True(m.BendX < -.05);
        pose = (m.Position, m.ScaleX, m.ScaleY, m.BendX, m.Energy);
        m.Release();
        Assert.Equal(pose, (m.Position, m.ScaleX, m.ScaleY, m.BendX, m.Energy));
        bool recoiled = false;
        for (int i = 0; i < 120; i++) { m.Advance(1d / 120); recoiled |= m.BendX > .01; }
        Assert.True(recoiled);
        Settle(m); Assert.Equal(0, m.Energy); Assert.Equal(1, m.ScaleX);
    }

    [Fact]
    public void FastDragProducesMoreStrainAndBendThanSlowDrag()
    {
        (double Strain, double Bend) Peak(bool fast)
        {
            var m = new LiquidToggleMotion { Elastic = true };
            m.SetState(false, true); m.Press(0); Settle(m);
            double strain = 0, bend = 0;
            for (int i = 0; i < 120; i++)
            {
                m.Pull(fast ? 20 : Math.Min(20, 20d * i / 90)); m.Advance(1d / 120);
                strain = Math.Max(strain, m.Stretch); bend = Math.Max(bend, Math.Abs(m.BendX));
            }
            return (strain, bend);
        }
        var slow = Peak(false); var fast = Peak(true);
        Assert.True(fast.Strain > slow.Strain * 1.5);
        Assert.True(fast.Bend > slow.Bend * 1.5);
    }

    [Fact]
    public void VerticalPullBendsTheLensAndSnapClearsAllMotion()
    {
        var m = new LiquidToggleMotion { Elastic = true };
        m.SetState(true, true); m.Press(38, 22); m.Pull(38, 42); Settle(m);
        Assert.InRange(m.BendY, 1, 2.5); Assert.True(m.Held);
        m.SetState(true, true);
        // A held state ignores external semantic updates; cancellation releases it first.
        Assert.True(m.Held);
        m.Release(true); m.SetState(true, true);
        Assert.Equal(0, m.Energy); Assert.Equal(0, m.BendY); Assert.False(m.Moving);
    }
}
