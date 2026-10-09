using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The four Home numbers, from transcript history alone.</summary>
public sealed class HomeStatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(2));

    private static TranscriptRecord Take(DateTimeOffset at, int words, double seconds = 0) => new()
    {
        At = at,
        Text = string.Join(' ', Enumerable.Repeat("word", words)),
        AudioSeconds = seconds,
    };

    [Fact]
    public void No_history_is_all_zeros()
    {
        HomeStats.Compute([], Now).ShouldBe(new HomeStatsResult(0, 0, TimeSpan.Zero, 0));
    }

    [Fact]
    public void Words_count_only_the_rolling_week()
    {
        var stats = HomeStats.Compute(
            [Take(Now.AddHours(-1), 10), Take(Now.AddDays(-6.9), 5), Take(Now.AddDays(-7.1), 100)], Now);

        stats.WordsThisWeek.ShouldBe(15);
    }

    [Fact]
    public void Words_split_on_any_whitespace()
    {
        HomeStats.CountWords("  deux   mots\tet\nquatre ").ShouldBe(4);
        HomeStats.CountWords(string.Empty).ShouldBe(0);
    }

    [Fact]
    public void Pace_is_words_over_spoken_minutes_and_ignores_untimed_takes()
    {
        var stats = HomeStats.Compute(
            [Take(Now.AddHours(-1), 150, seconds: 60), Take(Now.AddHours(-2), 75, seconds: 30), Take(Now.AddHours(-3), 999)],
            Now);

        stats.AverageWpm.ShouldBe(150);
    }

    [Fact]
    public void Time_saved_is_typing_at_40_wpm_minus_speaking_time()
    {
        // 400 words typed at 40 wpm = 10 min; spoken in 1 min; saved 9 min.
        HomeStats.Compute([Take(Now.AddHours(-1), 400, seconds: 60)], Now)
            .TimeSaved.ShouldBe(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public void Time_saved_is_never_negative()
    {
        // 4 words would take 6 s to type but took 60 s to say.
        HomeStats.Compute([Take(Now.AddHours(-1), 4, seconds: 60)], Now).TimeSaved.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Streak_counts_consecutive_days_ending_today()
    {
        var stats = HomeStats.Compute(
            [Take(Now, 1), Take(Now.AddDays(-1), 1), Take(Now.AddDays(-2), 1), Take(Now.AddDays(-4), 1)], Now);

        stats.StreakDays.ShouldBe(3);
    }

    [Fact]
    public void A_streak_survives_until_the_day_is_over()
    {
        HomeStats.Compute([Take(Now.AddDays(-1), 1), Take(Now.AddDays(-2), 1)], Now).StreakDays.ShouldBe(2);
    }

    [Fact]
    public void A_missed_day_breaks_the_streak()
    {
        HomeStats.Compute([Take(Now.AddDays(-2), 1), Take(Now.AddDays(-3), 1)], Now).StreakDays.ShouldBe(0);
    }

    [Fact]
    public void Days_are_calendar_days_in_the_callers_offset()
    {
        // 23:30 UTC on the 7th is 01:30 on the 8th at +02:00 — that is today, not yesterday.
        var lateUtc = new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero);
        HomeStats.Compute([Take(lateUtc, 1)], Now).StreakDays.ShouldBe(1);
    }

    [Fact]
    public void Durations_read_as_minutes_then_hours()
    {
        HomeStats.FormatDuration(TimeSpan.FromMinutes(9)).ShouldBe("9 min");
        HomeStats.FormatDuration(TimeSpan.FromMinutes(72)).ShouldBe("1h 12");
        HomeStats.FormatDuration(TimeSpan.Zero).ShouldBe("0 min");
    }
}
