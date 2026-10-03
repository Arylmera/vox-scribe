namespace VoxScribe.App.ViewModels;

/// <summary>
/// View model for MainWindow — encapsulates state and commands.
/// Extracted to enable view bindings and independent testing.
/// </summary>
public sealed class MainWindowViewModel
{
    /// <summary>Whether recording is active.</summary>
    public bool IsRecording { get; set; }

    /// <summary>Elapsed time display.</summary>
    public string ElapsedTime { get; set; } = "00:00";

    /// <summary>Volume meter value (0–1).</summary>
    public double Volume { get; set; }

    /// <summary>Current section (true = transcriptions, false = settings/dict).</summary>
    public bool ShowTranscriptions { get; set; } = true;
}
