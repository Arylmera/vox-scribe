using System.Runtime.InteropServices;

namespace VoxScribe.Platform.Windows;

/// <summary>
/// Reports every physical key press and release, for recording a push-to-talk chord.
/// </summary>
/// <remarks>
/// <para>
/// The settings UI cannot capture through Avalonia: its key events carry framework key codes,
/// not virtual keys, and cannot tell Right Ctrl from Left Ctrl — the distinction the whole
/// feature exists for. Only the same low-level hook the trigger uses sees what the trigger
/// will see, normalized identically — and it is literally the same hook,
/// <see cref="KeyboardHook"/>; this class is one more listener on it.
/// </para>
/// <para>
/// Constructed with a plain <see cref="Action{T1,T2}"/> so the app layer can create it by
/// reflection without referencing any type from this assembly.
/// </para>
/// </remarks>
public sealed class KeyCaptureHook : IDisposable
{
    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", EntryPoint = "GetKeyNameTextW", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, [Out] char[] text, int size);

    private readonly Action<int, bool> _onKey;
    private bool _listening;

    /// <param name="onKey">
    /// Called with (normalized virtual key, isDown) for every event. Runs on the hook thread —
    /// the subscriber marshals to its own dispatcher.
    /// </param>
    public KeyCaptureHook(Action<int, bool> onKey) => _onKey = onKey;

    /// <summary>Starts reporting keys.</summary>
    public bool Start()
    {
        Dispose();
        _listening = KeyboardHook.Subscribe(_onKey);
        return _listening;
    }

    /// <summary>The layout-local display name of a virtual key ("RIGHT CTRL", "F13"…).</summary>
    public static string NameOf(int virtualKey)
    {
        var scan = MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC);
        if (scan == 0) return $"VK 0x{virtualKey:X2}";

        // GetKeyNameText reads the scan code out of an lParam-shaped value; bit 24 is the
        // extended flag, without which the right-side and navigation keys report their
        // left/numpad namesakes.
        var lParam = (int)(scan << 16);
        if (IsExtended(virtualKey)) lParam |= 1 << 24;

        var name = new char[64];
        var length = GetKeyNameText(lParam, name, name.Length);
        return length > 0
            ? new string(name, 0, length).ToUpperInvariant()
            : $"VK 0x{virtualKey:X2}";
    }

    private static bool IsExtended(int virtualKey) => virtualKey is
        0xA3 or 0xA5 or 0x5B or 0x5C or   // right ctrl/alt, both Windows keys
        0x2D or 0x2E or 0x24 or 0x23 or   // insert, delete, home, end
        0x21 or 0x22 or                   // page up/down
        0x25 or 0x26 or 0x27 or 0x28 or   // arrows
        0x6F;                             // numpad divide

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_listening) return;

        KeyboardHook.Unsubscribe(_onKey);
        _listening = false;
    }
}
