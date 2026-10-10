# Claude Code Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A band above Claude Code's prompt (voxscribe plugin) with /parle and /say buttons, an armable target for the command chord, and click-to-talk — plus Install/Uninstall of the plugin from Vox-Scribe's Settings.

**Architecture:** The plugin module (`claude-plugin/hooks/register.tsx`) is dumb: it draws the band, writes intent files and submits what the app leaves in its outbox. All rules (live target, ack, fallback) live in `VoxScribe.Core/ClaudeBridge.cs`, tested. The engine gains one delegate (`DeliverToClaude`) checked at the top of `SendCommandAsync`, and two public methods (`BeginCommand`, `EndUtterance`) for click-to-talk.

**Tech Stack:** C# / .NET 10, xUnit + Shouldly, Avalonia 11 (Settings UI), TSX plugin module on Claude Code's undocumented module API (reference: HeyCubit/effortless `hooks/register.tsx`).

**Spec:** `docs/superpowers/specs/2026-10-10-claude-panel-design.md`

## Global Constraints

- Failure isolation: if Vox-Scribe fails, only Vox-Scribe fails. Every module handler catches and falls back to `next(e)`; every bridge file operation catches IO errors.
- With no live target, the command chord runs today's code path **line for line**; `CommandModeTests` stays green **unmodified**.
- `NotAcknowledged` never falls back to keyboard injection (a late submit would double-send).
- Vox-Scribe never reads or writes `~/.claude/settings.json`.
- The module intercepts no command (`command.run` untouched); `speak.ps1` stays as is (PowerShell 5.1, BOM).
- Directory: `%LOCALAPPDATA%\VoxScribe\claude\` (`DataDirectory.File("claude")`). Files: `target.json`, `mic.json`, `status.json`, `outbox/<session_id>.json`, `outbox/<id>.ack`.
- Timings: module poll ~300 ms; target beat every 2 s; stale after 6 s; ack timeout 3 s.
- `VoxScribe.Core` stays plain `net10.0`, no Win32.
- Build gate: `cd windows && dotnet build VoxScribe.sln --no-incremental -warnaserror` and `dotnet format VoxScribe.sln --verify-no-changes`. Tests: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf`.
- Load the `test-gates` skill before writing tests; mutation-prove each new guard (break the line, watch the test fail, restore).
- Views contain no literal colours/sizes — tokens only (`Design/DesignTokens.cs`).

## Two deliberate deviations from the spec (fixed in the spec by Task 8)

1. **No virtual hotkey source.** In toggle mode the engine ignores `Released`, so "stop" mapped to a release would never stop. Click-to-talk calls `engine.BeginCommand()` / `engine.EndUtterance()` / `engine.CancelAsync()` instead.
2. **No `Journal.Record` on `Delivered`.** The journal drives the undo shortcut, which backspaces into the focused window; text submitted through `$.prompt.submit` was never typed there.

## File map

| File | Change |
|---|---|
| `windows/src/VoxScribe.Core/ClaudeBridge.cs` | Create — target, outbox/ack, status, mic watcher |
| `windows/src/VoxScribe.Core/ClaudePlugin.cs` | Create — CLI argv, list parsing, process runner |
| `windows/src/VoxScribe.Core/DictationEngine.cs` | Modify — `DeliverToClaude`, `BeginCommand`, `EndUtterance` |
| `windows/src/VoxScribe.App/Composition.cs` | Modify — build and wire the bridge |
| `windows/src/VoxScribe.App/Views/Settings/ClaudeSection.cs` | Create — the CLAUDE tab |
| `windows/src/VoxScribe.App/Views/Settings/ShortcutsSection.cs` | Modify — command title moves out |
| `windows/src/VoxScribe.App/Views/SettingsPage.cs` | Modify — `SettingsTab.Claude` |
| `windows/tests/VoxScribe.Core.Tests/ClaudeBridgeTests.cs` | Create |
| `windows/tests/VoxScribe.Core.Tests/ClaudeDeliveryTests.cs` | Create |
| `windows/tests/VoxScribe.Core.Tests/ClaudePluginTests.cs` | Create |
| `claude-plugin/hooks/register.tsx` | Create — the band |
| `claude-plugin/hooks/hooks.json` | Modify — add `modules` |
| `claude-plugin/.claude-plugin/plugin.json` | Modify — version 1.1.0 |
| `claude-plugin/README.md` | Create — terminal flag, what the band does |
| `AGENTS.md`, spec | Modify — record the band and the deviations |

---

### Task 1: Feasibility spike (by hand, throwaway)

Nothing from this task is kept except the findings written into the spec. **If Q2 fails, stop and report to the owner: Tasks 2–5 and the click-to-talk half of Task 7 are void.**

**Files:**
- Create (temporary, not committed): `claude-plugin/hooks/register.tsx`, edit `claude-plugin/hooks/hooks.json`
- Modify (committed): `docs/superpowers/specs/2026-10-10-claude-panel-design.md` §1

- [ ] **Step 1: Write the spike module**

`claude-plugin/hooks/register.tsx`:

```tsx
import type { EngineInterface, Register } from 'claude-code'

// SPIKE — throwaway. Answers spec §1 Q1–Q6, then is deleted.
let started = false
let log: string[] = []

async function note($: EngineInterface, line: string) {
  log.push(`${new Date().toISOString()} ${line}`)
  const tmp = (await $.env.get('TEMP')) ?? '.'
  await $.fs.write(`${tmp}\\voxscribe-spike.log`, log.join('\n') + '\n').catch(() => undefined)
}

function startTimer($: EngineInterface) {
  if (started) return
  started = true
  $.clock.every(1000, () => void (async () => {
    const tmp = (await $.env.get('TEMP')) ?? '.'
    const text = await $.fs.read(`${tmp}\\voxscribe-spike-send.txt`).catch(() => '')
    if (typeof text !== 'string' || text.trim() === '') return
    await $.fs.write(`${tmp}\\voxscribe-spike-send.txt`, '')
    await note($, `submitting "${text.trim()}"`)
    const r = await $.prompt.submit({ text: text.trim() }).then(x => JSON.stringify(x), e => `ERR ${String(e)}`)
    await note($, `submit -> ${r}`)
  })().catch(e => void note($, `tick error ${String(e)}`)))
}

export const register: Register = (on) => {
  on('session.start', async ($, e, next) => {
    startTimer($)
    await note($, `session.start ${await $.session.id().catch(() => '?')} fs=${Object.keys($.fs).join(',')}`)
    return next(e)
  })
  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    try {
      startTimer($)
      const { Box, Text, Button } = $.ui.resolve(e) as Record<string, any>
      const rest = await next(e)
      return (
        <Box key="vox-spike-col" flexDirection="column">
          <Box key="vox-spike" flexDirection="row" gap={1}>
            <Text>VOX SPIKE ({e.surface}) busy={String(!!e.props.isWorking)}</Text>
            <Button key="vox-parle" label="Parle" onPress={() => void $.command.run({ command: 'voxscribe:parle', args: '' }).then(r => note($, `command.run -> ${JSON.stringify(r)}`), err => note($, `command.run ERR ${String(err)}`))} />
          </Box>
          {rest}
        </Box>
      )
    } catch (err) {
      void note($, `render error ${String(err)}`)
      return next(e)
    }
  })
}
```

Edit `claude-plugin/hooks/hooks.json` — add `"modules"` beside the existing `"hooks"` key:

```json
{
  "modules": ["./register.tsx"],
  "hooks": {
    "UserPromptExpansion": [ ...unchanged... ]
  }
}
```

- [ ] **Step 2: Load it**

First `claude plugin disable voxscribe@vox-scribe` — the installed 1.0.0 would otherwise fire the `/parle` hook a second time and muddy Q1/Q4 (re-enable it at Step 4). Then point Claude Code at the working tree (`claude --plugin-dir <repo>/claude-plugin`), with `CLAUDE_CODE_ENABLE_FUNCTION_HOOKS=1` set **for that shell only** (`$env:CLAUDE_CODE_ENABLE_FUNCTION_HOOKS = '1'` before `claude`). Do not edit `~/.claude/settings.json`.

- [ ] **Step 3: Answer the questions, writing each result down**

| # | How | Record |
|---|---|---|
| Q1 | Band visible **and** typed `/parle` still answers through `speak.ps1` (Vox-Scribe running) | modules + hooks coexist? |
| Q2 | Focus another app. `Set-Content $env:TEMP\voxscribe-spike-send.txt 'dis bonjour'`. Watch the session. Repeat in: Windows Terminal, an Orca pane, the desktop app | lands unfocused, per surface |
| Q3 | Same as Q2 while Claude is mid-answer | queued / failed / interrupted |
| Q4 | Press the band's Parle button with Vox-Scribe running | hook fires? reply read aloud? |
| Q5 | `fs=` keys in `%TEMP%\voxscribe-spike.log` | delete/rename present? |
| Q3b | In the log, the gap between the `submitting` and `submit ->` lines while Claude is mid-answer | does `submit` resolve at once, or only when the queued message is sent? |
| Q6 | Install another module plugin, or check the band coexists with `next(e)` content | our band + others both drawn? |
| Q7 | Add `void note($, 'render ' + await $.session.id())` to the render handler. Open two chats in the desktop app and two terminal sessions | one module instance per session (ids differ per instance's log lines), or one shared instance? |

- [ ] **Step 4: Record findings in the spec and remove the spike**

Replace the "If no" column of spec §1 with a "Finding (2026-10-xx)" column holding what you saw. Then:

```bash
git checkout -- claude-plugin/hooks/hooks.json
rm claude-plugin/hooks/register.tsx
git add docs/superpowers/specs/2026-10-10-claude-panel-design.md
git commit -m "docs: record the plugin-module spike findings"
```

Re-enable the installed plugin: `claude plugin enable voxscribe@vox-scribe`.

**Gate:** Q2 fails on every surface → stop, report. Q7 shows one module instance shared by several sessions → stop, report: Task 8's module-level `sid`/`armed`/`lastHandled` must become maps keyed by session id, a design change for the owner. Q3b shows `submit` resolving only when the queued message sends → Task 8 writes the ack right after starting `submit` (not after awaiting it); `lastHandled` still guarantees at-most-once. Q1 fails → Task 7 uses the layout the spike found. Q4 fails → Task 7's Parle/Say buttons write `speak/request.json` only if the spike found the transcript path, else drop them and tell the owner. Q3 "fails" → Task 7 holds the outbox text until `e.props.isWorking` is false (note it in the module).

---

### Task 2: ClaudeBridge — target, outbox, acknowledgement, status

**Files:**
- Create: `windows/src/VoxScribe.Core/ClaudeBridge.cs`
- Test: `windows/tests/VoxScribe.Core.Tests/ClaudeBridgeTests.cs`

**Interfaces:**
- Produces:
  - `public enum ClaudeDelivery { NoTarget, Delivered, NotAcknowledged }`
  - `public enum MicAction { Start, Stop, Cancel }` (used by Task 3)
  - `public sealed class ClaudeBridge : IDisposable`
    - `ClaudeBridge(string directory, IClock? clock = null, TimeSpan? ackTimeout = null)`
    - `static string DefaultDirectory`, `static TimeSpan Stale` (6 s)
    - `string? LiveTarget()`
    - `Task<ClaudeDelivery> TryDeliverAsync(string text, CancellationToken cancellationToken)`
    - `void PublishStatus(DictationState state, bool command)`
    - `void Start()`, `void Dispose()`

- [ ] **Step 1: Write the failing tests**

`windows/tests/VoxScribe.Core.Tests/ClaudeBridgeTests.cs`:

```csharp
using System.Text.Json;
using VoxScribe.Core;
using VoxScribe.Testing;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The app side of the Claude Code band: which session is armed, handing it a dictation, and
/// knowing whether it arrived. The module is a stand-in written in each test.
/// </summary>
public sealed class ClaudeBridgeTests : IDisposable
{
    private const string Session = "3f2c9a10-1b2c-4d5e-8f90-a1b2c3d4e5f6";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vox-claude-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ClaudeBridge Build(TimeSpan? ackTimeout = null)
    {
        var bridge = new ClaudeBridge(_dir, _clock, ackTimeout ?? TimeSpan.FromSeconds(2));
        Directory.CreateDirectory(Path.Combine(_dir, "outbox"));
        return bridge;
    }

    private void Arm(string session, TimeSpan age) =>
        File.WriteAllText(Path.Combine(_dir, "target.json"), JsonSerializer.Serialize(new
        {
            session_id = session,
            beat = (_clock.Now - age).ToUnixTimeMilliseconds(),
        }));

    /// <summary>Plays the module: waits for this session's outbox, then acknowledges it.</summary>
    private Task<string> AcknowledgeAsync(string session) => Task.Run(async () =>
    {
        var outbox = Path.Combine(_dir, "outbox", session + ".json");
        for (var i = 0; i < 400 && !File.Exists(outbox); i++) await Task.Delay(10);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(outbox));
        var id = json.RootElement.GetProperty("id").GetString()!;
        await File.WriteAllTextAsync(Path.Combine(_dir, "outbox", id + ".ack"), "");
        return json.RootElement.GetProperty("text").GetString()!;
    });

    [Fact]
    public void Nothing_armed_means_no_target()
    {
        using var bridge = Build();
        bridge.LiveTarget().ShouldBeNull();
    }

    [Fact]
    public void A_fresh_beat_is_a_live_target()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.FromSeconds(5.9));
        bridge.LiveTarget().ShouldBe(Session);
    }

    /// <summary>A session that crashed while armed must not swallow the next dictation.</summary>
    [Fact]
    public void A_stale_beat_is_no_target()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.FromSeconds(6.1));
        bridge.LiveTarget().ShouldBeNull();
    }

    /// <summary>The session id becomes a file name: anything but a plain id is refused.</summary>
    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_session_id_that_is_not_a_plain_id_is_no_target(string session)
    {
        using var bridge = Build();
        Arm(session, TimeSpan.Zero);
        bridge.LiveTarget().ShouldBeNull();
    }

    [Fact]
    public void A_corrupt_target_file_is_no_target()
    {
        using var bridge = Build();
        File.WriteAllText(Path.Combine(_dir, "target.json"), "{ not json");
        bridge.LiveTarget().ShouldBeNull();
    }

    [Fact]
    public async Task No_target_writes_nothing()
    {
        using var bridge = Build();

        (await bridge.TryDeliverAsync("bonjour", CancellationToken.None)).ShouldBe(ClaudeDelivery.NoTarget);

        Directory.GetFiles(Path.Combine(_dir, "outbox")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_acknowledged_dictation_is_delivered_and_cleaned_up()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.Zero);
        var module = AcknowledgeAsync(Session);

        var result = await bridge.TryDeliverAsync("lance les tests", CancellationToken.None);

        result.ShouldBe(ClaudeDelivery.Delivered);
        (await module).ShouldBe("lance les tests");
        Directory.GetFiles(Path.Combine(_dir, "outbox")).ShouldBeEmpty("outbox and ack are removed once delivered");
    }

    /// <summary>Without an ack the outbox is withdrawn, so a late module cannot submit it.</summary>
    [Fact]
    public async Task No_ack_is_not_acknowledged_and_the_outbox_is_withdrawn()
    {
        using var bridge = Build(TimeSpan.FromMilliseconds(200));
        Arm(Session, TimeSpan.Zero);

        var result = await bridge.TryDeliverAsync("bonjour", CancellationToken.None);

        result.ShouldBe(ClaudeDelivery.NotAcknowledged);
        File.Exists(Path.Combine(_dir, "outbox", Session + ".json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Status_names_the_target_only_for_a_command_in_progress()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.Zero);

        bridge.PublishStatus(DictationState.Recording, command: true);
        (await StatusAsync("listening")).ShouldBe(Session);

        bridge.PublishStatus(DictationState.Transcribing, command: true);
        (await StatusAsync("transcribing")).ShouldBe(Session);

        bridge.PublishStatus(DictationState.Recording, command: false);
        (await StatusAsync("listening")).ShouldBeNull();

        bridge.PublishStatus(DictationState.Idle, command: false);
        (await StatusAsync("idle")).ShouldBeNull();
    }

    /// <summary>Waits for status.json to reach <paramref name="state"/>; returns its session.</summary>
    private async Task<string?> StatusAsync(string state)
    {
        for (var i = 0; i < 200; i++)
        {
            try
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_dir, "status.json")));
                var root = json.RootElement;
                if (root.GetProperty("state").GetString() == state)
                {
                    root.GetProperty("beat").GetInt64().ShouldBe(_clock.Now.ToUnixTimeMilliseconds());
                    var session = root.GetProperty("session_id");
                    return session.ValueKind == JsonValueKind.Null ? null : session.GetString();
                }
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // Mid-replace; try again.
            }

            await Task.Delay(10);
        }

        throw new ShouldAssertException($"status.json never reached \"{state}\"");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudeBridgeTests`
Expected: build error, `ClaudeBridge` / `ClaudeDelivery` not found.

- [ ] **Step 3: Implement**

`windows/src/VoxScribe.Core/ClaudeBridge.cs`:

```csharp
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoxScribe.Abstractions;

namespace VoxScribe.Core;

/// <summary>What became of a dictation handed to the armed Claude Code session.</summary>
public enum ClaudeDelivery
{
    /// <summary>No live armed session: the caller takes today's window-title path.</summary>
    NoTarget,

    /// <summary>The session's module submitted it.</summary>
    Delivered,

    /// <summary>A session was armed but did not acknowledge in time. Never retried elsewhere.</summary>
    NotAcknowledged,
}

/// <summary>A request from the band's microphone button.</summary>
public enum MicAction
{
    /// <summary>Parler: start a command dictation.</summary>
    Start,

    /// <summary>Envoyer: end it and send.</summary>
    Stop,

    /// <summary>Annuler: throw it away.</summary>
    Cancel,
}

/// <summary>
/// The app side of the voxscribe plugin's band above Claude Code's prompt.
/// </summary>
/// <remarks>
/// <para>
/// Files in <see cref="DefaultDirectory"/>, nothing else: the module writes
/// <c>target.json</c> (the armed session and its beat) and <c>mic.json</c>; the app writes
/// <c>status.json</c> and <c>outbox/&lt;session&gt;.json</c>; the module answers with
/// <c>outbox/&lt;id&gt;.ack</c>.
/// </para>
/// <para>
/// Every rule lives here and not in the module, because the module has no CI. And every
/// file operation swallows IO failures: if this breaks, the command chord falls back to
/// today's window-title path and nothing else notices.
/// </para>
/// </remarks>
public sealed partial class ClaudeBridge : IDisposable
{
    /// <summary>A beat older than this is a session that died while armed.</summary>
    public static TimeSpan Stale { get; } = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AckPoll = TimeSpan.FromMilliseconds(50);

    private readonly string _directory;
    private readonly IClock _clock;
    private readonly TimeSpan _ackTimeout;
    private readonly Lock _gate = new();
    private readonly Lock _writing = new();
    private string _state = "idle";
    private bool _command;
    private long _since;
    private Timer? _beat;

    /// <param name="directory">Where the files live; <see cref="DefaultDirectory"/> in the app.</param>
    /// <param name="clock">Judges beats; the system clock by default.</param>
    /// <param name="ackTimeout">How long the module has to acknowledge; 3 s by default.</param>
    public ClaudeBridge(string directory, IClock? clock = null, TimeSpan? ackTimeout = null)
    {
        _directory = directory;
        _clock = clock ?? SystemClock.Instance;
        _ackTimeout = ackTimeout ?? TimeSpan.FromSeconds(3);
        _since = Now;
    }

    /// <summary>The folder the plugin module reads and writes.</summary>
    public static string DefaultDirectory => DataDirectory.File("claude");

    private long Now => _clock.Now.ToUnixTimeMilliseconds();

    private string Outbox => Path.Combine(_directory, "outbox");

    /// <summary>A session id becomes a file name: only a plain id is accepted.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SessionId();

    /// <summary>Creates the folders and starts the status beat.</summary>
    public void Start()
    {
        try
        {
            Directory.CreateDirectory(Outbox);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _beat = new Timer(_ => WriteStatus(), null, TimeSpan.Zero, Beat);
    }

    /// <summary>The armed session when its beat is fresh, else null.</summary>
    public string? LiveTarget()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "target.json")));
            var root = json.RootElement;
            if (!root.TryGetProperty("session_id", out var s) || s.ValueKind != JsonValueKind.String) return null;
            if (!root.TryGetProperty("beat", out var b) || !b.TryGetInt64(out var beat)) return null;

            var session = s.GetString()!;
            if (!SessionId().IsMatch(session)) return null;
            return Now - beat <= (long)Stale.TotalMilliseconds ? session : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Hands <paramref name="text"/> to the armed session and waits for its acknowledgement.
    /// Never throws.
    /// </summary>
    public async Task<ClaudeDelivery> TryDeliverAsync(string text, CancellationToken cancellationToken)
    {
        if (LiveTarget() is not { } session) return ClaudeDelivery.NoTarget;

        var id = Guid.NewGuid().ToString("N");
        var outbox = Path.Combine(Outbox, session + ".json");
        var ack = Path.Combine(Outbox, id + ".ack");

        try
        {
            Directory.CreateDirectory(Outbox);
            WriteAtomic(outbox, Json(w =>
            {
                w.WriteString("id", id);
                w.WriteString("text", text);
                w.WriteNumber("ts", Now);
            }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing reached the module, so the window-title path is still safe.
            return ClaudeDelivery.NoTarget;
        }

        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < _ackTimeout)
        {
            if (File.Exists(ack))
            {
                TryDelete(ack);
                Withdraw(outbox, id);
                return ClaudeDelivery.Delivered;
            }

            try
            {
                await Task.Delay(AckPoll, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Withdrawn so a module that wakes late cannot submit it after the user was told it failed.
        Withdraw(outbox, id);
        return ClaudeDelivery.NotAcknowledged;
    }

    /// <summary>
    /// Records the engine's state for the band. A command in progress names its session.
    /// </summary>
    /// <remarks>
    /// Called from the engine's Changed event, which fires while the microphone is starting:
    /// only fields change here, and the file is written on the pool, so a slow disk or an
    /// antivirus scan can never delay the first syllable of a dictation.
    /// </remarks>
    public void PublishStatus(DictationState state, bool command)
    {
        lock (_gate)
        {
            var name = state switch
            {
                DictationState.Recording => "listening",
                DictationState.Transcribing => "transcribing",
                _ => "idle",
            };
            if (name != _state) _since = Now;
            _state = name;
            _command = command;
            if (state == DictationState.Idle) MicSession = null;
        }

        ThreadPool.QueueUserWorkItem(_ => WriteStatus());
    }

    /// <summary>The session whose band started the dictation in progress, if one did.</summary>
    private string? MicSession { get; set; }

    /// <summary>Writes the latest state, whoever asked: queued writes therefore settle on the newest.</summary>
    private void WriteStatus()
    {
        // One writer at a time: two replacing status.json at once would fight over the temp file.
        lock (_writing)
        {
            string state;
            bool command;
            long since;
            string? mic;
            lock (_gate) (state, command, since, mic) = (_state, _command, _since, MicSession);

            var session = state != "idle" && command ? mic ?? LiveTarget() : null;
            var body = Json(w =>
            {
                w.WriteString("state", state);
                if (session is null) w.WriteNull("session_id");
                else w.WriteString("session_id", session);
                w.WriteNumber("since", since);
                w.WriteNumber("beat", Now);
            });

            try
            {
                WriteAtomic(Path.Combine(_directory, "status.json"), body);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The band then shows "not running" after the stale delay — the truth, near enough.
            }
        }
    }

    /// <summary>
    /// One JSON object, written by hand: the trim analyzer is on for src/, and reflection-based
    /// serialization of anonymous types would fail the warnings-as-errors build.
    /// </summary>
    private static string Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deletes the outbox only if it is still ours: a newer dictation may have replaced it.</summary>
    private static void Withdraw(string outbox, string id)
    {
        try
        {
            using (var json = JsonDocument.Parse(File.ReadAllText(outbox)))
            {
                if (json.RootElement.TryGetProperty("id", out var i) && i.GetString() != id) return;
            }

            File.Delete(outbox);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Already gone (the module deleted it) or locked; a stale outbox is refused by the module anyway.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The module must never read half a file.</summary>
    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    /// <inheritdoc />
    public void Dispose() => _beat?.Dispose();
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudeBridgeTests`
Expected: all PASS.

- [ ] **Step 5: Mutation-prove the guards**

One at a time, break and watch the named test fail, then restore:
- `<=` → `>=` in `LiveTarget` → `A_fresh_beat_is_a_live_target` and `A_stale_beat_is_no_target` fail.
- Remove `if (!SessionId().IsMatch(session)) return null;` → the theory fails.
- Remove the final `Withdraw(outbox, id);` → `No_ack_is_not_acknowledged_and_the_outbox_is_withdrawn` fails.

- [ ] **Step 6: Commit**

```bash
git add windows/src/VoxScribe.Core/ClaudeBridge.cs windows/tests/VoxScribe.Core.Tests/ClaudeBridgeTests.cs
git commit -m "feat: ClaudeBridge hands command dictations to an armed Claude Code session"
```

---

### Task 3: Mic requests from the band

**Files:**
- Modify: `windows/src/VoxScribe.Core/ClaudeBridge.cs`
- Test: `windows/tests/VoxScribe.Core.Tests/ClaudeBridgeTests.cs`

**Interfaces:**
- Consumes: `ClaudeBridge`, `MicAction` (Task 2)
- Produces:
  - `public static (string Session, MicAction Action, long Ts)? ParseMic(string json, long nowMs)`
  - `public event EventHandler<MicAction>? Mic` — raised from a pool thread
  - `Start()` now also watches `mic.json`

- [ ] **Step 1: Write the failing tests** (append to `ClaudeBridgeTests`)

```csharp
    [Theory]
    [InlineData("start", MicAction.Start)]
    [InlineData("stop", MicAction.Stop)]
    [InlineData("cancel", MicAction.Cancel)]
    public void A_mic_request_is_read(string action, MicAction expected)
    {
        var now = _clock.Now.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(new { session_id = Session, action, ts = now - 500 });

        ClaudeBridge.ParseMic(json, now).ShouldBe((Session, expected, now - 500));
    }

    /// <summary>A request left on disk by an earlier run must not start a recording.</summary>
    [Fact]
    public void An_old_mic_request_is_ignored()
    {
        var now = _clock.Now.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(new { session_id = Session, action = "start", ts = now - 10_001 });

        ClaudeBridge.ParseMic(json, now).ShouldBeNull();
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"session_id\":\"a/b\",\"action\":\"start\",\"ts\":0}")]
    [InlineData("{\"session_id\":\"abc\",\"action\":\"explode\",\"ts\":0}")]
    public void A_malformed_mic_request_is_ignored(string json) =>
        ClaudeBridge.ParseMic(json, 0).ShouldBeNull();

    [Fact]
    public async Task A_mic_request_written_after_start_is_raised_once()
    {
        using var bridge = new ClaudeBridge(_dir);
        var seen = new List<MicAction>();
        bridge.Mic += (_, a) => { lock (seen) seen.Add(a); };
        bridge.Start();

        var json = JsonSerializer.Serialize(new { session_id = Session, action = "start", ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
        File.WriteAllText(Path.Combine(_dir, "mic.json"), json);
        File.WriteAllText(Path.Combine(_dir, "mic.json"), json); // a second write of the same request

        for (var i = 0; i < 300 && seen.Count == 0; i++) await Task.Delay(10);
        await Task.Delay(300);
        lock (seen) seen.ShouldBe([MicAction.Start]);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudeBridgeTests`
Expected: build error, `ParseMic` / `Mic` not found.

- [ ] **Step 3: Implement** (add to `ClaudeBridge`)

Fields, beside the others:

```csharp
    private static readonly TimeSpan MicMaxAge = TimeSpan.FromSeconds(10);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pickUp;
    private long _lastMic;
```

Event and parser:

```csharp
    /// <summary>The band's microphone button. Raised on a pool thread.</summary>
    public event EventHandler<MicAction>? Mic;

    /// <summary>A mic request, or null when malformed, for an unusable session, or older than 10 s.</summary>
    public static (string Session, MicAction Action, long Ts)? ParseMic(string json, long nowMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("session_id", out var s) || s.GetString() is not { } session) return null;
            if (!SessionId().IsMatch(session)) return null;
            if (!root.TryGetProperty("ts", out var t) || !t.TryGetInt64(out var ts)) return null;
            if (nowMs - ts > (long)MicMaxAge.TotalMilliseconds) return null;

            MicAction? action = root.TryGetProperty("action", out var a) ? a.GetString() switch
            {
                "start" => MicAction.Start,
                "stop" => MicAction.Stop,
                "cancel" => MicAction.Cancel,
                _ => null,
            } : null;

            return action is { } act ? (session, act, ts) : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
```

In `Start()`, after `Directory.CreateDirectory(Outbox);` succeeds and before the timer line:

```csharp
        _watcher = new FileSystemWatcher(_directory, "mic.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        _watcher.Created += OnMicFile;
        _watcher.Changed += OnMicFile;
        _watcher.Renamed += OnMicFile;
        _watcher.EnableRaisingEvents = true;
```

Handlers (same debounce as `ReadAloud`):

```csharp
    private void OnMicFile(object? sender, FileSystemEventArgs e)
    {
        // One write raises several events: each restarts a short wait, only the last reads.
        var pickUp = new CancellationTokenSource();
        Interlocked.Exchange(ref _pickUp, pickUp)?.Cancel();
        _ = PickUpMicAsync(e.FullPath, pickUp.Token);
    }

    private async Task PickUpMicAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            if (ParseMic(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), Now) is not var (session, action, ts))
                return;

            lock (_gate)
            {
                // A late Changed (antivirus, indexer) re-raises the same request.
                if (ts == _lastMic) return;
                _lastMic = ts;
                if (action == MicAction.Start) MicSession = session;
            }

            Mic?.Invoke(this, action);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            // Replaced by a newer event, or the module still holds the file: its next write retries.
        }
    }
```

`Dispose()` becomes:

```csharp
    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _beat?.Dispose();
        _pickUp?.Cancel();
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudeBridgeTests`
Expected: all PASS.

- [ ] **Step 5: Mutation-prove**

- Remove `if (ts == _lastMic) return;` → `A_mic_request_written_after_start_is_raised_once` fails (two starts).
- Remove the `MicMaxAge` check → `An_old_mic_request_is_ignored` fails.

- [ ] **Step 6: Commit**

```bash
git add windows/src/VoxScribe.Core/ClaudeBridge.cs windows/tests/VoxScribe.Core.Tests/ClaudeBridgeTests.cs
git commit -m "feat: ClaudeBridge reads the band's microphone requests"
```

---

### Task 4: Engine — deliver to Claude first, click-to-talk entry points

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs` (`SendCommandAsync` at ~699; new members after `TogglePushToTalk` at ~294)
- Test: `windows/tests/VoxScribe.Core.Tests/ClaudeDeliveryTests.cs`

**Interfaces:**
- Consumes: `ClaudeDelivery` (Task 2)
- Produces:
  - `public Func<string, CancellationToken, Task<ClaudeDelivery>>? DeliverToClaude { get; set; }`
  - `public void BeginCommand()`
  - `public void EndUtterance()`

- [ ] **Step 1: Write the failing tests**

`windows/tests/VoxScribe.Core.Tests/ClaudeDeliveryTests.cs`:

```csharp
using VoxScribe.Core;
using VoxScribe.Testing;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// A command dictation goes to the armed Claude Code session first; the window-title path is
/// taken only when no session is armed.
/// </summary>
public sealed class ClaudeDeliveryTests
{
    private static (DictationEngine Engine, FakeHotkeySource Command, RecordingTextInjector Injector,
        FakeFocusAnchor Anchor, List<string> Delivered) Build(ClaudeDelivery outcome)
    {
        var command = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);
        anchor.WindowTitles.Add("Claude");
        var delivered = new List<string>();

        var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), new FakeHotkeySource(), new FakeTranscriber("lance les tests"), injector,
            () => [], new FakeClock(), focusAnchor: anchor, commandHotkey: command)
        {
            DeliverToClaude = (text, _) =>
            {
                delivered.Add(text);
                return Task.FromResult(outcome);
            },
        };

        return (engine, command, injector, anchor, delivered);
    }

    private static async Task WaitAsync(DictationEngine engine, DictationState state)
    {
        for (var i = 0; i < 20000 && engine.State != state; i++) await Task.Yield();
    }

    private static async Task DictateAsync(FakeHotkeySource hotkey, DictationEngine engine)
    {
        hotkey.Press();
        await WaitAsync(engine, DictationState.Recording);
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();
        hotkey.Release();
        await WaitAsync(engine, DictationState.Idle);
    }

    [Fact]
    public async Task Delivered_types_nothing_and_finds_no_window()
    {
        var (engine, command, injector, anchor, delivered) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;

        await DictateAsync(command, engine);

        delivered.ShouldBe(["lance les tests"]);
        injector.Injected.ShouldBeEmpty();
        injector.Enters.ShouldBe(0);
        anchor.LastFind.ShouldBeNull();
        engine.Journal.InjectedText.ShouldBeEmpty("undo must not backspace text that was never typed");
    }

    /// <summary>The armed session may still submit late: typing it too would send it twice.</summary>
    [Fact]
    public async Task Not_acknowledged_types_nothing_and_says_so()
    {
        var (engine, command, injector, anchor, _) = Build(ClaudeDelivery.NotAcknowledged);
        await using var _ = engine;

        await DictateAsync(command, engine);

        injector.Injected.ShouldBeEmpty();
        anchor.LastFind.ShouldBeNull();
        engine.Notice.ShouldContain("not sent");
    }

    [Fact]
    public async Task No_target_takes_the_window_title_path()
    {
        var (engine, command, injector, anchor, _) = Build(ClaudeDelivery.NoTarget);
        await using var _ = engine;

        await DictateAsync(command, engine);

        anchor.LastFind.ShouldBe("Claude");
        injector.Injected.ShouldBe(["lance les tests"]);
        injector.Enters.ShouldBe(1);
    }

    [Fact]
    public async Task A_throwing_delivery_types_nothing()
    {
        var (engine, command, injector, _, _) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;
        engine.DeliverToClaude = (_, _) => throw new InvalidOperationException("boom");

        await DictateAsync(command, engine);

        injector.Injected.ShouldBeEmpty();
        engine.Notice.ShouldContain("not sent");
    }

    /// <summary>Stop must stop even in toggle mode, where a key release is ignored.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_band_button_records_a_command(bool toggleMode)
    {
        var (engine, _, _, _, delivered) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;
        engine.ToggleMode = toggleMode;

        engine.BeginCommand();
        await WaitAsync(engine, DictationState.Recording);
        engine.CommandThisUtterance.ShouldBeTrue();
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();

        engine.EndUtterance();
        await WaitAsync(engine, DictationState.Idle);

        delivered.ShouldBe(["lance les tests"]);
    }

    [Fact]
    public async Task The_band_button_is_ignored_while_a_dictation_runs()
    {
        var (engine, command, _, _, _) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;

        command.Press();
        await WaitAsync(engine, DictationState.Recording);
        engine.BeginCommand();

        engine.State.ShouldBe(DictationState.Recording);
        command.Release();
        await WaitAsync(engine, DictationState.Idle);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudeDeliveryTests`
Expected: build error, `DeliverToClaude` / `BeginCommand` / `EndUtterance` not found.

- [ ] **Step 3: Implement**

In `DictationEngine.cs`, after `TogglePushToTalk()`:

```csharp
    /// <summary>
    /// Starts a command dictation from the Claude Code band's microphone button. Ignored unless
    /// idle — the engine already refuses a second start.
    /// </summary>
    /// <remarks>Never anchors: the band's own session is the target, found at release.</remarks>
    public void BeginCommand()
    {
        if (State != DictationState.Idle) return;
        _cleanThisUtterance = true;
        CommandThisUtterance = true;
        _anchorRequested = false;
        _ = BeginAsync();
    }

    /// <summary>
    /// Ends the dictation in progress as a key release would, in either mode: toggle mode ignores
    /// releases, and the band's Envoyer button must still stop. No-op unless recording.
    /// </summary>
    public void EndUtterance() => _ = EndAsync();

    /// <summary>
    /// Hands a finished command to the armed Claude Code session before the window-title path is
    /// tried. Null — no plugin bridge — leaves command mode exactly as it was.
    /// </summary>
    public Func<string, CancellationToken, Task<ClaudeDelivery>>? DeliverToClaude { get; set; }
```

At the top of `SendCommandAsync(string text)`, before `var target = …`:

```csharp
        if (DeliverToClaude is { } deliver)
        {
            ClaudeDelivery outcome;
            try
            {
                outcome = await deliver(text, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The outbox may already be written: typing as well could send it twice.
                outcome = ClaudeDelivery.NotAcknowledged;
            }

            // Not journalled when delivered: undo backspaces into the focused window, and this
            // text was submitted in Claude Code, never typed there.
            if (outcome == ClaudeDelivery.Delivered) return;
            if (outcome == ClaudeDelivery.NotAcknowledged)
            {
                ReportNotice("Claude Code did not take the command — command not sent");
                return;
            }
        }

```

The `catch (Exception)` will trip CA1031 under warnings-as-errors. If it does, add directly above the `try`: `#pragma warning disable CA1031 // any failure of the bridge must stay inside Vox-Scribe` and `#pragma warning restore CA1031` after the `catch` block.

- [ ] **Step 4: Run to verify they pass — and the old command-mode tests unmodified**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ClaudeDeliveryTests|FullyQualifiedName~CommandModeTests"`
Expected: all PASS. `git diff --stat windows/tests/VoxScribe.Core.Tests/CommandModeTests.cs` prints nothing.

- [ ] **Step 5: Mutation-prove**

- Change `if (outcome == ClaudeDelivery.Delivered) return;` to fall through → `Delivered_types_nothing_and_finds_no_window` fails.
- Make the `catch` set `ClaudeDelivery.NoTarget` → `A_throwing_delivery_types_nothing` fails.
- Make `EndUtterance` call `OnReleased(this, EventArgs.Empty)` → the `toggleMode: true` case hangs and fails.

- [ ] **Step 6: Commit**

```bash
git add windows/src/VoxScribe.Core/DictationEngine.cs windows/tests/VoxScribe.Core.Tests/ClaudeDeliveryTests.cs
git commit -m "feat: command dictations go to the armed Claude Code session first"
```

---

### Task 5: Composition — build and wire the bridge

**Files:**
- Modify: `windows/src/VoxScribe.App/Composition.cs`

**Interfaces:**
- Consumes: `ClaudeBridge`, `MicAction` (Tasks 2–3), `DeliverToClaude`, `BeginCommand`, `EndUtterance`, `CancelAsync` (Task 4)
- Produces: `Composition.Claude` (`ClaudeBridge?`)

- [ ] **Step 1: Wire it**

Constructor gains a last parameter `ClaudeBridge? claude` stored as:

```csharp
    /// <summary>The Claude Code band's bridge, or null when no platform layer is available.</summary>
    public ClaudeBridge? Claude { get; }
```

In `Create()`, declare `ClaudeBridge? claude = null;` beside `ReadAloud? readAloud = null;`, and after the `readAloud` block:

```csharp
            // The voxscribe plugin's band: an armed Claude Code session takes the command chord's
            // text, and its microphone button drives the same command dictation. Without the
            // plugin no session is ever armed and command mode is exactly what it was.
            claude = new ClaudeBridge(ClaudeBridge.DefaultDirectory);
            var bridge = claude;
            engine.DeliverToClaude = bridge.TryDeliverAsync;
            var shown = engine.State;
            engine.Changed += (_, _) =>
            {
                // Changed fires at buffer rate for the meter; the band only cares about state.
                if (live.State == shown) return;
                shown = live.State;
                bridge.PublishStatus(shown, live.CommandThisUtterance);
            };
            bridge.Mic += (_, action) =>
            {
                switch (action)
                {
                    case MicAction.Start: live.BeginCommand(); break;
                    case MicAction.Stop: live.EndUtterance(); break;
                    case MicAction.Cancel: _ = live.CancelAsync(); break;
                }
            };
            bridge.Start();
```

Return `new Composition(settings, dictionary, transcripts, engine, available, injector, readAloud, claude)`. In `DisposeAsync`, add `Claude?.Dispose();` beside `ReadAloud?.Dispose();`.

- [ ] **Step 2: Build with the CI gate**

Run: `cd windows && dotnet build VoxScribe.sln --no-incremental -warnaserror && dotnet format VoxScribe.sln --verify-no-changes && dotnet test VoxScribe.CrossPlatform.slnf`
Expected: build succeeds with 0 warnings; all tests PASS.

- [ ] **Step 3: Smoke-check by hand**

Run the app (`run` skill or `dotnet run --project src/VoxScribe.App`). `%LOCALAPPDATA%\VoxScribe\claude\status.json` exists, `state` is `idle`, `beat` refreshes every ~2 s. Command chord with no `target.json`: types into the "Claude" window as before.

- [ ] **Step 4: Commit**

```bash
git add windows/src/VoxScribe.App/Composition.cs
git commit -m "feat: wire the Claude Code bridge into the app"
```

---

### Task 6: ClaudePlugin — install, uninstall, state

**Files:**
- Create: `windows/src/VoxScribe.Core/ClaudePlugin.cs`
- Test: `windows/tests/VoxScribe.Core.Tests/ClaudePluginTests.cs`

**Interfaces:**
- Produces (`public static class ClaudePlugin`):
  - `const string Id = "voxscribe@vox-scribe"`, `const string TerminalFlagLine`
  - `delegate Task<(int ExitCode, string Output)> Runner(IReadOnlyList<string> args, CancellationToken cancellationToken)`
  - `static Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)` — the real `claude` CLI
  - `static bool? IsInstalled(string listJson)`
  - `static Task<bool?> CheckAsync(Runner run, CancellationToken ct)` — null when `claude` cannot be run
  - `static Task<string?> InstallAsync(Runner run, CancellationToken ct)` / `UninstallAsync` — null on success, else the CLI's message

- [ ] **Step 1: Write the failing tests**

`windows/tests/VoxScribe.Core.Tests/ClaudePluginTests.cs`:

```csharp
using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The Settings button drives the claude CLI; these pin what it runs and how it reads the answer.</summary>
public sealed class ClaudePluginTests
{
    private sealed class FakeCli
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, (int, string)> Answers { get; } = [];

        public Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> args, CancellationToken _)
        {
            var line = string.Join(' ', args);
            Calls.Add(line);
            return Task.FromResult(Answers.TryGetValue(line, out var a) ? a : (0, ""));
        }
    }

    [Fact]
    public void Installed_is_read_from_the_plugin_list()
    {
        ClaudePlugin.IsInstalled("""[{"id":"caveman@caveman"},{"id":"voxscribe@vox-scribe","enabled":true}]""").ShouldBe(true);
        ClaudePlugin.IsInstalled("""[{"id":"caveman@caveman"}]""").ShouldBe(false);
        ClaudePlugin.IsInstalled("not json").ShouldBeNull();
    }

    [Fact]
    public async Task Install_adds_the_marketplace_then_installs()
    {
        var cli = new FakeCli();

        (await ClaudePlugin.InstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();

        cli.Calls.ShouldBe([
            "plugin marketplace add Arylmera/vox-scribe",
            "plugin install voxscribe@vox-scribe --scope user",
        ]);
    }

    /// <summary>A marketplace already added makes "add" fail; the install must still run.</summary>
    [Fact]
    public async Task Install_carries_on_when_the_marketplace_exists()
    {
        var cli = new FakeCli();
        cli.Answers["plugin marketplace add Arylmera/vox-scribe"] = (1, "already exists");

        (await ClaudePlugin.InstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();
        cli.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_failed_install_returns_the_cli_message()
    {
        var cli = new FakeCli();
        cli.Answers["plugin install voxscribe@vox-scribe --scope user"] = (1, "network down");

        (await ClaudePlugin.InstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBe("network down");
    }

    [Fact]
    public async Task Uninstall_removes_only_the_plugin()
    {
        var cli = new FakeCli();

        (await ClaudePlugin.UninstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();
        cli.Calls.ShouldBe(["plugin uninstall voxscribe@vox-scribe"]);
    }

    [Fact]
    public async Task A_missing_cli_is_unknown_state()
    {
        var cli = new FakeCli();
        cli.Answers["plugin list --json"] = (ClaudePlugin.NotFound, "claude not found");

        (await ClaudePlugin.CheckAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void The_terminal_line_is_the_exact_env_entry() =>
        ClaudePlugin.TerminalFlagLine.ShouldBe("\"CLAUDE_CODE_ENABLE_FUNCTION_HOOKS\": \"1\"");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudePluginTests`
Expected: build error, `ClaudePlugin` not found.

- [ ] **Step 3: Implement**

`windows/src/VoxScribe.Core/ClaudePlugin.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VoxScribe.Core;

/// <summary>
/// Installs and uninstalls the voxscribe plugin through the <c>claude</c> CLI.
/// </summary>
/// <remarks>
/// Only the CLI: Vox-Scribe never edits Claude Code's own files, <c>settings.json</c>
/// included. The terminal flag the band needs is shown for the user to add themselves.
/// </remarks>
public static class ClaudePlugin
{
    /// <summary>The plugin as the CLI names it.</summary>
    public const string Id = "voxscribe@vox-scribe";

    /// <summary>The GitHub marketplace holding it.</summary>
    public const string Marketplace = "Arylmera/vox-scribe";

    /// <summary>The line to add under <c>env</c> in <c>~/.claude/settings.json</c> for the band to draw in a terminal.</summary>
    public const string TerminalFlagLine = "\"CLAUDE_CODE_ENABLE_FUNCTION_HOOKS\": \"1\"";

    /// <summary>The exit code <see cref="RunAsync"/> reports when <c>claude</c> cannot be started.</summary>
    public const int NotFound = -1;

    /// <summary>Runs the CLI with <paramref name="args"/>; behind a delegate so tests never start a process.</summary>
    public delegate Task<(int ExitCode, string Output)> Runner(IReadOnlyList<string> args, CancellationToken cancellationToken);

    /// <summary>Whether the plugin is in <c>claude plugin list --json</c>'s output; null when it is unreadable.</summary>
    public static bool? IsInstalled(string listJson)
    {
        try
        {
            using var json = JsonDocument.Parse(listJson);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;
            return json.RootElement.EnumerateArray().Any(p =>
                p.TryGetProperty("id", out var id) && id.GetString() == Id);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Installed or not; null when <c>claude</c> cannot be run or answers nonsense.</summary>
    public static async Task<bool?> CheckAsync(Runner run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var (code, output) = await run(["plugin", "list", "--json"], cancellationToken).ConfigureAwait(false);
        return code == 0 ? IsInstalled(output) : null;
    }

    /// <summary>Adds the marketplace (an existing one makes this fail harmlessly), then installs. Null on success.</summary>
    public static async Task<string?> InstallAsync(Runner run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var (added, addOutput) = await run(["plugin", "marketplace", "add", Marketplace], cancellationToken).ConfigureAwait(false);
        if (added == NotFound) return addOutput;

        var (code, output) = await run(["plugin", "install", Id, "--scope", "user"], cancellationToken).ConfigureAwait(false);
        return code == 0 ? null : output;
    }

    /// <summary>Removes the plugin, leaving the marketplace. Null on success.</summary>
    public static async Task<string?> UninstallAsync(Runner run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var (code, output) = await run(["plugin", "uninstall", Id], cancellationToken).ConfigureAwait(false);
        return code == 0 ? null : output;
    }

    /// <summary>
    /// The real CLI: <c>claude</c> (the native exe), else <c>claude.cmd</c> (an npm install). No window,
    /// two minutes at most.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var exe in new[] { "claude", "claude.cmd" })
        {
            var info = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) info.ArgumentList.Add(a);

            Process process;
            try
            {
                process = Process.Start(info)!;
            }
            catch (Win32Exception)
            {
                continue;
            }

            using (process)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    return (1, "claude did not answer within two minutes");
                }

                var text = (await stdout.ConfigureAwait(false)).Trim();
                var error = (await stderr.ConfigureAwait(false)).Trim();
                return (process.ExitCode, process.ExitCode == 0 || error.Length == 0 ? text : error);
            }
        }

        return (NotFound, "claude was not found on PATH");
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudePluginTests`
Expected: all PASS.

- [ ] **Step 5: Check the real CLI once, by hand**

`dotnet test` cannot. In a scratch `dotnet script` or a temporary test you do not commit, call `ClaudePlugin.CheckAsync(ClaudePlugin.RunAsync, default)`; on this machine it returns `true` (the plugin is installed).

- [ ] **Step 6: Commit**

```bash
git add windows/src/VoxScribe.Core/ClaudePlugin.cs windows/tests/VoxScribe.Core.Tests/ClaudePluginTests.cs
git commit -m "feat: install and uninstall the voxscribe plugin through the claude CLI"
```

---

### Task 7: Settings — the CLAUDE tab

**Files:**
- Create: `windows/src/VoxScribe.App/Views/Settings/ClaudeSection.cs`
- Modify: `windows/src/VoxScribe.App/Views/SettingsPage.cs` (enum + section map)
- Modify: `windows/src/VoxScribe.App/Views/Settings/ShortcutsSection.cs` (remove the title field and its note; update the COMMAND note)
- Modify: `windows/tests/VoxScribe.App.Tests/UiTests.cs` (the "six tabs" summary at ~362 → seven)
- Test: `windows/tests/VoxScribe.App.Tests/ClaudeSectionTests.cs`

**Interfaces:**
- Consumes: `ClaudePlugin` (Task 6)

- [ ] **Step 1: Write the failing UI tests**

`windows/tests/VoxScribe.App.Tests/ClaudeSectionTests.cs` — built directly, like `AppearanceSectionTests`, with a fake CLI so no test ever starts `claude`:

```csharp
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

public sealed class ClaudeSectionTests
{
    private static AppSettings Settings() =>
        new(Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json"));

    private static ClaudePlugin.Runner Cli(List<string> calls, bool installed) => (args, _) =>
    {
        var line = string.Join(' ', args);
        calls.Add(line);
        return Task.FromResult(line == "plugin list --json"
            ? (0, installed ? """[{"id":"voxscribe@vox-scribe"}]""" : "[]")
            : (0, ""));
    };

    private static List<string?> Texts(Control section) =>
        [.. section.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)];

    [AvaloniaFact]
    public void The_section_holds_the_command_window_title_and_the_terminal_line()
    {
        var section = ClaudeSection.Build(Settings(), _ => { }, Cli([], installed: false));

        Texts(section).ShouldContain("COMMAND WINDOW TITLE CONTAINS");
        Texts(section).ShouldContain(ClaudePlugin.TerminalFlagLine);
    }

    [AvaloniaFact]
    public async Task An_installed_plugin_offers_uninstall_and_runs_it()
    {
        var calls = new List<string>();
        var section = ClaudeSection.Build(Settings(), _ => { }, Cli(calls, installed: true));
        new Window { Content = section }.Show(); // attaching is what reads the plugin's state
        var button = section.GetLogicalDescendants().OfType<TransportKey>()
            .Single(b => b.Content as string is "INSTALL" or "UNINSTALL" || b.Content is null);
        for (var i = 0; i < 100 && button.Content is null; i++) await Task.Delay(10);

        button.Content.ShouldBe("UNINSTALL");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 100 && !calls.Contains("plugin uninstall voxscribe@vox-scribe"); i++) await Task.Delay(10);

        calls.ShouldContain("plugin uninstall voxscribe@vox-scribe");
    }
}
```

(`Panels.Labelled` renders its label as a `TextBlock`; if it renders it differently, match what `AppearanceSectionTests` asserts on.)

- [ ] **Step 2: Run to verify it fails**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~ClaudeSectionTests`
Expected: build error, `ClaudeSection` not found.

- [ ] **Step 3: Implement**

`SettingsPage.cs` — add to the enum after `Shortcuts`:

```csharp
    /// <summary>The Claude Code plugin and the command window.</summary>
    Claude,
```

and in the constructor's section map, after the Shortcuts line:

```csharp
        _sections[SettingsTab.Claude] = ClaudeSection.Build(_settings, Save, ClaudePlugin.RunAsync);
```

Update the class summary "six tabs" → "seven tabs" in `SettingsPage.cs` and `UiTests.cs`.

`ShortcutsSection.cs` — delete the `commandTitle` local, the `COMMAND WINDOW TITLE CONTAINS` row and its note; replace the COMMAND note with:

```csharp
                Panels.Note("Dictate at Claude Code instead of at a text field: the transcript "
                    + "(tidied when a cleanup model is set) goes to the Claude Code session armed in "
                    + "its band, or else to the window set under CLAUDE, and is submitted. Escape unbinds."),
```

`windows/src/VoxScribe.App/Views/Settings/ClaudeSection.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The voxscribe plugin for Claude Code, and where command dictations go without it.</summary>
internal static class ClaudeSection
{
    /// <summary>Builds the section.</summary>
    /// <param name="settings">User preferences.</param>
    /// <param name="save">Persists a change.</param>
    /// <param name="run">The claude CLI; <see cref="ClaudePlugin.RunAsync"/> in the app, a fake in tests.</param>
    public static Control Build(AppSettings settings, Action<SettingsData> save, ClaudePlugin.Runner run)
    {
        var commandTitle = Panels.Field("Claude",
            settings.Data.CommandWindowTitle,
            v => save(settings.Data with { CommandWindowTitle = v ?? "Claude" }));

        return Panels.Section("CLAUDE CODE", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                PluginBlock(run),
                Panels.Note("The plugin adds /parle and /say, and a band above Claude Code's prompt: "
                    + "arm a session as the command shortcut's target, talk to it with a click, "
                    + "or have its last reply read aloud. It applies to new sessions, or after /reload-plugins."),
                TerminalBlock(),
                Panels.Labelled("COMMAND WINDOW TITLE CONTAINS", commandTitle),
                Panels.Note("When no session is armed, the command shortcut types into the first "
                    + "visible window whose title contains this text. \"Claude\" matches the desktop "
                    + "app and a terminal tab running Claude Code. If no window matches, nothing is "
                    + "typed anywhere and the pill says so."),
            },
        });
    }

    /// <summary>The plugin's state and the one button that flips it.</summary>
    private static Control PluginBlock(ClaudePlugin.Runner run)
    {
        var status = Panels.Note("Checking…");
        status.VerticalAlignment = VerticalAlignment.Center;
        var button = new TransportKey { IsEnabled = false };
        bool? installed = null;

        async Task RefreshAsync()
        {
            installed = await ClaudePlugin.CheckAsync(run, CancellationToken.None);
            status.Text = installed switch
            {
                true => "Installed for Claude Code.",
                false => "Not installed.",
                null => "The claude command was not found, so the plugin cannot be managed from here.",
            };
            button.Content = installed == true ? "UNINSTALL" : "INSTALL";
            button.IsEnabled = installed is not null;
        }

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            status.Text = installed == true ? "Uninstalling…" : "Installing…";
            var error = installed == true
                ? await ClaudePlugin.UninstallAsync(run, CancellationToken.None)
                : await ClaudePlugin.InstallAsync(run, CancellationToken.None);
            await RefreshAsync();
            if (error is not null) status.Text = $"{status.Text} {error}";
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { button, status },
        };
        // Read whenever the tab is shown — the plugin may be changed from a terminal — and never
        // at build: SettingsPage builds every tab, and the UI tests build SettingsPage.
        row.AttachedToVisualTree += (_, _) => _ = RefreshAsync();
        return Panels.Labelled("PLUGIN", row);
    }

    /// <summary>The terminal flag, explained and copied — never written by Vox-Scribe.</summary>
    private static Control TerminalBlock()
    {
        var copy = new TransportKey { Content = "COPY THE LINE" };
        var line = Panels.Note(ClaudePlugin.TerminalFlagLine);
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(ClaudePlugin.TerminalFlagLine);
        };

        return Panels.Labelled("BAND IN A TERMINAL", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Note("The desktop app draws the band as is. A terminal draws it only with this "
                    + "line in the \"env\" block of ~/.claude/settings.json, then a restart of Claude Code. "
                    + "It turns on module code for every plugin, not just this one, so Vox-Scribe leaves "
                    + "the file to you."),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = Tokens.Space.Snug,
                    Children = { copy, line },
                },
            },
        });
    }
}
```

- [ ] **Step 4: Run the gate and the full suite**

Run: `cd windows && dotnet build VoxScribe.sln --no-incremental -warnaserror && dotnet format VoxScribe.sln --verify-no-changes && dotnet test VoxScribe.CrossPlatform.slnf`
Expected: 0 warnings; all PASS, including `ContrastTests`/`DoctrineTests` (no literal colours added).

- [ ] **Step 5: Look at it**

Run the app, open Settings → CLAUDE in each of the five themes, light and dark. INSTALL/UNINSTALL round-trips (`claude plugin list` agrees). COPY THE LINE puts the exact line on the clipboard. Shortcuts no longer shows the title field.

- [ ] **Step 6: Commit**

```bash
git add windows/src/VoxScribe.App/Views windows/tests/VoxScribe.App.Tests/UiTests.cs windows/tests/VoxScribe.App.Tests/ClaudeSectionTests.cs
git commit -m "feat: Claude tab in Settings installs the plugin and holds the command window"
```

---

### Task 8: The band — plugin module

Adjust this task to the spike's findings (Task 1) before starting: hooks.json layout (Q1), busy behaviour (Q3), Parle/Say route (Q4).

**Files:**
- Create: `claude-plugin/hooks/register.tsx`
- Modify: `claude-plugin/hooks/hooks.json`, `claude-plugin/.claude-plugin/plugin.json` (`"version": "1.1.0"`, description mentions the band)
- Create: `claude-plugin/README.md`
- Modify: `AGENTS.md` (the "Claude Code plugin is almost empty on purpose" paragraph), the spec (§4 deviations)

- [ ] **Step 1: Write the module**

`claude-plugin/hooks/register.tsx`:

```tsx
import type { EngineInterface, Register, RenderInput } from 'claude-code'

// The Vox-Scribe band above Claude Code's prompt. Deliberately dumb: it draws, writes intent
// files and submits what the app leaves in this session's outbox. Every rule — which session is
// live, acknowledgement, fallback — lives in the app (ClaudeBridge.cs), where it is tested.
// Every handler catches: if this breaks, the band goes away and Claude Code does not notice.

const POLL_MS = 300
const BEAT_MS = 2000
const STALE_MS = 6000
const MAX_TEXT = 32_000
const MAX_FILE = 64_000
const TRIP_AFTER = 20

type Status = { state: 'idle' | 'listening' | 'transcribing'; session_id: string | null; since: number; beat: number }

let dir: string | null = null
let sid: string | null = null
let armed = false
let lastBeat = 0
let status: Status | null = null
let lastHandled: string | null = null
let errors = 0
let tripped = false
let timerStarted = false

async function base($: EngineInterface): Promise<string> {
  if (dir) return dir
  const local = await $.env.get('LOCALAPPDATA')
  if (!local) throw new Error('LOCALAPPDATA is not set')
  return (dir = `${local}\\VoxScribe\\claude`)
}

async function me($: EngineInterface): Promise<string> {
  return (sid ??= await $.session.id())
}

async function readJson($: EngineInterface, path: string): Promise<any | null> {
  const text = await $.fs.read(path).catch(() => '')
  if (typeof text !== 'string' || text.length === 0 || text.length > MAX_FILE) return null
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

async function writeTarget($: EngineInterface, session: string | null) {
  lastBeat = Date.now()
  await $.fs.write(`${await base($)}\\target.json`, JSON.stringify({ session_id: session, beat: session ? lastBeat : 0 }))
}

async function writeMic($: EngineInterface, action: 'start' | 'stop' | 'cancel') {
  await $.fs.write(`${await base($)}\\mic.json`, JSON.stringify({ session_id: await me($), action, ts: Date.now() }))
}

/** The one place the prompt is touched: a fresh, sane outbox for this session, at most once. */
async function drainOutbox($: EngineInterface, d: string, session: string) {
  const o = await readJson($, `${d}\\outbox\\${session}.json`)
  if (!o || typeof o.id !== 'string' || typeof o.text !== 'string' || typeof o.ts !== 'number') return
  if (o.id === lastHandled) return
  lastHandled = o.id // before submitting: at most once, never twice
  if (Date.now() - o.ts > STALE_MS || o.text.trim() === '' || o.text.length > MAX_TEXT) return
  await $.prompt.submit({ text: o.text })
  await $.fs.write(`${d}\\outbox\\${o.id}.ack`, '')
}

async function tick($: EngineInterface) {
  if (tripped) return
  try {
    const d = await base($)
    const session = await me($)
    const before = JSON.stringify([status, armed])

    const target = await readJson($, `${d}\\target.json`)
    if (armed && target?.session_id !== session) armed = false // another session took the target
    if (armed && Date.now() - lastBeat >= BEAT_MS) await writeTarget($, session)

    const s = await readJson($, `${d}\\status.json`)
    status = s && typeof s.beat === 'number' && typeof s.state === 'string' ? s : null

    await drainOutbox($, d, session)
    if (JSON.stringify([status, armed]) !== before) $.ui.invalidate('ui.render')
    errors = 0
  } catch {
    if (++errors >= TRIP_AFTER) {
      tripped = true
      $.ui.invalidate('ui.render')
    }
  }
}

function startTimer($: EngineInterface) {
  if (timerStarted) return
  timerStarted = true
  $.clock.every(POLL_MS, () => void tick($).catch(() => undefined))
}

function press($: EngineInterface, run: () => Promise<unknown>) {
  return () => void run().then(() => tick($)).catch(() => undefined)
}

function band($: EngineInterface, e: RenderInput<'AbovePrompt'>) {
  const { Box, Text, Button } = $.ui.resolve(e) as Record<string, any>
  if (tripped) return <Text key="vox-off" dimColor>○ Vox-Scribe désactivé (erreur)</Text>

  const alive = !!status && Date.now() - status.beat <= STALE_MS
  if (!alive) return <Text key="vox-down" dimColor>○ Vox-Scribe n'est pas lancé</Text>

  const mine = status!.session_id !== null && status!.session_id === sid
  if (mine && status!.state !== 'idle') {
    const secs = Math.max(0, Math.floor((Date.now() - status!.since) / 1000))
    const label = status!.state === 'listening' ? `● Écoute…  ${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, '0')}` : '● Transcription…'
    return (
      <Box key="vox-busy" flexDirection="row" gap={1} alignItems="center">
        <Text bold>{label}</Text>
        <Button key="vox-send" label="■ Envoyer" onPress={press($, () => writeMic($, 'stop'))} />
        <Button key="vox-cancel" label="✕ Annuler" onPress={press($, () => writeMic($, 'cancel'))} />
      </Box>
    )
  }

  return (
    <Box key="vox-band" flexDirection="row" gap={1} alignItems="center">
      <Text bold={armed}>{armed ? '● Cible active' : '◎ Vox-Scribe'}</Text>
      <Button key="vox-arm" label={armed ? '🎯 Désarmer' : '🎯 Cibler'} onPress={press($, async () => {
        armed = !armed
        await writeTarget($, armed ? await me($) : null)
      })} />
      <Button key="vox-talk" label="🎙 Parler" onPress={press($, async () => {
        armed = true
        await writeTarget($, await me($))
        await writeMic($, 'start')
      })} />
      <Button key="vox-parle" label="🔊 Parle" onPress={press($, () => $.command.run({ command: 'voxscribe:parle', args: '' }))} />
      <Button key="vox-say" label="🔊 Say" onPress={press($, () => $.command.run({ command: 'voxscribe:say', args: '' }))} />
    </Box>
  )
}

export const register: Register = (on) => {
  on('session.start', async ($, e, next) => {
    // A timer started inside a request may end with it (effortless); the session start owns it.
    try {
      sid = await $.session.id()
      startTimer($)
    } catch {
      // No band this session; Claude Code carries on.
    }
    return next(e)
  })

  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    try {
      startTimer($) // a plugin reload may draw before any session start
      const { Box } = $.ui.resolve(e) as Record<string, any>
      const ours = band($, e)
      const rest = await next(e)
      return (
        <Box key="vox-col" flexDirection="column">
          {ours}
          {rest}
        </Box>
      )
    } catch {
      return next(e)
    }
  })
}
```

If spike Q3 showed `$.prompt.submit` fails while busy, guard `drainOutbox` with the last-seen `e.props.isWorking` (store it in a module variable from `ui.render`) and return early while busy — the app's 3 s ack timeout then reports "not taken"; record that limit in the README. If spike Q3b showed `submit` resolving only when the queued message sends, change `drainOutbox`'s last two lines to start the submit, write the ack, then await: `const sent = $.prompt.submit({ text: o.text }); await $.fs.write(`${d}\outbox\${o.id}.ack`, ''); await sent` — `lastHandled` keeps it at most once.

- [ ] **Step 2: Declare it**

`claude-plugin/hooks/hooks.json` — the layout the spike proved for Q1, i.e. by default:

```json
{
  "modules": ["./register.tsx"],
  "hooks": {
    "UserPromptExpansion": [
      {
        "matcher": "parle|say|voxscribe:parle|voxscribe:say",
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"${CLAUDE_PLUGIN_ROOT}/hooks/speak.ps1\"",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

`plugin.json`: `"version": "1.1.0"`, description `"/parle and /say, and a band above the prompt: arm a session for Vox-Scribe's command shortcut, talk to it with a click."`

- [ ] **Step 3: README**

`claude-plugin/README.md` (English):

```markdown
# voxscribe — Claude Code plugin

Companion to the Vox-Scribe app (Windows). The app does the work; this plugin only hands it requests.

- `/parle`, `/say` — read the previous reply aloud (French, English).
- A band above the prompt:
  - **🎯 Cibler** arms this session: Vox-Scribe's command shortcut, pressed from any window,
    sends its dictation here. One session is armed at a time; the last one armed wins.
  - **🎙 Parler** dictates to this session with a click; **■ Envoyer** sends, **✕ Annuler** drops it.
  - **🔊 Parle / Say** — the same as typing the commands.

## The band in a terminal

The desktop app draws the band as is. A terminal draws it only with this in `~/.claude/settings.json`,
then a restart:

    "env": { "CLAUDE_CODE_ENABLE_FUNCTION_HOOKS": "1" }

This turns on module code for every installed plugin, not just this one. Vox-Scribe never edits the
file; Settings → CLAUDE copies the line for you. Without it, `/parle` and `/say` still work.

## Experimental

The band uses Claude Code's undocumented plugin-module API and may break with a Claude Code update.
If it does, the band disappears; the commands and Vox-Scribe's own shortcuts keep working.
In the desktop app's split view the band draws only in the left pane.
```

- [ ] **Step 4: Record the design in AGENTS.md and the spec**

AGENTS.md — replace the "The Claude Code plugin is almost empty on purpose" paragraph's body so it also says: `claude-plugin/hooks/register.tsx` draws a band above the prompt (experimental, undocumented module API); it only writes `target.json`/`mic.json` and submits `outbox/<session>.json` in `%LOCALAPPDATA%\VoxScribe\claude`; every rule is in `ClaudeBridge`; an armed session overrides only the command chord, and with none armed command mode is unchanged; `NotAcknowledged` never falls back to typing.

Spec §4 — replace "virtual hotkey source" with the `BeginCommand`/`EndUtterance` methods and why (toggle mode ignores releases); replace `Delivered → Journal.Record` with "not journalled: undo would backspace into the focused window".

- [ ] **Step 5: End-to-end by hand** (spec §7 — no CI can see this)

With Vox-Scribe running from this branch and the plugin loaded (`/reload-plugins`):

1. Band shows `◎ Vox-Scribe`. Quit the app → within ~6 s `○ Vox-Scribe n'est pas lancé`. Restart it.
2. Session A: 🎯 Cibler → `● Cible active`. Session B: 🎯 Cibler → A falls back to `◎ Vox-Scribe` within a second.
3. Arm A, focus a browser, press the command chord, say a sentence → it is submitted in A; the browser got nothing.
4. Close A's Claude Code. Wait 7 s. Command chord → typed into the "Claude" window as before.
5. 🎙 Parler → `● Écoute… 0:0x` → ■ Envoyer → submitted. Again → ✕ Annuler → nothing sent.
6. Toggle mode on in Settings: repeat 5 — Envoyer still stops.
7. 🔊 Parle and 🔊 Say read the last reply.
8. Typed `/parle` still works.
9. Raw and cleanup chords type into the focused field exactly as before.

Note anything that differs in the spec's §1 findings table.

- [ ] **Step 6: Commit**

```bash
git add claude-plugin AGENTS.md docs/superpowers/specs/2026-10-10-claude-panel-design.md
git commit -m "feat: Vox-Scribe band above Claude Code's prompt"
```

---

## Self-review

- **Spec coverage:** §1 spike → Task 1. §2 module guarantees → Task 8 (catch-all, bounded reads, no process.run, outbox-only prompt, circuit breaker, no command interception). App guarantees → Task 4 (NoTarget path untouched, existing tests unmodified). Band states → Task 8. §3 protocol → Tasks 2–3 (files, beats, stale, ack, withdraw), Task 8 (module side). §4 → Tasks 4–5 (with the two documented deviations). §5 Settings → Tasks 6–7 (never touches settings.json; copy line). §6 errors → Tasks 2–8 tests and the by-hand list. §7 testing → each task plus Task 8 step 5.
- **Types:** `ClaudeDelivery`, `MicAction`, `ClaudeBridge.TryDeliverAsync(string, CancellationToken)` matches `DeliverToClaude`'s `Func<string, CancellationToken, Task<ClaudeDelivery>>`; `ClaudePlugin.Runner` matches `RunAsync`'s signature and the tests' `FakeCli.RunAsync`.
- **Known soft spots:** the module's API calls are copied from effortless and only proven by Task 1; `Panels.Note`/`Panels.Field`/`TransportKey` are used exactly as `GeneralSection`/`ShortcutsSection` use them.
