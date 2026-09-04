using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Settings;

/// <summary>
/// The TEST CONNECTION row: button, lamp and verdict, reading its endpoint fresh on each
/// click so both the transcription and cleanup sections share one implementation.
/// </summary>
internal static class ConnectionTester
{
    /// <summary>Builds the row; <paramref name="read"/> supplies the current settings.</summary>
    public static StackPanel Build(Func<(string? Endpoint, string Model, string? Key)> read)
    {
        var lamp = new Lamp { VerticalAlignment = VerticalAlignment.Center };
        var status = Panels.Note("Not tested yet.");
        status.VerticalAlignment = VerticalAlignment.Center;

        var test = new TransportKey { Content = "TEST CONNECTION", EngagedColor = Tokens.Colors.Ink };
        test.Click += async (_, _) =>
        {
            // Clicking steals focus from the field being edited, so its LostFocus save has
            // already run — the settings are current by the time we read them here.
            test.IsEnabled = false;
            lamp.IsLit = false;
            status.Text = "Testing…";
            var (endpoint, model, key) = read();
            var (ok, message) = await TestConnectionAsync(endpoint, model, key);
            lamp.IsLit = true;
            lamp.LampColor = ok ? Tokens.Colors.MeterGreen : Tokens.Colors.MeterRed;
            status.Text = message;
            test.IsEnabled = true;
        };

        var save = new TransportKey { Content = "SAVE", EngagedColor = Tokens.Colors.Ink };
        save.Click += async (_, _) =>
        {
            // Force LostFocus on any active field to trigger its onCommit handler
            save.Focus();

            // Feedback: brief status, then clear after 2s
            status.Text = "Saved.";
            status.Foreground = new SolidColorBrush(Tokens.Colors.MeterGreen);

            await Task.Delay((int)Tokens.Motion.Feedback.TotalMilliseconds * 4);
            status.Text = "Not tested yet.";
            status.Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary);
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { test, save, lamp, status },
        };
    }

    /// <summary>
    /// Probes the gateway's <c>/models</c> listing — reachability and auth in one round
    /// trip, plus a check that the configured model is actually routed there.
    /// </summary>
    private static async Task<(bool Ok, string Message)> TestConnectionAsync(
        string? endpoint, string model, string? apiKey)
    {
        if (string.IsNullOrEmpty(endpoint))
            return (false, "No endpoint configured — transcription is local.");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var started = Stopwatch.GetTimestamp();
            using var response = await http.GetAsync(endpoint.TrimEnd('/') + "/models");
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return (false, "Reached the server, but the API key was rejected.");
            if (!response.IsSuccessStatusCode)
                return (false, $"Server answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var listed = json.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.EnumerateArray().Any(m =>
                    m.TryGetProperty("id", out var id) && id.GetString() == model);

            return listed
                ? (true, $"Connected — model “{model}” available ({elapsed.TotalMilliseconds:F0} ms).")
                : (true, $"Connected ({elapsed.TotalMilliseconds:F0} ms), but “{model}” is not "
                       + "in the server's model list — check the model name.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or JsonException)
        {
            return (false, e is TaskCanceledException
                ? "No answer within 8 s — server unreachable?"
                : $"Connection failed: {e.Message}");
        }
    }
}
