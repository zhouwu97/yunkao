using System.Numerics;
using System.Text.Json;
using YunKao.Core.Presentation;

namespace YunKao.Tests;

public sealed class LiquidSurfaceMotionTests
{
    [Fact]
    public void TrajectoryMatchesThePinnedUpstreamController()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/reference-tether.json")));
        Assert.Equal("7a424c93b9be13cf835b91ea67495fec2ec0584e", document.RootElement.GetProperty("revision").GetString());
        var motion = new LiquidSurfaceMotion();
        foreach (var frame in document.RootElement.GetProperty("frames").EnumerateArray())
        {
            double Value(string name) => frame.GetProperty(name).GetDouble();
            switch (frame.GetProperty("action").GetString())
            {
                case "press": motion.Press(Value("x"), Value("y"), 400, 200); break;
                case "pull": motion.Pull(Value("x"), Value("y")); break;
                case "release": motion.Release(); break;
            }
            motion.Advance(1d / 60);
            Assert.Equal(Value("positionX"), motion.PositionX, 8);
            Assert.Equal(Value("positionY"), motion.PositionY, 8);
            Assert.Equal(Value("pressure"), motion.Pressure, 8);
            Assert.Equal(Value("stretch"), motion.Stretch, 8);
            Assert.Equal(Value("angle"), motion.Angle, 8);
            Assert.Equal(Value("bendX"), motion.BendX, 8);
            Assert.Equal(Value("bendY"), motion.BendY, 8);
            Assert.Equal(Value("energy"), motion.Energy, 8);
        }
        Assert.False(motion.Moving);
    }

    private static void Settle(LiquidSurfaceMotion motion)
    {
        for (int i = 0; i < 1200 && motion.Moving; i++) motion.Advance(1d / 120);
        Assert.False(motion.Moving);
    }

    [Theory]
    [InlineData(75)]
    [InlineData(18)]
    public void TetherUsesTheComponentTravelLimitAndReleasesToItsOrigin(double travel)
    {
        var motion = new LiquidSurfaceMotion(travel);
        motion.Press(0, 0, 400, 200);
        motion.Pull(2000, -2000); Settle(motion);
        Assert.Equal(travel, motion.PositionX);
        Assert.Equal(-travel, motion.PositionY);
        Assert.Equal(1.0245, motion.Lift, 8);
        Assert.Equal(0, motion.Stretch);
        Assert.Equal(0, motion.BendX);
        var center = Vector2.Transform(new Vector2(200, 100), motion.CreatePose(400, 200));
        Assert.Equal(200 + travel, center.X, 3);
        Assert.Equal(100 - travel, center.Y, 3);
        motion.Release(); Settle(motion);
        Assert.Equal(Matrix3x2.Identity, motion.CreatePose(400, 200));
    }

    [Fact]
    public void HostTuningLimitsTravelWithoutChangingTheReferenceSpring()
    {
        var panel = new LiquidSurfaceMotion(12, dragGain: .18);
        panel.Press(0, 0, 400, 200); panel.Pull(50, -25); Settle(panel);
        Assert.Equal(9, panel.PositionX); Assert.Equal(-4.5, panel.PositionY);
        panel.Pull(2000, -2000); Settle(panel);
        Assert.Equal(12, panel.PositionX); Assert.Equal(-12, panel.PositionY);
        panel.Release(); Settle(panel);
        Assert.Equal(Matrix3x2.Identity, panel.CreatePose(400, 200));
    }

    [Fact]
    public void BoundsKeepTheTetherInsideTheViewport()
    {
        var motion = new LiquidSurfaceMotion();
        motion.SetBounds(-12, 9, -6, 30);
        motion.Press(0, 0, 400, 200); motion.Pull(-200, 200); Settle(motion);
        Assert.Equal(-12, motion.PositionX); Assert.Equal(30, motion.PositionY);
        motion.Pull(200, -200); Settle(motion);
        Assert.Equal(9, motion.PositionX); Assert.Equal(-6, motion.PositionY);
    }

    [Fact]
    public void ReleaseReversalAndRegrabPreserveTheCurrentPose()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 400, 200); motion.Pull(70, 20); motion.Advance(.05);
        var before = motion.CreatePose(400, 200);
        motion.Pull(-70, -20); Assert.Equal(before, motion.CreatePose(400, 200));
        motion.Release(); Assert.Equal(before, motion.CreatePose(400, 200));
        motion.Press(100, 50, 400, 200); Assert.Equal(before, motion.CreatePose(400, 200));
        motion.Release(); Settle(motion);
        Assert.Equal(Matrix3x2.Identity, motion.CreatePose(400, 200));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    public void FrameRatesRemainStableAndReleaseHasVisibleRecoil(int fps)
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 400, 200); motion.Pull(75, 0); Settle(motion);
        motion.Release(); double opposite = 0;
        for (int i = 0; i < fps * 4; i++)
        {
            motion.Advance(1d / fps);
            opposite = Math.Min(opposite, motion.PositionX);
            Assert.InRange(motion.PositionX, -25, 80);
            Assert.InRange(motion.Stretch, -.2, .34);
        }
        Assert.True(opposite < -5, $"Recoil was only {opposite:F2} DIP at {fps} FPS");
        Assert.False(motion.Moving); Assert.Equal(0, motion.Energy);
    }

    [Fact]
    public void TransformOriginDoesNotChangeTheVisiblePose()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(0, 0, 400, 200); motion.Pull(75, 40); motion.Advance(.05);
        var point = new Vector2(31, 53); var center = new Vector2(200, 100);
        Assert.True(Vector2.Distance(Vector2.Transform(point, motion.CreatePose(400, 200)),
            Vector2.Transform(point - center, motion.CreatePose(400, 200, .5, .5)) + center) < .001);
    }

    [Fact]
    public void ResetAndInvalidSamplesDoNotLeaveAnActiveAnimation()
    {
        var motion = new LiquidSurfaceMotion();
        motion.Press(double.NaN, 0, 400, 200); Assert.False(motion.Held);
        motion.Press(0, 0, 400, 200); motion.Pull(double.PositiveInfinity, 5);
        motion.Advance(double.NaN); motion.Advance(-1); Assert.Equal(0, motion.Energy);
        motion.Pull(75, 40); motion.Advance(.05); motion.Reset();
        Assert.False(motion.Held); Assert.False(motion.Moving);
        Assert.Equal(Matrix3x2.Identity, motion.CreatePose(400, 200));
    }
}
