using System.Runtime.InteropServices;
using VoxScribe.Abstractions;

namespace VoxScribe.Platform.Windows;

/// <summary>
/// Remembers the foreground window and the UI Automation focused element at press, and
/// brings both back at release.
/// </summary>
/// <remarks>
/// <para>
/// Re-activating the window alone makes Windows restore the window's <i>own</i> idea of its
/// last focused control, which is wrong the moment the user clicked another field in the
/// same window while speaking. UI Automation's focused element identifies the field itself
/// across Win32, WPF, Electron and Chromium, and <c>SetFocus</c> restores it. The caret is
/// the control's business.
/// </para>
/// <para>
/// <b>Direct COM interop, on purpose.</b> <c>System.Windows.Automation</c> lives in the
/// WindowsDesktop framework, which this self-contained publish does not ship. The
/// interfaces below are declared as a <i>prefix</i> of the real vtables; that is valid as
/// long as only declared slots are called, and only <c>GetFocusedElement</c> and
/// <c>SetFocus</c> are.
/// </para>
/// <para>
/// Same UIPI caveat as <c>SendInput</c>: an elevated target ignores all of this silently and
/// the text goes wherever focus is. Every call runs on a pool thread under a timeout so a
/// frozen target cannot stall the dictation.
/// </para>
/// </remarks>
public sealed class UiAutomationFocusAnchor : IFocusAnchor
{
    /// <summary>Longest a capture may take before it is abandoned.</summary>
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>How long to wait for the window to actually come forward.</summary>
    private static readonly TimeSpan ForegroundWait = TimeSpan.FromMilliseconds(300);

    /// <summary>Interval between foreground checks.</summary>
    private static readonly TimeSpan ForegroundPoll = TimeSpan.FromMilliseconds(10);

    /// <summary>Pause after focusing, so the target's own focus handling finishes first.</summary>
    private static readonly TimeSpan FocusSettle = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Longest a restore may take, all steps included: both foreground attempts, the settle
    /// pause, and slack for sleep granularity. Derived, not chosen: the timeout only abandons
    /// the restore, it cannot stop it, so a budget below the synchronous worst case would let
    /// an abandoned restore keep switching windows while the injector is already typing.
    /// </summary>
    private static readonly TimeSpan RestoreTimeout =
        ForegroundWait + ForegroundWait + FocusSettle + TimeSpan.FromMilliseconds(100);

    [ComImport]
    [Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation;

    /// <summary>Prefix of IUIAutomation: only <see cref="GetFocusedElement"/> is called.</summary>
    [ComImport]
    [Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);
        int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2);
        IUIAutomationElement GetRootElement();
        IUIAutomationElement ElementFromHandle(IntPtr hwnd);
        IUIAutomationElement ElementFromPoint(long pt);
        IUIAutomationElement GetFocusedElement();
    }

    /// <summary>Prefix of IUIAutomationElement: only <see cref="SetFocus"/> is called.</summary>
    [ComImport]
    [Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    // Built on first use from inside the pool-thread work below, never from the app's STA UI
    // thread: UI Automation clients belong in an MTA. An object created on the STA would have
    // every call marshalled back to it, so a hung target would freeze the window instead of
    // just timing out here.
    private readonly Lazy<IUIAutomation?> _automation = new(TryCreateAutomation);

    private static IUIAutomation? TryCreateAutomation()
    {
        try { return (IUIAutomation)new CUIAutomation(); }
        catch (COMException) { return null; }
        catch (InvalidCastException) { return null; }
    }

    /// <inheritdoc />
    public async ValueTask<IFocusTarget?> CaptureAsync(CancellationToken cancellationToken)
    {
        var work = Task.Run(() =>
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            IUIAutomationElement? element = null;
            try { element = _automation.Value?.GetFocusedElement(); }
            catch (COMException) { /* no element; the window alone is still worth restoring */ }

            return new Target(hwnd, element);
        }, cancellationToken);

        var finished = await Task.WhenAny(work, Task.Delay(CaptureTimeout, cancellationToken))
            .ConfigureAwait(false);

        return finished == work && work.Status == TaskStatus.RanToCompletion ? work.Result : null;
    }

    private sealed class Target : IFocusTarget
    {
        private readonly IntPtr _hwnd;
        private readonly IUIAutomationElement? _element;

        public Target(IntPtr hwnd, IUIAutomationElement? element)
        {
            _hwnd = hwnd;
            _element = element;
        }

        public async ValueTask<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            var work = Task.Run(Restore, cancellationToken);
            var finished = await Task.WhenAny(work, Task.Delay(RestoreTimeout, cancellationToken))
                .ConfigureAwait(false);

            return finished == work && work.Status == TaskStatus.RanToCompletion && work.Result;
        }

        private bool Restore()
        {
            if (!IsWindow(_hwnd)) return false;

            var forward = GetForegroundWindow() == _hwnd || BringForward();

            // Not fatal: with the window forward, Windows has already restored its last
            // focused child, which is right whenever the user did not click another field.
            try { _element?.SetFocus(); }
            catch (COMException) { }

            Thread.Sleep(FocusSettle);
            return forward;
        }

        private bool BringForward()
        {
            SetForegroundWindow(_hwnd);
            if (WaitForeground()) return true;

            // A background process may not steal foreground. Borrowing the current
            // foreground thread's input queue is the documented-by-folklore way round it.
            var current = GetForegroundWindow();
            var ours = GetCurrentThreadId();
            var theirs = GetWindowThreadProcessId(current, IntPtr.Zero);
            if (theirs == 0 || theirs == ours) return false;

            AttachThreadInput(ours, theirs, true);
            try
            {
                SetForegroundWindow(_hwnd);
                return WaitForeground();
            }
            finally
            {
                AttachThreadInput(ours, theirs, false);
            }
        }

        private bool WaitForeground()
        {
            var deadline = Environment.TickCount64 + (long)ForegroundWait.TotalMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (GetForegroundWindow() == _hwnd) return true;
                Thread.Sleep(ForegroundPoll);
            }

            return GetForegroundWindow() == _hwnd;
        }
    }
}
