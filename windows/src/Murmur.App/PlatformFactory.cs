using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Murmur.Abstractions;

namespace Murmur.App;

/// <summary>
/// Loads the Windows platform layer, if it is present.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why reflection rather than a project reference:</b> <c>Murmur.Platform.Windows</c>
/// targets <c>net10.0-windows</c>. Referencing it directly would force this project onto that
/// TFM too, and the app could then no longer be built or headless-tested on macOS — losing
/// the fast local loop that is the whole reason for choosing Avalonia.
/// </para>
/// <para>
/// The assembly is shipped alongside the app on Windows and simply absent elsewhere, so the
/// lookup failing is the normal, expected case on a developer's Mac.
/// </para>
/// </remarks>
internal static class PlatformFactory
{
    private const string AssemblyName = "Murmur.Platform.Windows";
    private const string Namespace = "Murmur.Platform.Windows";

    private static Assembly? _assembly;
    private static bool _attempted;

    /// <summary>Whether the Windows platform assembly could be loaded.</summary>
    public static bool IsAvailable => Load() is not null;

    /// <summary>
    /// Teaches the default load context to find the platform assembly beside the executable.
    /// </summary>
    /// <remarks>
    /// <c>PublishSingleFile</c> only bundles assemblies the compiler knows about, and this
    /// one is deliberately invisible to it — that is what keeps <c>Murmur.App</c> on plain
    /// <c>net10.0</c>. It therefore ships as a loose file next to the exe. Default probing
    /// normally finds it, but a single-file host resolves differently enough that relying on
    /// that alone is a bet — and losing it means the app starts fine and then does nothing
    /// when the user presses the key. Explicit is cheaper than that failure.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Murmur.Platform.Windows ships whole beside the executable and is "
                      + "never trimmed; nothing it depends on can have been removed.")]
    public static void InstallResolver()
    {
        if (_resolverInstalled) return;
        _resolverInstalled = true;

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            // Not just the platform assembly: its dependencies (NAudio.Wasapi, NAudio.Core…)
            // are equally invisible to the compiler, so they are not in deps.json either and
            // default probing refuses them even when the file sits right beside the exe.
            if (name.Name is null) return null;

            var candidate = System.IO.Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
    }

    private static bool _resolverInstalled;

    /// <summary>Creates the WASAPI capture, or null off Windows.</summary>
    /// <param name="deviceId">An <c>MMDevice.ID</c>, or null for the system default.</param>
    public static IAudioCapture? CreateAudioCapture(string? deviceId = null) =>
        Create<IAudioCapture>("WasapiAudioCapture", [deviceId]);

    /// <summary>Active capture devices as (ID, friendly name) pairs; empty off Windows.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    public static KeyValuePair<string, string>[] ListCaptureDevices()
    {
        var method = Load()?.GetType($"{Namespace}.AudioDeviceCatalog")?.GetMethod("ListCapture");
        try
        {
            return method?.Invoke(null, null) as KeyValuePair<string, string>[] ?? [];
        }
        catch (TargetInvocationException)
        {
            return []; // no audio service — settings shows only "system default"
        }
    }

    /// <summary>Creates the low-level keyboard hook, or null off Windows.</summary>
    public static IHotkeySource? CreateHotkeySource(int virtualKey) =>
        CreateHotkeySource([virtualKey]);

    /// <summary>Creates the hook armed with a chord, or null off Windows.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    public static IHotkeySource? CreateHotkeySource(int[] virtualKeys)
    {
        var hook = Create<IHotkeySource>("PushToTalkHook", []);

        // Keys is int[] on the concrete type; set by name to avoid referencing it.
        hook?.GetType().GetProperty("Keys")?.SetValue(hook, virtualKeys);
        return hook;
    }

    /// <summary>Re-arms a live hook with a new chord, so a recorded shortcut works
    /// immediately instead of after a restart.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    public static void UpdateHotkeyChord(IHotkeySource hotkey, int[] virtualKeys) =>
        hotkey.GetType().GetProperty("Keys")?.SetValue(hotkey, virtualKeys);

    /// <summary>
    /// Names the keys that suppress <paramref name="hotkey"/> while held, so a longer chord
    /// sharing its keys wins instead of both firing.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    public static void UpdateHotkeyBlockers(IHotkeySource hotkey, int[] virtualKeys) =>
        hotkey.GetType().GetProperty("Blockers")?.SetValue(hotkey, virtualKeys);

    /// <summary>
    /// Starts the global key recorder, or returns null off Windows. Dispose to stop.
    /// </summary>
    /// <param name="onKey">(normalized virtual key, isDown) — called on the hook thread.</param>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    public static IDisposable? StartKeyCapture(Action<int, bool> onKey)
    {
        if (Create<IDisposable>("KeyCaptureHook", [onKey]) is not { } hook) return null;

        var started = hook.GetType().GetMethod("Start")?.Invoke(hook, null) as bool? ?? false;
        if (started) return hook;

        hook.Dispose();
        return null;
    }

    /// <summary>The layout-local display name of a virtual key, e.g. "RIGHT CTRL".</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    public static string KeyDisplayName(int virtualKey)
    {
        var method = Load()?.GetType($"{Namespace}.KeyCaptureHook")?.GetMethod("NameOf");
        try
        {
            return method?.Invoke(null, [virtualKey]) as string ?? $"VK 0x{virtualKey:X2}";
        }
        catch (TargetInvocationException)
        {
            return $"VK 0x{virtualKey:X2}";
        }
    }

    /// <summary>Whether Vox-Scribe is registered to start at login; false off Windows.</summary>
    public static bool IsLaunchAtLoginEnabled() => StartupCall("IsEnabled", null) as bool? ?? false;

    /// <summary>Registers or unregisters the login entry. No-op off Windows.</summary>
    public static void SetLaunchAtLogin(bool enabled) => StartupCall("Set", [enabled]);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    private static object? StartupCall(string method, object?[]? arguments)
    {
        var target = Load()?.GetType($"{Namespace}.StartupRegistration")?.GetMethod(method);
        try
        {
            return target?.Invoke(null, arguments);
        }
        catch (TargetInvocationException)
        {
            // A locked-down machine can deny the Run key. Reporting "off" and doing nothing
            // is better than a crash from a settings checkbox.
            return null;
        }
    }

    /// <summary>Creates the SendInput injector, or null off Windows.</summary>
    public static ITextInjector? CreateTextInjector() =>
        Create<ITextInjector>("SendInputTextInjector", []);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "The platform assembly is published whole alongside the app and is "
                      + "never trimmed; its types are resolved by name at startup.")]
    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:AssemblyLocation",
        Justification = "Assembly.Load resolves from the bundle, not from a file path.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Murmur.Platform.Windows is published whole and never trimmed.")]
    private static T? Create<T>(string typeName, object?[] arguments) where T : class
    {
        var assembly = Load();
        var type = assembly?.GetType($"{Namespace}.{typeName}");
        if (type is null) return null;

        try
        {
            return Activator.CreateInstance(type, arguments) as T;
        }
        catch (Exception e) when (e is MissingMethodException or TargetInvocationException)
        {
            return null;
        }
    }

    private static Assembly? Load()
    {
        if (_attempted) return _assembly;
        _attempted = true;

        // Absent on macOS and Linux, which is expected and must stay silent — this runs on
        // every launch of the headless test host.
        try
        {
            _assembly = Assembly.Load(AssemblyName);
        }
        catch (Exception e) when (e is FileNotFoundException or BadImageFormatException)
        {
            _assembly = null;
        }

        return _assembly;
    }
}
