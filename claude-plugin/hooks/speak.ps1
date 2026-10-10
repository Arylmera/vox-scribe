# /parle and /say: tell Vox-Scribe which transcript to read the last reply from.
#
# UserPromptExpansion hook. Vox-Scribe watches %LOCALAPPDATA%\VoxScribe\speak and does all
# the work (finding the reply, rewriting it for speech, speaking it); this only hands over the
# transcript path. The command is blocked, so it never costs a model turn.
# Windows PowerShell 5.1 compatible: it is the one shell every Windows machine has.

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8

$payload = (New-Object System.IO.StreamReader([Console]::OpenStandardInput(), $utf8)).ReadToEnd() | ConvertFrom-Json
$name = ("" + $payload.command_name).TrimStart('/').Split(':')[-1].ToLowerInvariant()
if ($name -notin 'parle', 'say') { exit 0 }
$fr = $name -eq 'parle'

try {
    $dir = Join-Path $env:LOCALAPPDATA 'VoxScribe\speak'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $request = @{
        command         = $name
        transcript_path = $payload.transcript_path
        session_id      = $payload.session_id
        ts              = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    } | ConvertTo-Json -Compress
    $tmp = Join-Path $dir ".request-$PID.tmp"
    [System.IO.File]::WriteAllText($tmp, $request, $utf8)
    Move-Item -Force -Path $tmp -Destination (Join-Path $dir 'request.json')  # never half-written

    if (Get-Process -Name 'VoxScribe.App' -ErrorAction SilentlyContinue) {
        $note = if ($fr) { 'Lecture de la réponse précédente.' } else { 'Reading the previous reply aloud.' }
    } else {
        $note = if ($fr) { "Vox-Scribe n'est pas lancé : rien ne sera lu." } else { 'Vox-Scribe is not running: nothing will be read.' }
    }
} catch {
    $note = "/$name failed: $($_.Exception.Message)"
}

# Block, not suppress: a suppressed prompt still ran a model turn (measured 2026-10-09).
@{ decision = 'block'; reason = $note } | ConvertTo-Json -Compress
