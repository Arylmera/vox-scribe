using NAudio.Wave;
using VoxScribe.Abstractions;

namespace VoxScribe.Platform.Windows;

/// <summary>Plays a WAV clip on the default output device through WinMM.</summary>
/// <remarks>Logic-free on purpose: what to say and when lives in <c>ReadAloud</c>.</remarks>
public sealed class WaveOutPlayer : IAudioPlayer
{
    /// <inheritdoc />
    public async Task PlayAsync(byte[] wav, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        using var reader = new WaveFileReader(new MemoryStream(wav));
        using var output = new WaveOutEvent();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => ended.TrySetResult();
        output.Init(reader);

        // Registered after Play, so a cancel that raced in between still stops it. Stop raises
        // PlaybackStopped, which ends the wait below.
        output.Play();
        using var stop = cancellationToken.Register(output.Stop);
        await ended.Task.ConfigureAwait(false);
    }
}
