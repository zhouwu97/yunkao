using YunKao.Core.Presentation;

namespace YunKao.Tests;

public sealed class LiquidSurfaceMotionTests
{
    [Theory]
    [InlineData(5, 100, 0, .5)]
    [InlineData(395, 100, 1, .5)]
    [InlineData(200, 5, .5, 0)]
    [InlineData(200, 195, .5, 1)]
    public void PullInfluenceStartsOnTheNearestEdge(double x, double y, double expectedX, double expectedY)
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(x, y, 400, 200);
        Assert.Equal(expectedX, motion.GrabX);
        Assert.Equal(expectedY, motion.GrabY);
        motion.Pull(x + 100, y - 50);
        Settle(motion);
        Assert.Equal(expectedX, motion.GrabX);
        Assert.Equal(expectedY, motion.GrabY);
    }

    private static void Settle(LiquidSurfaceMotion motion)
    {
        for (int i = 0; i < 900 && motion.Moving; i++) motion.Advance(1d / 120);
        Assert.False(motion.Moving);
    }

    [Fact]
    public void HoldingAPullKeepsTheShapeDeformedAtRest()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(120, 80, 400, 200);
        motion.Pull(230, 130);
        Settle(motion);
        Assert.True(motion.Held);
        Assert.InRange(motion.PullX, 5, 19);
        Assert.True(motion.PullX > 3);
        Assert.True(motion.PullY > 1);
        Assert.Equal(1, motion.Pressure);
    }

    [Fact]
    public void ReleasePreservesTheCurrentShapeThenReturnsToRest()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 200, 100);
        motion.Pull(80, 35);
        motion.Advance(.08);
        var before = (motion.PullX, motion.PullY, motion.GrabX, motion.GrabY, motion.Pressure);
        motion.Release();
        Assert.Equal(before, (motion.PullX, motion.PullY, motion.GrabX, motion.GrabY, motion.Pressure));
        Settle(motion);
        Assert.False(motion.Held);
        Assert.Equal(0, motion.Energy);
        Assert.Equal(0, motion.PullX);
        Assert.Equal(0, motion.PullY);
        Assert.Equal(0, motion.Pressure);
    }

    [Fact]
    public void RegrabDoesNotRestartTheSprings()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 200, 100);
        motion.Pull(80, 0);
        motion.Advance(.08);
        motion.Release();
        motion.Advance(.02);
        var before = (motion.PullX, motion.PullY, motion.GrabX, motion.GrabY, motion.Pressure);
        motion.Press(35, 12, 200, 100);
        Assert.Equal(before, (motion.PullX, motion.PullY, motion.GrabX, motion.GrabY, motion.Pressure));
    }

    [Fact]
    public void ReversingThePullChangesBendContinuously()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 200, 100);
        motion.Pull(80, 0);
        Settle(motion);
        double bend = motion.PullX;
        double offset = -5; // Pressing the left edge adds a small outward bulge.
        motion.Pull(-80, 0);
        Assert.Equal(bend, motion.PullX);
        motion.Advance(1d / 120);
        Assert.True(motion.PullX > 0);
        Settle(motion);
        Assert.InRange(motion.PullX, -bend + 2 * offset - .001, -bend + 2 * offset + .001);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    public void DifferentFrameRatesStayStable(int fps)
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 400, 240);
        for (int i = 0; i < fps; i++)
        {
            motion.Pull(i * 180d / fps, i * 65d / fps);
            motion.Advance(1d / fps);
            Assert.InRange(motion.Energy, 0, .376);
            Assert.InRange(motion.PullX, -25, 25);
        }
        motion.Release();
        for (int i = 0; i < fps * 4; i++) motion.Advance(1d / fps);
        Assert.False(motion.Moving);
        Assert.Equal(0, motion.Energy);
    }

    [Fact]
    public void VeryWidePanelsKeepDeformationWithinTheCapturePadding()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 2000, 40);
        motion.Pull(10000, 0);
        for (int i = 0; i < 120; i++)
        {
            motion.Advance(1d / 120);
            Assert.True(Math.Sqrt(motion.PullX * motion.PullX + motion.PullY * motion.PullY) <= 25.001);
        }
    }

    [Fact]
    public void ResetCancelsAnActivePullImmediately()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 200, 100);
        motion.Pull(80, 0);
        motion.Advance(.08);
        motion.Reset();
        Assert.False(motion.Moving);
        Assert.False(motion.Held);
        Assert.Equal(0, motion.Energy);
        Assert.Equal(0, motion.Pressure);
    }

    [Fact]
    public void InvalidSamplesDoNotPoisonTheMotion()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(double.NaN, 0, 200, 100);
        Assert.False(motion.Held);
        motion.Press(0, 0, 200, 100);
        motion.Pull(double.PositiveInfinity, 5);
        motion.Advance(double.NaN);
        motion.Advance(-1);
        Assert.Equal(0, motion.Energy);
        motion.Release();
        Settle(motion);
    }

    [Fact]
    public void APressBulgesLocallyAndReleaseHasVisibleRecoil()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(200, 4, 400, 240);
        Settle(motion);
        Assert.Equal(-5, motion.PullY, 3);
        motion.Pull(200, -95);
        Settle(motion);
        double held = motion.PullY;
        Assert.True(held < -18);
        motion.Release();
        double oppositePeak = 0;
        for (int frame = 0; frame < 90; frame++)
        {
            motion.Advance(1d / 120);
            oppositePeak = Math.Max(oppositePeak, motion.PullY);
        }
        Assert.True(oppositePeak > 4, $"Recoil was only {oppositePeak:F2} DIP.");
        Settle(motion);
        Assert.Equal(0, motion.Energy);
    }
}
