namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Every mascot motion as a pure function of time (seconds) and input level (0–1).
/// </summary>
/// <remarks>
/// Values are quantised: the mascot is on screen all day, and a property set to the value it
/// already holds raises nothing and repaints nothing, so a coarse grid keeps idle frames free.
/// Driven by <see cref="HudWindow"/>'s existing 33 ms tick rather than Avalonia animations,
/// which would repaint every frame anyway and fight layout over offsets.
/// </remarks>
internal static class ChibiMotion
{
    private const double BobPeriod = 3.2;
    private const double BobDepth = 3;
    private const double HalfPixel = 0.5;
    private const double HopHeight = 8;
    private const double HopBounce = 0.45;
    private const double HopDecay = 1.5;
    private const double BlinkPeriod = 4;
    private const double BlinkLength = 0.15;
    private const double TalkClosed = 0.3;
    private const double TalkRange = 0.8;
    private const double ScaleStep = 0.05;
    private const double EqFloor = 0.25;
    private const double EqWobbleSpeed = 12;
    private const double EqWobbleSpread = 1.7;
    private const double EqWobble = 0.3;
    private const double PulsePeriod = 0.9;
    private const double PulseLow = 0.35;
    private const double SpinDegreesPerSecond = 90;
    private const double SpinStep = 6;
    private const double FlapPeriod = 0.5;
    private const double FlapNarrowest = 0.6;
    private const double WavePeriod = 0.6;
    private const double WaveHeight = 6;
    private const double SquishWiden = 0.06;
    private const double SquishFlatten = 0.1;
    private const double RisePeriod = 1.6;
    private const double RiseHeight = 14;
    private const double RiseStart = 0.9;
    private const double RiseStagger = 0.8;

    /// <summary><paramref name="value"/> on a grid of <paramref name="step"/>.</summary>
    public static double Quantize(double value, double step) => Math.Round(value / step) * step;

    /// <summary>Idle float: 0 to −3 px and back over 3.2 s.</summary>
    public static double Bob(double t) =>
        Quantize(-(BobDepth / 2) * (1 - Math.Cos(2 * Math.PI * t / BobPeriod)), HalfPixel);

    /// <summary>The done hop: bounces of 0.45 s, decaying to rest. Negative is up.</summary>
    public static double Hop(double sinceDone) =>
        Quantize(-HopHeight * Math.Abs(Math.Sin(Math.PI * sinceDone / HopBounce)) * Math.Exp(-sinceDone * HopDecay), HalfPixel);

    /// <summary>True during the last 0.15 s of every 4 s.</summary>
    public static bool Blinking(double t) => t % BlinkPeriod > BlinkPeriod - BlinkLength;

    /// <summary>Mouth height scale: 0.3 closed to 1.1 at full voice.</summary>
    public static double Talk(double level) => Quantize(TalkClosed + (TalkRange * Math.Clamp(level, 0, 1)), ScaleStep);

    /// <summary>Equaliser bar height scale, 0.25–1: the level with a per-bar wobble.</summary>
    public static double Eq(double level, int index, double t)
    {
        var wobble = 1 - EqWobble + (EqWobble * Math.Sin((t * EqWobbleSpeed) + (index * EqWobbleSpread)));
        return Quantize(EqFloor + ((1 - EqFloor) * Math.Clamp(level * wobble, 0, 1)), ScaleStep);
    }

    /// <summary>Glow opacity, 0.35–1 over 0.9 s.</summary>
    public static double Pulse(double t) =>
        Quantize(PulseLow + ((1 - PulseLow) * (0.5 + (0.5 * Math.Cos(2 * Math.PI * t / PulsePeriod)))), ScaleStep);

    /// <summary>A slow turn, 90° a second, in 6° steps.</summary>
    public static double Spin(double t) => Quantize(t * SpinDegreesPerSecond % 360, SpinStep);

    /// <summary>A rocking angle of ±<paramref name="degrees"/> over <paramref name="period"/> seconds, whole degrees.</summary>
    public static double Swing(double t, double period, double degrees) =>
        Math.Round(degrees * Math.Sin(2 * Math.PI * t / period));

    /// <summary>Wing width scale, 0.6–1 over 0.5 s.</summary>
    public static double Flap(double t) =>
        Quantize(FlapNarrowest + ((1 - FlapNarrowest) * (0.5 + (0.5 * Math.Cos(2 * Math.PI * t / FlapPeriod)))), ScaleStep);

    /// <summary>Waving arm, 0 to −6 px, odd arms in opposition. Whole pixels (pixel art).</summary>
    public static double Wave(double t, int index) =>
        Math.Round(-(WaveHeight / 2) * (1 - Math.Cos((2 * Math.PI * t / WavePeriod) + (index * Math.PI))));

    /// <summary>A jelly squash with the voice: wider and flatter.</summary>
    public static (double X, double Y) Squish(double level)
    {
        var l = Math.Clamp(level, 0, 1);
        return (Quantize(1 + (SquishWiden * l), ScaleStep / 5), Quantize(1 - (SquishFlatten * l), ScaleStep / 5));
    }

    /// <summary>A puff or note drifting up 14 px and fading, staggered by <paramref name="index"/>.</summary>
    public static (double Y, double Opacity) Rise(double t, int index)
    {
        var p = (t + (index * RiseStagger)) % RisePeriod / RisePeriod;
        return (Quantize(-RiseHeight * p, HalfPixel), Quantize(RiseStart * (1 - p), ScaleStep));
    }
}
