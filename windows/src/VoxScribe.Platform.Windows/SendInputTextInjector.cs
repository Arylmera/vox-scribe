using System.Runtime.InteropServices;
using VoxScribe.Abstractions;

namespace VoxScribe.Platform.Windows;

/// <summary>
/// Types text into whatever application has focus.
/// </summary>
/// <remarks>
/// <para>
/// <b>UI Automation is not an option here.</b> On macOS the Accessibility API can write text
/// directly; Windows has no counterpart. <c>TextPattern</c> is documented as providing no
/// means to insert or modify text, and <c>ValuePattern</c> replaces an entire field rather
/// than inserting at the caret — and multi-line controls do not support it at all. Microsoft's
/// own sample code says plainly: "text input must be simulated."
/// </para>
/// <para>
/// So <c>SendInput</c> is the primary path, not a fallback. Short text is typed as Unicode
/// packets, which leaves the clipboard untouched. Longer text goes via the clipboard, because
/// some terminals and Electron apps drop characters when thousands of synthetic keystrokes
/// arrive back to back.
/// </para>
/// <para>
/// <b>Classic Win32 text controls get the text directly.</b> When the focused control is an
/// Edit or RichEdit — Windows 11 Notepad's editor is <c>RichEditD2DPT</c> — the text goes in
/// with <c>EM_REPLACESEL</c>, as one undoable insertion at the caret. Notepad's autocorrect
/// rewrites words while synthetic keystrokes are still arriving and mangles them
/// ("eeeeees cccccc…"), even at one character per 15 ms; measured against the real Notepad in
/// October 2026, the direct insertion came through exact every time.
/// </para>
/// </remarks>
public sealed class SendInputTextInjector : ITextInjector
{
    /// <summary>Above this many characters, paste instead of typing.</summary>
    private const int PasteThreshold = 200;

    /// <summary>Characters per <c>SendInput</c> call when typing.</summary>
    private const int ChunkSize = 40;

    /// <summary>Pause between chunks, so slower targets keep up.</summary>
    private static readonly TimeSpan ChunkGap = TimeSpan.FromMilliseconds(4);

    /// <summary>
    /// Time for the target to observe the new clipboard before Ctrl+V arrives. Without it, a
    /// fast paste can pick up the <i>previous</i> contents.
    /// </summary>
    private static readonly TimeSpan ClipboardSettle = TimeSpan.FromMilliseconds(60);

    /// <summary>Time for the target to finish reading before the clipboard is restored.</summary>
    private static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the target gets to answer Ctrl+C before the clipboard is read.</summary>
    private static readonly TimeSpan CopySettle = TimeSpan.FromMilliseconds(150);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_V = 0x56;
    private const int VK_C = 0x43;
    private const int VK_RETURN = 0x0D;
    private const int VK_BACK = 0x08;   // not in the extended-key set, plain scancode path

    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL, ParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private const uint EM_REPLACESEL = 0x00C2;
    private const int GWL_STYLE = -16;
    private const long ES_READONLY = 0x0800;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int Size;
        public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, char[] name, int capacity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, nint wParam, string lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>Replaces the built-in Win32 clipboard paste; null uses it.</summary>
    /// <remarks>
    /// Injected rather than called directly because clipboard access is UI-framework specific
    /// and must happen on an STA thread — the app supplies an implementation that marshals to
    /// its own dispatcher.
    /// </remarks>
    public Func<string, CancellationToken, Task<bool>>? ClipboardPaste { get; init; }

    /// <inheritdoc />
    public async ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text)) return true;

        if (TryReplaceSelection(text)) return true;

        // Newlines sent as Unicode packets do not reliably produce a new line — many controls
        // want a real VK_RETURN. Long text goes to the clipboard anyway, which handles them.
        var hasNewlines = text.Contains('\n', StringComparison.Ordinal);

        if (text.Length <= PasteThreshold && !hasNewlines)
        {
            return TypeUnicode(text);
        }

        if (ClipboardPaste is not null)
        {
            return await ClipboardPaste(text, cancellationToken).ConfigureAwait(false);
        }

        return await PasteAsync(text, cancellationToken).ConfigureAwait(false) || TypeWithNewlines(text);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ctrl+C, then the clipboard sequence number says whether anything was copied — an app
    /// with no selection copies nothing and the number does not move. The user's text is put
    /// back afterwards, as <see cref="PasteAsync"/> does.
    /// ponytail: only text is saved and restored, as for paste.
    /// </remarks>
    public async ValueTask<string?> CopySelectionAsync(CancellationToken cancellationToken)
    {
        var previous = ReadClipboardText();
        var before = GetClipboardSequenceNumber();

        if (!PressCtrl(VK_C)) return null;
        await Task.Delay(CopySettle, cancellationToken).ConfigureAwait(false);

        if (GetClipboardSequenceNumber() == before) return null;

        var copied = ReadClipboardText();
        if (previous is not null) WriteClipboardText(previous);
        return string.IsNullOrEmpty(copied) ? null : copied;
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard, presses Ctrl+V, then puts back the text
    /// that was there. False when the clipboard could not be taken, so the caller types instead.
    /// </summary>
    /// <remarks>
    /// Nothing supplied <see cref="ClipboardPaste"/> until October 2026, so every long dictation
    /// was typed, and an 1 371-character one lost its last 550 in the Orca terminal.
    /// ponytail: only text is saved and restored — an image on the clipboard is replaced by the
    /// dictation. Save every format if that ever matters.
    /// </remarks>
    private static async Task<bool> PasteAsync(string text, CancellationToken cancellationToken)
    {
        var previous = ReadClipboardText();
        if (!WriteClipboardText(text)) return false;

        await Task.Delay(ClipboardSettle, cancellationToken).ConfigureAwait(false);
        var pasted = PressCtrlV();
        await Task.Delay(PasteSettle, cancellationToken).ConfigureAwait(false);

        if (previous is not null) WriteClipboardText(previous);
        return pasted;
    }

    private static string? ReadClipboardText()
    {
        if (!OpenClipboardWithRetry()) return null;
        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool WriteClipboardText(string text)
    {
        if (!OpenClipboardWithRetry()) return false;
        try
        {
            if (!EmptyClipboard()) return false;

            var bytes = (text.Length + 1) * sizeof(char);
            var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
            if (handle == IntPtr.Zero) return false;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                GlobalFree(handle);
                return false;
            }

            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
            GlobalUnlock(handle);

            // On success the clipboard owns the memory; on failure it is still ours.
            if (SetClipboardData(CF_UNICODETEXT, handle) != IntPtr.Zero) return true;
            GlobalFree(handle);
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Another process — a clipboard manager, the target itself — can hold the clipboard for a
    /// few milliseconds, so one refusal is not an answer.
    /// </summary>
    private static bool OpenClipboardWithRetry()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }

        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One VK_BACK down/up pair through the same tagged <c>SendInput</c> path as typing, so
    /// the app's own hook ignores it. The count loop lives in Core.
    /// </remarks>
    public ValueTask<bool> BackspaceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        INPUT[] backspace = [KeyInput(VK_BACK, up: false), KeyInput(VK_BACK, up: true)];
        return ValueTask.FromResult(SendInput(2, backspace, InputSize) == 2);
    }

    /// <inheritdoc />
    public ValueTask<bool> EnterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        INPUT[] enter = [KeyInput(VK_RETURN, up: false), KeyInput(VK_RETURN, up: true)];
        return ValueTask.FromResult(SendInput(2, enter, InputSize) == 2);
    }

    /// <summary>
    /// Inserts <paramref name="text"/> at the caret of the focused control when it is a writable
    /// Edit or RichEdit; false when it is anything else, so the caller types instead.
    /// </summary>
    private static bool TryReplaceSelection(string text)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        var info = new GUITHREADINFO { Size = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(GetWindowThreadProcessId(foreground, out _), ref info)) return false;

        var focus = info.Focus;
        if (focus == IntPtr.Zero) return false;

        var name = new char[64];
        var length = GetClassName(focus, name, name.Length);
        var className = new string(name, 0, Math.Max(length, 0));
        var isEdit = className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
            || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);

        // EM_REPLACESEL ignores ES_READONLY, so a read-only field must be left to typing,
        // which it rejects on its own.
        if (!isEdit || (GetWindowLongPtr(focus, GWL_STYLE).ToInt64() & ES_READONLY) != 0) return false;

        // Edit controls break lines on CRLF. A hung or higher-integrity target fails the call
        // instead of blocking it, and typing gets its chance.
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        return SendMessageTimeout(focus, EM_REPLACESEL, 1, normalized, SMTO_ABORTIFHUNG, 1000, out _) != IntPtr.Zero;
    }

    /// <summary>Types arbitrary text as Unicode packets.</summary>
    /// <remarks>
    /// <para>
    /// Iterating by UTF-16 code unit is correct and intentional. A non-BMP character — an
    /// emoji, some CJK extensions — is two surrogate halves, and each is sent as its own
    /// event; Unicode-aware controls recombine them. The 16-bit <c>ScanCode</c> field cannot
    /// carry a full 21-bit code point.
    /// </para>
    /// <para>
    /// <b>Failure here is silent.</b> <c>SendInput</c> is subject to UIPI, and Microsoft
    /// documents that when it is blocked "neither GetLastError nor the return value will
    /// indicate the failure was caused by UIPI blocking." Injecting into an elevated window
    /// from a normal process can return success and do nothing.
    /// </para>
    /// </remarks>
    private static bool TypeUnicode(string text)
    {
        for (var offset = 0; offset < text.Length; offset += ChunkSize)
        {
            var length = Math.Min(ChunkSize, text.Length - offset);

            // Never split a surrogate pair across two SendInput calls.
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length++;

            var inputs = new INPUT[length * 2];
            for (var i = 0; i < length; i++)
            {
                var unit = text[offset + i];
                inputs[i * 2] = UnicodeInput(unit, up: false);
                inputs[(i * 2) + 1] = UnicodeInput(unit, up: true);
            }

            if (SendInput((uint)inputs.Length, inputs, InputSize) != inputs.Length) return false;
            if (offset + length < text.Length) Thread.Sleep(ChunkGap);
        }

        return true;
    }

    /// <summary>Types text, sending a real Return for each newline.</summary>
    private static bool TypeWithNewlines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0 && !TypeUnicode(lines[i])) return false;

            if (i < lines.Length - 1)
            {
                INPUT[] enter = [KeyInput(VK_RETURN, up: false), KeyInput(VK_RETURN, up: true)];
                if (SendInput(2, enter, InputSize) != 2) return false;
            }
        }

        return true;
    }

    /// <summary>Presses Ctrl+V, temporarily lifting any modifier the user is resting on.</summary>
    /// <remarks>
    /// Microsoft: "This function does not reset the keyboard's current state. Any keys that
    /// are already pressed when the function is called might interfere." A user resting on
    /// Shift would otherwise get Ctrl+Shift+V — "paste as plain text", or something else
    /// entirely depending on the app.
    /// </remarks>
    public static bool PressCtrlV() => PressCtrl(VK_V);

    /// <summary>Presses Ctrl+<paramref name="virtualKey"/>, temporarily lifting any modifier the user is resting on.</summary>
    /// <remarks>
    /// Microsoft: "This function does not reset the keyboard's current state. Any keys that
    /// are already pressed when the function is called might interfere." A user resting on
    /// Shift would otherwise get Ctrl+Shift+<c>key</c> — a different command entirely in some apps.
    /// </remarks>
    private static bool PressCtrl(int virtualKey)
    {
        var held = new List<int>();
        foreach (var key in new[] { VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN })
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0) held.Add(key);
        }

        var sequence = new List<INPUT>();
        foreach (var key in held) sequence.Add(KeyInput(key, up: true));

        sequence.Add(KeyInput(VK_CONTROL, up: false));
        sequence.Add(KeyInput(virtualKey, up: false));
        sequence.Add(KeyInput(virtualKey, up: true));
        sequence.Add(KeyInput(VK_CONTROL, up: true));

        foreach (var key in held) sequence.Add(KeyInput(key, up: false));

        var inputs = sequence.ToArray();
        return SendInput((uint)inputs.Length, inputs, InputSize) == inputs.Length;
    }

    /// <summary>
    /// Presses and releases <paramref name="virtualKey"/> <i>without</i> the injected tag, so
    /// the app's own hook sees it as if a finger had. Self-test only: it is how the published
    /// binary proves its hook end to end on a machine nobody is typing on.
    /// </summary>
    public static bool TapUntagged(int virtualKey)
    {
        var down = KeyInput(virtualKey, up: false);
        var up = KeyInput(virtualKey, up: true);
        down.Union.Keyboard.ExtraInfo = IntPtr.Zero;
        up.Union.Keyboard.ExtraInfo = IntPtr.Zero;

        INPUT[] tap = [down, up];
        return SendInput(2, tap, InputSize) == 2;
    }

    /// <summary>How long to wait after setting the clipboard before pasting.</summary>
    public static TimeSpan ClipboardSettleDelay => ClipboardSettle;

    /// <summary>How long to wait after pasting before restoring the clipboard.</summary>
    public static TimeSpan PasteSettleDelay => PasteSettle;

    private static INPUT UnicodeInput(ushort codeUnit, bool up) => new()
    {
        Type = INPUT_KEYBOARD,
        Union = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = 0,   // must be 0 for KEYEVENTF_UNICODE
                ScanCode = codeUnit,
                Flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                Time = 0,
                ExtraInfo = PushToTalkHook.InjectedTag,
            },
        },
    };

    private static INPUT KeyInput(int virtualKey, bool up)
    {
        var flags = up ? KEYEVENTF_KEYUP : 0;
        if (IsExtendedKey(virtualKey)) flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            Type = INPUT_KEYBOARD,
            Union = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = (ushort)virtualKey,
                    ScanCode = (ushort)MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC),
                    Flags = flags,
                    Time = 0,
                    // Tagged so our own hook ignores it and doesn't re-trigger dictation.
                    ExtraInfo = PushToTalkHook.InjectedTag,
                },
            },
        };
    }

    private static bool IsExtendedKey(int virtualKey) => virtualKey is
        0xA3 or 0xA5 or 0x5B or 0x5C or   // right ctrl/alt, both Windows keys
        0x2D or 0x2E or 0x24 or 0x23 or   // insert, delete, home, end
        0x21 or 0x22 or                   // page up/down
        0x25 or 0x26 or 0x27 or 0x28;     // arrows
}
