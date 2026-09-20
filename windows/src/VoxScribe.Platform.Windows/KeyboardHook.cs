using System.Runtime.InteropServices;

namespace VoxScribe.Platform.Windows;

/// <summary>
/// The one low-level keyboard hook in the process. Every chord, the cancel key and the
/// shortcut recorder subscribe here and receive the same normalized (key, isDown) stream.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one hook, not one per shortcut.</b> Each <c>WH_KEYBOARD_LL</c> hook is a
/// synchronous detour on every keystroke in the session, and each is a separate chance for
/// Windows to remove it silently after a slow callback. Five shortcuts meant five detours
/// and five such chances. One hook fans out to as many listeners as there are shortcuts.
/// </para>
/// <para>
/// <b>Why a dedicated thread:</b> Microsoft's guidance is explicit — low-level hooks should
/// run on their own thread that hands work off and returns immediately. If the hook
/// procedure exceeds <c>LowLevelHooksTimeout</c> (capped at 1000 ms since Windows 10 1709)
/// the system <b>silently removes the hook, with no way for the application to know</b>. A
/// hook on the UI thread dies the first time the app does a slow layout pass.
/// </para>
/// <para>
/// <b>Why the key is never swallowed:</b> the macOS build consumed Right Option because on
/// macOS that key types characters. Here, suppression buys nothing and risks something much
/// worse — if the key-down is swallowed but the key-up escapes (the hook timed out
/// mid-gesture, or focus crossed into an elevated window), the target application believes
/// the modifier is held down forever.
/// </para>
/// <para>
/// The statics here are a <i>registry</i>, not per-instance state: the lesson of the old
/// singleton (a second hook silently replacing the first) is exactly what the listener list
/// exists to prevent. Each listener keeps its own chord state.
/// </para>
/// </remarks>
internal static class KeyboardHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    private const uint LLKHF_EXTENDED = 0x01;

    /// <summary>
    /// Stamped into <c>dwExtraInfo</c> on every event this app injects, so our own Ctrl+V
    /// paste cannot re-enter this hook and re-trigger dictation.
    /// </summary>
    public static readonly IntPtr InjectedTag = unchecked((IntPtr)0x4D524D52); // 'MRMR'

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

    private static readonly Lock Gate = new();

    /// <summary>
    /// Replaced wholesale on every change and read without a lock by the callback: the hook
    /// thread must never wait on anything, and a snapshot array costs it nothing.
    /// </summary>
    private static volatile Action<int, bool>[] s_listeners = [];

    /// <summary>
    /// Roots the delegate for the lifetime of the hook. Microsoft: "you must ensure the
    /// callback is not moved around by the garbage collector, otherwise your app will crash
    /// with an ExecutionEngineException." A local variable here produces a crash minutes or
    /// hours later, at random, with no useful stack.
    /// </summary>
    private static HookProc? s_callback;

    private static IntPtr s_hook;
    private static Thread? s_thread;
    private static uint s_threadId;
    private static bool s_installed;

    /// <summary>
    /// Starts delivering every physical key event to <paramref name="listener"/>, installing
    /// the hook if this is the first subscriber.
    /// </summary>
    /// <returns>False if the hook could not be installed.</returns>
    public static bool Subscribe(Action<int, bool> listener)
    {
        lock (Gate)
        {
            if (s_thread is null) Install();
            if (!s_installed) return false;

            if (Array.IndexOf(s_listeners, listener) < 0) s_listeners = [.. s_listeners, listener];
            return true;
        }
    }

    /// <summary>Stops delivering to <paramref name="listener"/>; the last one out uninstalls the hook.</summary>
    public static void Unsubscribe(Action<int, bool> listener)
    {
        lock (Gate)
        {
            s_listeners = [.. s_listeners.Where(l => l != listener)];
            if (s_listeners.Length == 0) Uninstall();
        }
    }

    /// <summary>Callers hold <see cref="Gate"/>.</summary>
    private static void Install()
    {
        using var ready = new ManualResetEventSlim(false);
        s_installed = false;

        s_thread = new Thread(() =>
        {
            s_threadId = GetCurrentThreadId();
            s_callback = Callback;

            // Pass the running module's handle. IntPtr.Zero is documented as possibly
            // failing when threadId is 0, which is exactly the global case.
            s_hook = SetWindowsHookEx(WH_KEYBOARD_LL, s_callback, GetModuleHandle(null), 0);
            s_installed = s_hook != IntPtr.Zero;

            // ReSharper disable once AccessToDisposedClosure
            ready.Set();
            if (!s_installed) return;

            // Required. The system delivers hook callbacks by *sending a message* to this
            // thread, so without a pump the hook is installed but never invoked. No
            // TranslateMessage/DispatchMessage: this thread owns no windows, and the only
            // message it cares about is the WM_QUIT that ends the loop.
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            if (s_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(s_hook);
                s_hook = IntPtr.Zero;
            }
        })
        {
            IsBackground = true,
            Name = "VoxScribe keyboard hook",
            // Stay ahead of the 1000 ms timeout that silently removes the hook.
            Priority = ThreadPriority.AboveNormal,
        };

        s_thread.SetApartmentState(ApartmentState.STA);
        s_thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));

        if (!s_installed) Uninstall();
    }

    /// <summary>Callers hold <see cref="Gate"/>.</summary>
    private static void Uninstall()
    {
        if (s_thread is null) return;

        if (s_threadId != 0) PostThreadMessage(s_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        s_thread.Join(TimeSpan.FromSeconds(2));

        s_thread = null;
        s_threadId = 0;
        s_callback = null;
        s_installed = false;
    }

    /// <summary>Keep the body short — this runs inside the system's hook timeout.</summary>
    private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code != HC_ACTION) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        try
        {
            Dispatch(wParam, lParam);
        }
        catch (Exception)
        {
            // An exception escaping into the hook chain would take the process down from a
            // thread with no useful context. Swallow and keep the chain intact.
        }

        // Always chain. Microsoft: otherwise "other applications that have installed
        // WH_KEYBOARD_LL hooks will not receive hook notifications and may behave
        // incorrectly as a result."
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static void Dispatch(IntPtr wParam, IntPtr lParam)
    {
        var e = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // Ignore anything this app injected itself.
        if (e.ExtraInfo == InjectedTag) return;

        // ToInt32 rather than a cast: since .NET 7 an explicit (int)IntPtr conversion
        // silently truncates instead of throwing, which CA2020 flags. Window messages are
        // always small, so the checked conversion is free and states the intent.
        var message = wParam.ToInt32();
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        var isUp = message is WM_KEYUP or WM_SYSKEYUP;
        if (!isDown && !isUp) return;

        var key = PushToTalkHook.Normalize(
            (int)e.VirtualKey, (int)(e.ScanCode & 0xFF), (e.Flags & LLKHF_EXTENDED) != 0);

        foreach (var listener in s_listeners)
        {
            try
            {
                listener(key, isDown);
            }
            catch (Exception)
            {
                // One listener's fault must not cost the others their keystroke.
            }
        }
    }
}
