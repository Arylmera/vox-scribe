using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// Past transcriptions: searchable, each copyable and deletable.
/// </summary>
/// <remarks>
/// Rows show which dictionary corrections fired. Without that the dictionary is invisible and
/// there is no way to tell a rule that works from one that never matches.
/// </remarks>
public sealed class TranscriptionsView : UserControl
{
    private readonly TranscriptStore _store;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly Silkscreen _count;

    /// <summary>Builds the view over <paramref name="store"/>.</summary>
    public TranscriptionsView(TranscriptStore store)
    {
        _store = store;

        _search = Panels.SearchBox("Search transcriptions");
        _search.TextChanged += (_, _) => Refresh();

        _list = Panels.ListBody();
        _count = Panels.Counter();

        var clear = Panels.DeckButton("DELETE ALL");
        clear.Click += (_, _) => { _store.Clear(); Refresh(); };

        Content = Panels.ListShell(
            Panels.SearchRow(_search), Panels.Footer(_count, clear), _list);

        // Changed fires from the dictation engine's worker thread (its pipeline runs
        // ConfigureAwait(false) throughout); touching Avalonia controls there throws and the
        // view silently stops refreshing. Always marshal to the UI thread.
        _store.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var records = _store.Search(_search.Text ?? string.Empty);

        _list.Children.Clear();
        _count.Text = $"{_store.Records.Count} RECORDING{(_store.Records.Count == 1 ? "" : "S")}";

        if (records.Count == 0)
        {
            _list.Children.Add(Panels.EmptyState(
                _store.Records.Count == 0 ? "NO RECORDINGS" : "NO MATCHES",
                _store.Records.Count == 0 ? "Hold the push-to-talk key and speak." : "Try a different search."));
            return;
        }

        // The journal groups by day; the tape log numbers takes from the oldest up.
        DateTime? day = null;
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];

            if (Themes.Transcripts == TranscriptStyle.Journal)
            {
                var date = record.At.ToLocalTime().Date;
                if (date != day)
                {
                    day = date;
                    _list.Children.Add(new TextBlock
                    {
                        Text = date.ToString("dddd d MMMM", CultureInfo.CurrentCulture),
                        FontFamily = Tokens.Fonts.Prose,
                        FontStyle = FontStyle.Italic,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
                        Margin = new Thickness(0, Tokens.Space.Base, 0, Tokens.Space.Tight),
                    });
                }
            }

            _list.Children.Add(BuildRow(record, TakeNumber(record)));
        }
    }

    /// <summary>The record's position from the oldest, so take numbers survive filtering.</summary>
    private int TakeNumber(TranscriptRecord record)
    {
        var all = _store.Records;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Id == record.Id) return all.Count - i;
        }

        return all.Count;
    }

    private Border BuildRow(TranscriptRecord record, int take)
    {
        var copy = Panels.DeckButton("COPY");
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(record.Text).ConfigureAwait(true);

            copy.Content = "COPIED";
            await Task.Delay(Tokens.Motion.CopyHold).ConfigureAwait(true);
            copy.Content = "COPY";
        };

        var delete = Panels.DeckButton("DELETE");
        delete.Click += (_, _) => _store.Remove(record.Id);
        var actions = Panels.Row(Tokens.Space.Tight, copy, delete);

        var text = new TextBlock
        {
            Text = record.Text,
            FontFamily = Tokens.Fonts.Prose,
            FontSize = Tokens.Fonts.Body,
            Foreground = Tokens.Brushes.InkOnDeck,
            TextWrapping = TextWrapping.Wrap,
        };

        return Themes.Transcripts switch
        {
            TranscriptStyle.TakeLog => BuildTakeRow(record, take, text, actions),
            TranscriptStyle.Journal => BuildJournalRow(record, text, actions),
            _ => BuildBareRow(record, text, actions),
        };
    }

    /// <summary>A numbered take on the tape log, Signal House style.</summary>
    private static Border BuildTakeRow(
        TranscriptRecord record, int take, TextBlock text, Control actions)
    {
        var header = Panels.Row(
            Tokens.Space.Snug,
            new TextBlock
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"TAKE {take:000}"),
                FontFamily = Tokens.Fonts.Mono,
                FontSize = Tokens.Fonts.Caption,
                Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
                VerticalAlignment = VerticalAlignment.Center,
            },
            MetaBlock(record, Tokens.Emphasis.Ghost));

        var body = new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children = { Panels.SplitRow(header, actions), text },
        };
        AddCorrections(body, record);
        return Panels.DeckCard(body);
    }

    /// <summary>A journal entry: the time in the margin, the words on the page.</summary>
    private static Border BuildJournalRow(TranscriptRecord record, TextBlock text, Control actions)
    {
        var margin = new StackPanel
        {
            Spacing = Tokens.Space.Hair,
            Children =
            {
                new TextBlock
                {
                    Text = record.At.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
                    FontFamily = Tokens.Fonts.Mono,
                    FontSize = Tokens.Fonts.Caption,
                    Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Soft),
                },
                new TextBlock
                {
                    Text = record.ProcessingSeconds.ToString("0.0", CultureInfo.CurrentCulture) + "s",
                    FontFamily = Tokens.Fonts.Mono,
                    FontSize = Tokens.Fonts.Caption,
                    Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Ghost),
                },
            },
        };

        var body = new StackPanel { Spacing = Tokens.Space.Snug, Children = { text } };
        AddCorrections(body, record);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        margin.Width = Tokens.Material.RailWidth;
        Grid.SetColumn(margin, 0);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(actions, 2);
        actions.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(margin);
        grid.Children.Add(body);
        grid.Children.Add(actions);

        return HairlineRow(grid);
    }

    /// <summary>A bare hairline row, Deep Field style: words left, meta right.</summary>
    private static Border BuildBareRow(TranscriptRecord record, TextBlock text, Control actions)
    {
        var body = new StackPanel { Spacing = Tokens.Space.Snug, Children = { text } };
        AddCorrections(body, record);

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { MetaBlock(record, Tokens.Emphasis.Soft), actions },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(body, 0);
        Grid.SetColumn(meta, 1);
        grid.Children.Add(body);
        grid.Children.Add(meta);

        return HairlineRow(grid);
    }

    /// <summary>Time and felt latency, on one mono line.</summary>
    private static TextBlock MetaBlock(TranscriptRecord record, double emphasis) => new()
    {
        Text = record.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)
             + " · " + record.ProcessingSeconds.ToString("0.0", CultureInfo.CurrentCulture) + "s",
        FontFamily = Tokens.Fonts.Mono,
        FontSize = Tokens.Fonts.Caption,
        Foreground = Tokens.Brushes.InkOnDeckAt(emphasis),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Wraps a row in the hairline framing the card-less styles use.</summary>
    private static Border HairlineRow(Control content) => new()
    {
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Hairline),
        Padding = new Thickness(0, Tokens.Space.Snug, 0, Tokens.Space.Base),
        Child = content,
    };

    private static void AddCorrections(StackPanel body, TranscriptRecord record)
    {
        if (record.Corrections is { Count: > 0 } corrections)
        {
            body.Children.Add(BuildCorrectionBadges(corrections));
        }
    }

    /// <summary>Shows that the dictionary fired, and on what.</summary>
    private static WrapPanel BuildCorrectionBadges(IReadOnlyList<Dictionary.AppliedCorrection> corrections)
    {
        var row = new WrapPanel { ItemSpacing = Tokens.Space.Snug, LineSpacing = Tokens.Space.Tight };

        row.Children.Add(new Silkscreen
        {
            Text = "CORRECTED",
            Foreground = new SolidColorBrush(Tokens.Colors.MeterAmber),
            VerticalAlignment = VerticalAlignment.Center,
        });

        foreach (var correction in corrections)
        {
            var label = correction.Count > 1
                ? $"{correction.From} → {correction.To} ×{correction.Count}"
                : $"{correction.From} → {correction.To}";

            row.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(
                    Tokens.Colors.MeterAmber, Tokens.Material.NoticeEdgeOpacity),
                BorderThickness = new Thickness(Tokens.Border.Hairline),
                CornerRadius = new CornerRadius(Tokens.Radius.Chip),
                Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair),
                Child = new TextBlock
                {
                    Text = label,
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Caption,
                    Foreground = Tokens.Brushes.InkOnDeck,
                },
            });
        }

        return row;
    }
}
