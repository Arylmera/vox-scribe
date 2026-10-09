using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>Theme, variant and light/dark changes repaint without a restart.</summary>
public sealed class LiveThemeTests
{
    [AvaloniaFact]
    public void A_theme_change_rebuilds_the_main_window()
    {
        var window = new MainWindow();
        window.Show();
        var before = window.Content;
        try
        {
            Themes.Apply("mono", null, dark: true);
            Dispatcher.UIThread.RunJobs();

            window.Content.ShouldNotBeSameAs(before);
            window.Background.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.FromRgb(0x0B, 0x0B, 0x0B));
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
            Dispatcher.UIThread.RunJobs();
            window.ExitAllowed = true;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Applying_a_theme_repaints_the_fluent_system_accent_resources()
    {
        try
        {
            Themes.Apply("paper", "plum", dark: false);
            var plum = (Color)Avalonia.Application.Current!.Resources["SystemAccentColor"]!;
            plum.ShouldBe(Tokens.Colors.Accent);

            Themes.Apply("paper", "moss", dark: false);
            var moss = (Color)Avalonia.Application.Current!.Resources["SystemAccentColor"]!;
            moss.ShouldBe(Tokens.Colors.Accent);

            moss.ShouldNotBe(plum, "a Windows accent colour must not leak through Fluent controls between themes");
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    [AvaloniaFact]
    public void Shared_controls_take_the_theme_painted_when_they_are_built()
    {
        try
        {
            // Force each control's static type initializer under a known baseline theme
            // first — otherwise a regression to the old eager-default behaviour (the colour
            // or radius baked in once, at whichever theme happened to be active when the
            // type was first touched) could hide behind coincidence, depending on test order.
            Themes.Apply(Themes.DefaultId, null, dark: false);
            var paperLamp = new Lamp().LampColor;
            var paperKey = new TransportKey().EngagedColor;
            var paperPanel = new BrushedPanel().CornerRadius;

            // Tide's card radius (20) is neither 0 — the control's static registration
            // default — nor Paper's (12), so a stale default cannot pass as either.
            Themes.Apply("tide", null, dark: true);

            new Lamp().LampColor.ShouldBe(Tokens.Colors.Silkscreen);
            new Lamp().LampColor.ShouldNotBe(paperLamp);

            new TransportKey().EngagedColor.ShouldBe(Tokens.Colors.Ink);
            new TransportKey().EngagedColor.ShouldNotBe(paperKey);

            var panel = new BrushedPanel().CornerRadius;
            panel.ShouldBe(Tokens.Radius.Panel);
            panel.ShouldNotBe(0);
            panel.ShouldNotBe(paperPanel);
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }
}

/// <summary>
/// A theme switch rebuilds the main window from fresh section views and discards the old ones
/// (<see cref="MainWindow"/>'s <c>Rebuild</c>). A discarded view must not keep listening to the
/// store it no longer shows — otherwise every theme switch leaks a subscription that still
/// calls <c>Refresh</c> on an orphaned control tree.
/// </summary>
public sealed class ViewLifecycleTests
{
    [AvaloniaFact]
    public void A_detached_transcriptions_view_stops_refreshing_on_store_changes()
    {
        var store = new TranscriptStore(Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.jsonl"));
        var view = new TranscriptionsView(store);
        var window = new Window { Content = view };
        try
        {
            window.Show();

            var counter = view.GetVisualDescendants().OfType<Silkscreen>()
                .First(s => s.Text is { } text && text.Contains("RECORDING", StringComparison.Ordinal));
            counter.Text.ShouldBe("0 RECORDINGS");

            // Stands in for what Rebuild does to the old section view: detach it and move on.
            window.Content = null;

            store.Add(new TranscriptRecord { Text = "hello" });
            Dispatcher.UIThread.RunJobs();

            counter.Text.ShouldBe(
                "0 RECORDINGS", "a detached view must not still be refreshing from the store it no longer owns");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_reattached_transcriptions_view_catches_up_on_what_changed_while_it_was_away()
    {
        // History → Home → dictate → History: the view is detached, a record is added while
        // it has no subscription, then it comes back on screen. OnAttachedToVisualTree must
        // refresh immediately rather than wait for the next Changed event it missed.
        var store = new TranscriptStore(Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.jsonl"));
        var view = new TranscriptionsView(store);
        var window = new Window { Content = view };
        try
        {
            window.Show();

            var counter = view.GetVisualDescendants().OfType<Silkscreen>()
                .First(s => s.Text is { } text && text.Contains("RECORDING", StringComparison.Ordinal));
            counter.Text.ShouldBe("0 RECORDINGS");

            window.Content = null;
            store.Add(new TranscriptRecord { Text = "hello" });
            Dispatcher.UIThread.RunJobs();

            window.Content = view;
            Dispatcher.UIThread.RunJobs();

            counter.Text.ShouldBe(
                "1 RECORDING", "re-attaching must refresh from the store, not keep showing a stale count");
        }
        finally
        {
            window.Close();
        }
    }
}
