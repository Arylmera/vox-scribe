using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// Home: a headline and status line, this week's four numbers, the last few dictations, and
/// the chords. Rebuilt on every history or settings change — it is small.
/// </summary>
internal sealed class HomePage : UserControl
{
    private const int RecentCount = 5;
    private const int HeroIndex = 2;
    private const int MorningEnds = 12;
    private const int AfternoonEnds = 18;

    private readonly TranscriptStore? _transcripts;
    private readonly AppSettings? _settings;
    private readonly Action<AppPage> _navigate;
    private readonly Func<string, Task> _retype;

    /// <summary>Builds the page. Null stores give an empty page (headless tests).</summary>
    public HomePage(TranscriptStore? transcripts, AppSettings? settings, Action<AppPage> navigate, Func<string, Task> retype)
    {
        _transcripts = transcripts;
        _settings = settings;
        _navigate = navigate;
        _retype = retype;

        // Content is built on attach (below), not here — a page that is constructed but
        // never shown need not pay for it, and every test attaches before inspecting.
    }

    // Transcripts.Changed fires on the engine's worker thread: always marshal.
    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // A theme switch rebuilds the window from fresh instances and discards this one — a
        // subscription held past that point would keep refreshing a control with no parent.
        if (_transcripts is not null) _transcripts.Changed += OnChanged;
        if (_settings is not null) _settings.Changed += OnChanged;
        Refresh();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_transcripts is not null) _transcripts.Changed -= OnChanged;
        if (_settings is not null) _settings.Changed -= OnChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        var data = _settings?.Data ?? new SettingsData();
        IReadOnlyList<TranscriptRecord> records = _transcripts?.Records ?? [];
        var stats = HomeStats.Compute(records, DateTimeOffset.Now);

        var page = new DockPanel();
        page.Children.Add(Panels.Docked(BuildFooter(data), Dock.Bottom));
        page.Children.Add(Panels.Docked(new StackPanel
        {
            Spacing = Tokens.Space.Wide,
            Children = { BuildHeader(data), BuildStats(stats) },
        }, Dock.Top));
        page.Children.Add(new ScrollViewer { Content = BuildRecent(records) });
        Content = page;
    }

    private Control BuildHeader(SettingsData data)
    {
        var theme = Themes.Active;
        var headline = new TextBlock
        {
            Text = theme.Headline ?? Greeting(DateTime.Now.Hour),
            FontFamily = Tokens.Fonts.Display,
            FontWeight = theme.DisplayWeight,
            FontSize = theme.HeadlineSize,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
        };

        var status = Panels.Row(
            Tokens.Space.Snug,
            new Ellipse
            {
                Width = Tokens.Material.LampBullet,
                Height = Tokens.Material.LampBullet,
                Fill = Tokens.Brushes.Ink,
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock
            {
                Text = StatusLine(data),
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Body,
                Foreground = Tokens.Brushes.InkSecondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });

        var text = new StackPanel { Spacing = Tokens.Space.Base, Children = { headline, status } };
        if (!theme.HeaderCard) return text;

        var card = new DockPanel();
        card.Children.Add(Panels.Docked(Shell.Orb(Tokens.Material.HeroOrbSize, core: true), Dock.Right));
        text.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(text);
        return new Border
        {
            Background = Tokens.Brushes.Panel,
            BorderBrush = Tokens.Brushes.Seam,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            Padding = new Thickness(Tokens.Space.Panel, Tokens.Space.Wide),
            Child = card,
        };
    }

    private static string Greeting(int hour) =>
        hour < MorningEnds ? "Good morning." : hour < AfternoonEnds ? "Good afternoon." : "Good evening.";

    private string StatusLine(SettingsData data)
    {
        var model = data.SttEndpoint is { Length: > 0 }
            ? $"Remote · {data.SttModel}"
            : _settings is null || Composition.IsModelInstalled ? "Parakeet · local" : "No speech model — see Settings";
        var cleanup = data.CleanupEndpoint is { Length: > 0 } ? $"Cleanup via {data.CleanupModel}" : "Cleanup off";
        return Shell.Caps($"Ready · {model} · {cleanup} · Hold {Panels.Chord(data.ResolvedPushToTalkKeys)} to dictate");
    }

    private static Control BuildStats(HomeStatsResult stats)
    {
        var theme = Themes.Active;
        (string Label, string Value, string Unit)[] cells =
        [
            ("Words this week", stats.WordsThisWeek.ToString("N0", CultureInfo.InvariantCulture), string.Empty),
            ("Average pace", stats.AverageWpm.ToString(CultureInfo.InvariantCulture), "wpm"),
            ("Time saved", HomeStats.FormatDuration(stats.TimeSaved), string.Empty),
            ("Streak", stats.StreakDays.ToString(CultureInfo.InvariantCulture), stats.StreakDays == 1 ? "day" : "days"),
        ];

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = theme.Stats == StatStyle.Cards ? Tokens.Space.Base : 0,
        };
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = BuildStat(cells[i].Label, cells[i].Value, cells[i].Unit, hero: i == HeroIndex, first: i == 0);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        if (theme.Stats == StatStyle.Cards) return grid;
        return new Border
        {
            BorderBrush = Tokens.Brushes.Seam,
            BorderThickness = new Thickness(0, Tokens.Border.Hairline, 0, Tokens.Border.Hairline),
            Child = grid,
        };
    }

    private static Border BuildStat(string label, string value, string unit, bool hero, bool first)
    {
        var theme = Themes.Active;
        var fill = hero && theme.HeroStat == HeroStyle.Fill;
        var tint = hero && theme.HeroStat == HeroStyle.Tint;
        var ink = fill ? Tokens.Brushes.OnAccent : hero ? Tokens.Brushes.Accent : Tokens.Brushes.Ink;
        var muted = fill ? Tokens.Brushes.OnAccent : Tokens.Brushes.InkSecondary;

        var number = new TextBlock
        {
            Inlines = new InlineCollection
            {
                new Run(value)
                {
                    FontFamily = theme.StatNumerals,
                    FontWeight = theme.StatWeight,
                    FontSize = theme.StatSize,
                    FontStyle = hero && theme.HeroStat == HeroStyle.AccentItalic ? FontStyle.Italic : FontStyle.Normal,
                    Foreground = ink,
                },
                new Run(unit.Length > 0 ? " " + Shell.Caps(unit) : string.Empty)
                {
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Body,
                    Foreground = muted,
                },
            },
        };

        var cell = new Border
        {
            Padding = new Thickness(Tokens.Space.Roomy),
            Child = new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    new TextBlock
                    {
                        Text = Shell.Caps(label),
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = muted,
                    },
                    number,
                },
            },
        };

        if (theme.Stats == StatStyle.Cards)
        {
            cell.Background = Tokens.Brushes.Panel;
            cell.BorderBrush = Tokens.Brushes.Seam;
            cell.BorderThickness = new Thickness(Tokens.Border.Hairline);
            cell.CornerRadius = new CornerRadius(Tokens.Radius.Panel);
        }
        else if (!first)
        {
            cell.BorderBrush = Tokens.Brushes.Seam;
            cell.BorderThickness = new Thickness(Tokens.Border.Hairline, 0, 0, 0);
        }

        if (fill) cell.Background = Tokens.Brushes.AccentFill;
        if (tint) cell.Background = new SolidColorBrush(Tokens.Colors.AccentTint);
        return cell;
    }

    private StackPanel BuildRecent(IReadOnlyList<TranscriptRecord> records)
    {
        var theme = Themes.Active;
        var title = new TextBlock
        {
            Text = Shell.Caps("Recent"),
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Row,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var seeAll = Panels.LinkButton(theme.Uppercase ? "[ALL HISTORY]" : "See all history →");
        seeAll.Click += (_, _) => _navigate(AppPage.History);

        var list = new StackPanel();
        var recent = records.Take(RecentCount).ToList();
        if (recent.Count == 0)
        {
            list.Children.Add(Panels.EmptyState("NO DICTATIONS YET", "Hold your shortcut anywhere and speak."));
        }

        for (var i = 0; i < recent.Count; i++) list.Children.Add(BuildRow(recent[i], i));

        Control body = theme.List == ListStyle.Card
            ? new Border
            {
                Background = Tokens.Brushes.Panel,
                BorderBrush = Tokens.Brushes.Seam,
                BorderThickness = new Thickness(Tokens.Border.Hairline),
                CornerRadius = new CornerRadius(Tokens.Radius.Panel),
                Padding = new Thickness(Tokens.Space.Snug),
                Child = list,
            }
            : list;

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Margin = new Thickness(0, Tokens.Space.Wide, 0, Tokens.Space.Base),
            Children = { Panels.SplitRow(title, seeAll), body },
        };
    }

    private Control BuildRow(TranscriptRecord record, int index)
    {
        var theme = Themes.Active;
        var time = new TextBlock
        {
            Text = record.At.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.Label,
            Foreground = Tokens.Brushes.InkSecondary,
            Width = Tokens.Material.RowTimeWidth,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = record.Text,
            FontFamily = Tokens.Fonts.Prose,
            FontSize = Tokens.Fonts.Row,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Tokens.Space.Base, 0),
        };

        var copy = Panels.IconButton(Shell.CopyIcon, "Copy");
        copy.Click += async (_, _) =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(record.Text).ConfigureAwait(true);
                }
            }
            catch (Exception)
            {
                // Copying is a convenience; clipboard contention from another app must
                // never take the app down over an async void handler.
            }
        };
        var again = Panels.IconButton(Shell.RetypeIcon, "Type again");
        again.Click += async (_, _) =>
        {
            try
            {
                await _retype(record.Text).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // "Type again" is a convenience; a clash with a fresh dictation must never
                // take the app down over an async void handler.
            }
        };

        var badge = Badge(cleaned: record.RawText is not null);
        badge.Margin = new Thickness(0, 0, Tokens.Space.Snug, 0);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            Height = Tokens.Material.RowHeight,
        };
        Control[] cells = [time, text, badge, copy, again];
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        var row = new Border { Padding = new Thickness(Tokens.Space.Snug, 0), Child = grid };
        switch (theme.List)
        {
            case ListStyle.Hairline:
                row.BorderBrush = Tokens.Brushes.Seam;
                row.BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Hairline);
                break;
            case ListStyle.Zebra:
                if (index % 2 == 0) row.Background = Tokens.Brushes.Panel;
                row.CornerRadius = new CornerRadius(Tokens.Radius.Chip);
                break;
            case ListStyle.Card:
                if (index > 0)
                {
                    row.BorderBrush = Tokens.Brushes.Seam;
                    row.BorderThickness = new Thickness(0, Tokens.Border.Hairline, 0, 0);
                }

                break;
            case ListStyle.Dashed:
                return new StackPanel { Children = { new DashedRule(), row } };
        }

        return row;
    }

    /// <summary>RAW or CLEAN. History does not record command mode, so CMD is never shown here.</summary>
    private static Border Badge(bool cleaned) => new()
    {
        CornerRadius = new CornerRadius(Tokens.Radius.Chip),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        BorderBrush = cleaned ? Tokens.Brushes.Accent : Tokens.Brushes.Seam,
        Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = cleaned ? "CLEAN" : "RAW",
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.Caption,
            LetterSpacing = Tokens.Fonts.SilkscreenTracking,
            Foreground = cleaned ? Tokens.Brushes.Accent : Tokens.Brushes.InkSecondary,
        },
    };

    private static WrapPanel BuildFooter(SettingsData data)
    {
        var footer = new WrapPanel
        {
            ItemSpacing = Tokens.Space.Wide,
            LineSpacing = Tokens.Space.Tight,
            Margin = new Thickness(0, Tokens.Space.Base, 0, 0),
        };

        (string Label, int[]? Keys)[] chords =
        [
            ("Raw", data.ResolvedPushToTalkKeys),
            ("Cleanup", data.CleanupPushToTalkKeys),
            ("Undo", data.UndoKeys),
            ("Command", data.CommandKeys),
        ];
        foreach (var (label, keys) in chords)
        {
            footer.Children.Add(new TextBlock
            {
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Label,
                Inlines = new InlineCollection
                {
                    new Run(Shell.Caps(label) + " ") { Foreground = Tokens.Brushes.InkSecondary },
                    new Run(Shell.Caps(Panels.Chord(keys))) { Foreground = Tokens.Brushes.Ink, FontWeight = FontWeight.Medium },
                },
            });
        }

        return footer;
    }
}
