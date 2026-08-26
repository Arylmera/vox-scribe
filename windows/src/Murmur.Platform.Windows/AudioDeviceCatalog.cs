using NAudio.CoreAudioApi;

namespace Murmur.Platform.Windows;

/// <summary>Enumerates capture devices for the settings UI.</summary>
/// <remarks>
/// Lives here rather than in <c>Murmur.App</c> because only this assembly references NAudio.
/// The return type is a BCL pair array on purpose: the app calls this by reflection (see
/// <c>PlatformFactory</c>) and must not need any NAudio type to read the result.
/// </remarks>
public static class AudioDeviceCatalog
{
    /// <summary>Active capture endpoints as (ID, friendly name) pairs.</summary>
    public static KeyValuePair<string, string>[] ListCapture()
    {
        using var enumerator = new MMDeviceEnumerator();
        return
        [
            .. enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => new KeyValuePair<string, string>(d.ID, d.FriendlyName)),
        ];
    }
}
