using System.Globalization;

namespace VoxScribe.Core;

/// <summary>The four numbers on the Home page.</summary>
/// <param name="WordsThisWeek">Words dictated in the rolling last 7 days.</param>
/// <param name="AverageWpm">Speaking pace over the week's timed dictations.</param>
/// <param name="TimeSaved">Typing time at 40 wpm minus speaking time, never negative.</param>
/// <param name="StreakDays">Consecutive days with a dictation, ending today or yesterday.</param>
public sealed record HomeStatsResult(int WordsThisWeek, int AverageWpm, TimeSpan TimeSaved, int StreakDays);

/// <summary>Computes <see cref="HomeStatsResult"/> from transcript history. Pure; the clock is passed in.</summary>
public static class HomeStats
{
    /// <summary>The typing speed dictation is compared against.</summary>
    public const double TypingWordsPerMinute = 40;

    /// <summary>The rolling window "this week" covers.</summary>
    public static TimeSpan Week { get; } = TimeSpan.FromDays(7);

    /// <summary>Computes the stats as of <paramref name="now"/>; days are counted in its offset.</summary>
    public static HomeStatsResult Compute(IEnumerable<TranscriptRecord> records, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        var since = now - Week;
        var words = 0;
        var timedWords = 0;
        var seconds = 0.0;
        var days = new HashSet<DateTime>();

        foreach (var record in records)
        {
            days.Add(record.At.ToOffset(now.Offset).Date);
            if (record.At <= since || record.At > now) continue;

            var count = CountWords(record.Text);
            words += count;
            if (record.AudioSeconds > 0)
            {
                timedWords += count;
                seconds += record.AudioSeconds;
            }
        }

        var wpm = seconds > 0
            ? (int)Math.Round(timedWords / (seconds / 60), MidpointRounding.AwayFromZero)
            : 0;
        var saved = TimeSpan.FromMinutes(Math.Max(0, (words / TypingWordsPerMinute) - (seconds / 60)));

        return new HomeStatsResult(words, wpm, saved, Streak(days, now.Date));
    }

    /// <summary>Whitespace-separated words.</summary>
    public static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>"9 min" under an hour, "1h 12" from an hour up.</summary>
    public static string FormatDuration(TimeSpan span) => span.TotalMinutes < 60
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes} min")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:00}");

    private static int Streak(HashSet<DateTime> days, DateTime today)
    {
        var day = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }
}
