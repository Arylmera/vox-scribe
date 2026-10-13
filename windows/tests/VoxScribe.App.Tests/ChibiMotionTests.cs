using Shouldly;
using VoxScribe.App.Views.Chibi;
using Xunit;

namespace VoxScribe.AppTests;

/// <summary>Mascot motion is a pure, quantised function of time and level.</summary>
public sealed class ChibiMotionTests
{
    private static IEnumerable<double> Seconds(double until, double step = 0.033)
    {
        for (var t = 0.0; t < until; t += step) yield return t;
    }

    [Fact]
    public void Bob_floats_within_three_pixels_in_half_pixel_steps()
    {
        foreach (var t in Seconds(10))
        {
            var y = ChibiMotion.Bob(t);
            y.ShouldBeInRange(-3 - 1e-9, 1e-9);
            (y * 2).ShouldBe(Math.Round(y * 2), 1e-9);
        }
    }

    [Fact]
    public void Idle_bob_changes_rarely_so_idle_frames_mostly_repaint_nothing()
    {
        // 30 frames a second for 3.2 s (one period): a 0.5 px grid on a 3 px swing allows at
        // most a dozen distinct steps, so most frames set an unchanged value.
        var changes = 0;
        var last = ChibiMotion.Bob(0);
        foreach (var t in Seconds(3.2))
        {
            var y = ChibiMotion.Bob(t);
            if (y != last) changes++;
            last = y;
        }

        changes.ShouldBeLessThan(30);
    }

    [Fact]
    public void Talk_opens_with_the_voice()
    {
        ChibiMotion.Talk(0).ShouldBe(0.3, 1e-9);
        ChibiMotion.Talk(1).ShouldBe(1.1, 1e-9);
        ChibiMotion.Talk(2).ShouldBe(1.1, 1e-9, "level is clamped");
        ChibiMotion.Talk(0.5).ShouldBeGreaterThan(ChibiMotion.Talk(0.2));
    }

    [Fact]
    public void Eyes_blink_briefly_every_four_seconds()
    {
        ChibiMotion.Blinking(1.0).ShouldBeFalse();
        ChibiMotion.Blinking(3.9).ShouldBeTrue();
        Seconds(4).Count(ChibiMotion.Blinking).ShouldBeLessThan(10);
    }

    [Fact]
    public void The_done_hop_jumps_then_settles()
    {
        ChibiMotion.Hop(0).ShouldBe(0);
        ChibiMotion.Hop(0.225).ShouldBeLessThan(-4);
        ChibiMotion.Hop(3).ShouldBeGreaterThan(-0.5);
    }

    [Fact]
    public void Eq_bars_stay_in_range_and_rise_with_level()
    {
        foreach (var t in Seconds(2))
            for (var i = 0; i < 5; i++)
            {
                ChibiMotion.Eq(0, i, t).ShouldBe(0.25, 1e-9);
                ChibiMotion.Eq(1, i, t).ShouldBeInRange(0.25 - 1e-9, 1 + 1e-9);
            }
    }

    [Fact]
    public void Pulse_and_rise_fade_but_never_vanish_or_overflow()
    {
        foreach (var t in Seconds(4))
        {
            ChibiMotion.Pulse(t).ShouldBeInRange(0.35 - 1e-9, 1 + 1e-9);
            var (y, opacity) = ChibiMotion.Rise(t, 0);
            y.ShouldBeInRange(-14 - 1e-9, 1e-9);
            opacity.ShouldBeInRange(-1e-9, 0.9 + 1e-9);
        }
    }

    [Fact]
    public void Squish_flattens_with_the_voice()
    {
        ChibiMotion.Squish(0).ShouldBe((1.0, 1.0));
        var (x, y) = ChibiMotion.Squish(1);
        x.ShouldBeGreaterThan(1);
        y.ShouldBeLessThan(1);
    }

    [Fact]
    public void Swivel_swings_forty_degrees_in_whole_degrees()
    {
        foreach (var t in Seconds(5))
        {
            var a = ChibiMotion.Swivel(t);
            a.ShouldBeInRange(-40, 40);
            a.ShouldBe(Math.Round(a));
        }

        Seconds(2.4).Max(ChibiMotion.Swivel).ShouldBeGreaterThan(35);
    }

    [Fact]
    public void Rings_grow_and_fade()
    {
        foreach (var t in Seconds(3))
            for (var i = 0; i < 3; i++)
            {
                var (scale, opacity) = ChibiMotion.Ring(t, i);
                scale.ShouldBeInRange(0.9 - 1e-9, 1.5 + 1e-9);
                opacity.ShouldBeInRange(-1e-9, 0.8 + 1e-9);
            }

        ChibiMotion.Ring(0, 0).Scale.ShouldBeLessThan(ChibiMotion.Ring(0.9, 0).Scale);
        ChibiMotion.Ring(0, 0).Opacity.ShouldBeGreaterThan(ChibiMotion.Ring(0.9, 0).Opacity);
    }

    [Fact]
    public void Trot_hops_a_whole_pixel_at_a_time()
    {
        var seen = new HashSet<double>();
        foreach (var t in Seconds(2))
        {
            var y = ChibiMotion.Trot(t, 0);
            (y == 0 || y == -6).ShouldBeTrue($"t={t} y={y}");
            seen.Add(y);
        }

        seen.ShouldBe(new[] { 0.0, -6.0 }, ignoreOrder: true);
    }

    [Fact]
    public void Letters_fall_and_fade()
    {
        foreach (var t in Seconds(3))
            for (var i = 0; i < 4; i++)
            {
                var (y, opacity) = ChibiMotion.Fall(t, i);
                y.ShouldBeInRange(-1e-9, 52 + 1e-9);
                opacity.ShouldBeInRange(-1e-9, 1 + 1e-9);
            }

        ChibiMotion.Fall(1.0, 0).Y.ShouldBeGreaterThan(ChibiMotion.Fall(0.2, 0).Y);
    }
}
