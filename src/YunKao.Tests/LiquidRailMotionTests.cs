using YunKao.Core.Presentation;

namespace YunKao.Tests;

public sealed class LiquidRailMotionTests
{
    [Theory]
    [InlineData(1.30, 100, 1)]
    [InlineData(1.30, 2100, 2)]
    [InlineData(2.70, -2100, 2)]
    [InlineData(-.50, -1800, 0)]
    [InlineData(4.80, 1800, 4)]
    public void Release_stays_on_the_rail_and_projects_at_most_one_neighbor(double position, double speed, int expected)
        => Assert.Equal(expected, LiquidRailMotion.ReleaseIndex(position, speed, 49, 5));

    [Fact]
    public void Reversal_preserves_momentum_and_releases_pressure()
    {
        var motion = new LiquidRailMotion();
        motion.Aim(190, true, 1);
        motion.Advance(.06);
        double position = motion.Position;
        Assert.True(motion.Pressure > 0 && motion.Stretch > 0);
        motion.Aim(0);
        Assert.Equal(position, motion.Position);
        motion.Advance(.003);
        Assert.True(motion.Position > position);
        for (int i = 0; i < 240 && motion.Moving; i++) motion.Advance(1d / 60);
        Assert.False(motion.Moving);
        Assert.Equal(0, motion.Position);
        Assert.Equal(0, motion.Pressure);
        Assert.Equal(0, motion.Stretch);
    }

    [Fact]
    public void Frame_rates_produce_consistent_positions_and_snap_resets_deformation()
    {
        var a = new LiquidRailMotion();
        var b = new LiquidRailMotion();
        a.Aim(100, true, .7, 6);
        b.Aim(100, true, .7, 6);
        for (int i = 0; i < 6; i++) a.Advance(1d / 30);
        for (int i = 0; i < 24; i++) b.Advance(1d / 120);
        Assert.InRange(Math.Abs(a.Position - b.Position), 0, 2);
        a.Snap(49);
        Assert.Equal(49, a.Position);
        Assert.False(a.Moving || a.Held);
        Assert.Equal(0, a.Pressure + a.Stretch + a.Cross + a.Bend);
    }

    [Fact]
    public void Faster_drag_has_more_deformation_and_a_held_surface_relaxes()
    {
        static (double Stretch, double Bend) Drag(double duration)
        {
            var motion = new LiquidRailMotion();
            double stretch = 0, bend = 0;
            int frames = (int)Math.Round(duration * 120);
            for (int i = 1; i <= frames; i++)
            {
                motion.Aim(180d * i / frames, held: true, pressure: 1, cross: 6);
                motion.Advance(1d / 120);
                stretch = Math.Max(stretch, motion.Stretch);
                bend = Math.Max(bend, Math.Abs(motion.Bend));
            }
            for (int i = 0; i < 600 && motion.Moving; i++) motion.Advance(1d / 120);
            Assert.False(motion.Moving);
            Assert.Equal(0, motion.Stretch);
            Assert.Equal(0, motion.Bend);
            Assert.Equal(0, motion.CrossBend);
            Assert.True(motion.Held);
            return (stretch, bend);
        }
        var slow = Drag(1.8);
        var fast = Drag(.18);
        Assert.True(fast.Stretch > slow.Stretch * 2);
        Assert.True(fast.Bend > slow.Bend * 2);
    }
}
