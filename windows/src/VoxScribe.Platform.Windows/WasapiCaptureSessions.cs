using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoxScribe.Abstractions;

namespace VoxScribe.Platform.Windows;

/// <summary>
/// Other apps' microphone streams through the WASAPI session manager on each capture endpoint.
/// </summary>
/// <remarks>
/// Session mute (<c>ISimpleAudioVolume</c>) silences one app's stream and leaves the device —
/// and our own capture — running, unlike endpoint mute. Logic-free on purpose: which streams to
/// mute lives in <c>MicrophoneMuter</c>.
/// </remarks>
public sealed class WasapiCaptureSessions : ICaptureSessions
{
    /// <inheritdoc />
    public IReadOnlyList<CaptureSession> List()
    {
        var found = new List<CaptureSession>();
        Each(session => found.Add(new CaptureSession(
            (int)session.GetProcessID, NameOf((int)session.GetProcessID), session.SimpleAudioVolume.Mute)));
        return found;
    }

    /// <inheritdoc />
    public void SetMuted(int processId, bool muted) => Each(session =>
    {
        if ((int)session.GetProcessID == processId) session.SimpleAudioVolume.Mute = muted;
    });

    /// <summary>Every non-expired, non-system session on every active microphone.</summary>
    private static void Each(Action<AudioSessionControl> visit)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.IsSystemSoundsSession) continue;
                    if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                    visit(session);
                }
            }
        }
    }

    private static string NameOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;   // exited since the session was listed
        }
    }
}
