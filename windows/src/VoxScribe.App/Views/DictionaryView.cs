using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;
using VoxScribe.Dictionary;

namespace VoxScribe.App.Views;

/// <summary>
/// The dictionary: add, edit, delete, search.
/// </summary>
/// <remarks>
/// Both entry kinds live in one list rather than separate tabs — they are two shapes of the
/// same idea, and you want to see everything you have taught it at once. The kind is carried
/// by a silkscreen tag on each row.
/// </remarks>
public sealed class DictionaryView : UserControl
{
    private readonly DictionaryFile _file;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly Silkscreen _count;

    /// <summary>Builds the view over <paramref name="file"/>.</summary>
    public DictionaryView(DictionaryFile file)
    {
        _file = file;

        _search = Panels.SearchBox("Search dictionary");
        _search.TextChanged += (_, _) => Refresh();

        var add = Panels.DeckButton("ADD");
        add.Click += (_, _) => ShowEditor(null);

        _list = Panels.ListBody();
        _count = Panels.Counter();

        var reveal = Panels.DeckButton("OPEN DICTIONARY.TXT");
        reveal.Click += (_, _) => OpenInEditor(_file.FilePath);

        Content = Panels.ListShell(
            Panels.SearchRow(_search, add), Panels.Footer(_count, reveal), _list);

        _file.Changed += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var entries = _file.Search(_search.Text ?? string.Empty);

        _list.Children.Clear();
        _count.Text = $"{_file.Entries.Count} ENTRIES";

        if (entries.Count == 0)
        {
            _list.Children.Add(Panels.EmptyState(
                _file.Entries.Count == 0 ? "DICTIONARY EMPTY" : "NO MATCHES",
                _file.Entries.Count == 0
                    ? "Add words it keeps getting wrong."
                    : "Try a different search."));
            return;
        }

        foreach (var entry in entries) _list.Children.Add(BuildRow(entry));
    }

    private Border BuildRow(DictionaryEntry entry)
    {
        var edit = Panels.DeckButton("EDIT");
        edit.Click += (_, _) => ShowEditor(entry);

        var toggle = Panels.DeckButton(entry.IsEnabled ? "OFF" : "ON");
        toggle.Click += (_, _) => _file.Update(entry with { IsEnabled = !entry.IsEnabled });

        var delete = Panels.DeckButton("DELETE");
        delete.Click += (_, _) => _file.Remove(entry.Id);

        var leading = Panels.Row(
            Tokens.Space.Base,
            new Lamp
            {
                IsLit = entry.IsEnabled,
                LampColor = Tokens.Colors.MeterGreen,
                Width = Tokens.Material.LampBullet,
                Height = Tokens.Material.LampBullet,
                VerticalAlignment = VerticalAlignment.Center,
            },
            new Silkscreen
            {
                Text = entry.Kind == EntryKind.Correction ? "FIX" : "TERM",
                Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Soft),
                Width = Tokens.Material.EntryTagWidth,
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock
            {
                Text = entry.Kind == EntryKind.Correction
                    ? $"{entry.Hear}  →  {entry.Write}"
                    : entry.Write,
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Body,
                Foreground = Tokens.Brushes.InkOnDeck,
                VerticalAlignment = VerticalAlignment.Center,
            });

        return new Border
        {
            Background = Tokens.Brushes.Deck,
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            Opacity = entry.IsEnabled ? 1 : Tokens.Emphasis.Disabled,
            Child = Panels.SplitRow(
                leading, Panels.Row(Tokens.Space.Tight, edit, toggle, delete)),
        };
    }

    private void ShowEditor(DictionaryEntry? entry)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var editor = new DictionaryEditorWindow(entry);
        editor.Saved += (_, saved) =>
        {
            if (entry is null) _file.Add(saved); else _file.Update(saved);
        };

        _ = editor.ShowDialog(owner);
    }

    /// <summary>Opens the dictionary in the user's default text editor.</summary>
    private static void OpenInEditor(string path)
    {
        try
        {
            // The file must exist before the shell will open it — a brand-new install has
            // never saved one.
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
            }

            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            // Opening an editor is a convenience; failing to must never take the app down.
        }
    }
}

/// <summary>
/// Add or edit one dictionary entry, with the false-positive warning shown live.
/// </summary>
/// <remarks>
/// A term has one field, a correction has two, and the label above the second one changes
/// with the kind. Both are built once and shown or hidden, rather than rebuilt on every
/// switch: rebuilding would throw away whatever the user had already typed.
/// </remarks>
public sealed class DictionaryEditorWindow : Window
{
    private readonly TransportKey _termKey;
    private readonly TransportKey _correctionKey;
    private readonly TextBox _hear;
    private readonly TextBox _write;
    private readonly StackPanel _hearField;
    private readonly Silkscreen _writeLabel;
    private readonly StackPanel _warnings;
    private readonly TransportKey _save;
    private readonly Guid _id;
    private readonly bool _wasEnabled;

    private EntryKind _kind;

    /// <summary>Raised when the user saves.</summary>
    public event EventHandler<DictionaryEntry>? Saved;

    /// <summary>Creates the editor for a new or existing entry.</summary>
    public DictionaryEditorWindow(DictionaryEntry? entry)
    {
        _id = entry?.Id ?? Guid.NewGuid();
        _wasEnabled = entry?.IsEnabled ?? true;
        _kind = entry?.Kind ?? EntryKind.Term;

        Title = entry is null ? "New entry" : "Edit entry";
        Width = Tokens.Size.EditorWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = Tokens.Brushes.Chassis;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _termKey = new TransportKey { Content = "TERM" };
        _correctionKey = new TransportKey { Content = "CORRECTION" };
        _termKey.Click += (_, _) => SetKind(EntryKind.Term);
        _correctionKey.Click += (_, _) => SetKind(EntryKind.Correction);

        _hear = DeckField("cloud code", entry?.Hear ?? string.Empty);
        _write = DeckField("Claude Code", entry?.Write ?? string.Empty);
        _hear.TextChanged += (_, _) => Revalidate();
        _write.TextChanged += (_, _) => Revalidate();

        _hearField = Panels.Labelled("WHEN YOU HEAR", _hear);
        _writeLabel = new Silkscreen { Text = "WORD OR PHRASE" };
        _warnings = new StackPanel { Spacing = Tokens.Space.Snug };

        var cancel = new TransportKey { Content = "CANCEL" };
        cancel.Click += (_, _) => Close();

        _save = new TransportKey { Content = "SAVE" };
        _save.Click += (_, _) =>
        {
            if (!IsValid) return;
            Saved?.Invoke(this, Draft);
            Close();
        };

        Content = BuildContent(cancel);

        // Last: it is what puts the fields, the labels and the SAVE key into agreement with
        // _kind, and every one of them has to exist by the time it runs.
        SetKind(_kind);
    }

    private DictionaryEntry Draft => new()
    {
        Id = _id,
        Kind = _kind,
        Write = (_write.Text ?? string.Empty).Trim(),
        Hear = _kind == EntryKind.Correction ? (_hear.Text ?? string.Empty).Trim() : string.Empty,
        IsEnabled = _wasEnabled,
    };

    private bool IsValid =>
        Draft.Write.Length > 0 && (_kind == EntryKind.Term || Draft.Hear.Length > 0);

    private StackPanel BuildContent(Control cancel) => new()
    {
        Margin = new Thickness(Tokens.Space.Panel),
        Spacing = Tokens.Space.Roomy,
        Children =
        {
            Panels.Row(Tokens.Space.Snug, _termKey, _correctionKey),
            _hearField,
            new StackPanel { Spacing = Tokens.Space.Tight, Children = { _writeLabel, _write } },
            _warnings,
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Tokens.Space.Snug,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, _save },
            },
        },
    };

    private void SetKind(EntryKind kind)
    {
        _kind = kind;
        _termKey.IsEngaged = kind == EntryKind.Term;
        _correctionKey.IsEngaged = kind == EntryKind.Correction;

        _hearField.IsVisible = kind == EntryKind.Correction;
        _writeLabel.Text = kind == EntryKind.Correction ? "WRITE" : "WORD OR PHRASE";

        Revalidate();
    }

    private void Revalidate()
    {
        _warnings.Children.Clear();

        foreach (var warning in DictionaryWarning.Check(Draft))
        {
            _warnings.Children.Add(BuildWarning(warning.Message));
        }

        _save.IsEngaged = IsValid;
        _save.IsEnabled = IsValid;
    }

    /// <summary>An amber-outlined note: this rule will probably fire when you don't want it.</summary>
    private static Border BuildWarning(string message) => new()
    {
        BorderBrush = new SolidColorBrush(
            Tokens.Colors.MeterAmber, Tokens.Material.NoticeEdgeOpacity),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Chip),
        Padding = new Thickness(Tokens.Space.Snug),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children =
            {
                new Lamp
                {
                    IsLit = true,
                    LampColor = Tokens.Colors.MeterAmber,
                    Width = Tokens.Material.LampBullet,
                    Height = Tokens.Material.LampBullet,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, Tokens.Space.Tight, 0, 0),
                },
                new TextBlock
                {
                    Text = message,
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Label,
                    Foreground = Tokens.Brushes.Ink,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = Tokens.Material.WarningMaxWidth,
                },
            },
        },
    };

    /// <summary>A text field styled for the dark deck, the twin of <see cref="Panels.SearchBox"/>.</summary>
    private static TextBox DeckField(string placeholder, string text) => new()
    {
        Text = text,
        Watermark = placeholder,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        Foreground = Tokens.Brushes.InkOnDeck,
        Background = Tokens.Brushes.Deck,
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Chip),
        Padding = new Thickness(Tokens.Space.Snug),
    };
}
