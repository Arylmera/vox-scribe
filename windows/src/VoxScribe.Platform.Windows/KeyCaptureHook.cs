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
/// will see, normalized identically (<see cref="PushToTalkHook.Normalize(int,int,bool)"/>).
/// </para>
/// <para>
/// Same structure as <see cref="PushToTalkHook"/> for the same reasons: dedicated pumping
/// thread, rooted callback, always chains. Never swallows — the user is typing into a live
/// system while recording. Constructed with a plain <see cref="Action{T1,T2}"/> so the app
/// layer can create it by reflection without referencing any type from this assembly.
/// </para>
/// </remarks>
public sealed class KeyCaptureHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    private const uint LLKHF_EXTENDED = 0x01;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", EntryPoint = "GetKeyNameTextW", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, [Out] char[] text, int size);

    private static HookProc? s_callback;
    private static KeyCaptureHook? s_instance;

    private readonly Action<int, bool> _onKey;
    private IntPtr _hook;
    private Thread? _thread;
    private uint _threadId;

    /// <param name="onKey">
    /// Called with (normalized virtual key, isDown) for every event. Runs on the hook thread —
    /// the subscriber marshals to its own dispatcher.
    /// </param>
    public KeyCaptureHook(Action<int, bool> onKey) => _onKey = onKey;

    /// <summary>Installs the hook. One recorder at a time; a second Start steals it.</summary>
    public bool Start()
    {
        Dispose();
        s_instance = this;

        using var ready = new ManualResetEventSlim(false);
        var installed = false;

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            s_callback = StaticCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, s_callback, GetModuleHandle(null), 0);
            installed = _hook != IntPtr.Zero;

            // ReSharper disable once AccessToDisposedClosure
            ready.Set();
            if (!installed) return;

            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        })
        {
            IsBackground = true,
            Name = "VoxScribe key recorder hook",
            Priority = ThreadPriority.AboveNormal,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));

        return installed;
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

    private static IntPtr StaticCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        var self = s_instance;
        if (code != HC_ACTION || self is null) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        try
        {
            var e = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (e.ExtraInfo != PushToTalkHook.InjectedTag)
            {
                var message = wParam.ToInt32();
                var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (isDown || message is WM_KEYUP or WM_SYSKEYUP)
                {
                    var key = PushToTalkHook.Normalize(
                        (int)e.VirtualKey, (int)(e.ScanCode & 0xFF), (e.Flags & LLKHF_EXTENDED) != 0);
                    self._onKey(key, isDown);
                }
            }
        }
        catch (Exception)
        {
            // Never let an exception escape into the hook chain.
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_thread is null) return;

        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));

        _thread = null;
        _threadId = 0;

        if (ReferenceEquals(s_instance, this))
        {
            s_instance = null;
            s_callback = null;
        }
    }
}
