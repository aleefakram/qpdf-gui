# Bulk Folder Decryption — Design

Status: approved
Scope: QPdfDecryptor GUI only (no changes to the qpdf C++ tree)

## Problem

Users receive folders of password-protected PDFs (typically from auditors) and must decrypt
many files at once. Passwords either match across all files or differ per file. Today the GUI
handles one file per run.

## Resolved decisions

| Decision | Choice |
|---|---|
| Show matched passwords in results | Yes, masked by default with reveal toggle; CSV export excludes passwords |
| Files that need no password | Copied to the output folder so it contains a complete clean set |
| Concurrency | v1 sequential; bounded parallel probing deferred to v2 |
| Recursive subfolders | Included in v1 via an "Include subfolders" checkbox |

## Verified qpdf behavior (bundled qpdf.exe)

`qpdf --requires-password --password-file=- <file>` reads the candidate password from stdin
and exits with:

| Exit code | Stderr | Meaning |
|---|---|---|
| 3 | — | File is encrypted and this password opens it |
| 0 | — | File is encrypted, password is wrong |
| 2 | empty | File is not encrypted |
| 2 | non-empty | Error (missing, locked, or corrupt file) |

A probe costs ~50 ms (header parse + key derivation) versus a full decrypt attempt, so
brute-force search uses probes and runs one real decrypt per file, after a password wins.

## UX

The existing single window gains a mode pair at the top: **Single file** (today's flow,
unchanged) and **Folder of files**.

Folder mode layout, in reading order:

1. Folder picker with a live PDF count preview ("14 PDFs found") and an
   "Include subfolders" checkbox.
2. Password section as a radio pair:
   - **One password for all files** — today's masked entry box.
   - **Try a list of passwords (.txt, one per line)** — file picker.
3. Output folder picker, defaulting to `<input folder>\decrypted`.
4. "Open folder when finished" checkbox.
5. Primary button labeled "Decrypt N PDFs"; Cancel keeps its current semantics.
6. Progress bar shows overall file count; phase text below shows either
   "Trying password k of N…" for the current file or its decrypt percentage.
7. On completion the status panel becomes a results summary
   (`11 decrypted · 1 needed no password · 2 failed`) with an expandable per-file list:
   status icon, file name, outcome, and matched password masked behind the existing
   reveal-toggle pattern. An "Export report (CSV)" button writes outcomes without passwords.

Drag-and-drop works in folder mode by accepting a single dropped folder.

## Password strategy

One algorithm covers both scenarios without asking the user which applies:

Per file, probe candidates in order until one opens the file, then decrypt once with the
winner. Candidate ordering is adaptive (most-recently-successful first):

- Every password that succeeds anywhere moves to the front of the candidate list.
- Single-password-for-all case: the first file performs a full scan; every later file hits
  on the first probe.
- All-different case: degrades to a per-file scan, which is unavoidable given the input.

The empty password is always tried implicitly first (qpdf default), which also resolves
owner-restriction-only files with zero candidates.

Performance envelope: ~60 ms per probe means a 500-password list against all-unique
passwords costs roughly 30 s per file in the worst case. Visible "trying password k of N"
progress keeps this acceptable. If it ever is not, the escape hatch is libqpdf P/Invoke for
in-process probing (out of scope for v1).

## Core API (`QPdfDecryptor.Core`)

```csharp
public sealed record BulkDecryptRequest(
    string QpdfPath,
    IReadOnlyList<string> InputPaths,
    IReadOnlyList<string> PasswordCandidates, // normalized: single mode == list of one
    string OutputDirectory,
    ConflictPolicy ConflictPolicy,
    string? InputRoot = null); // recursive mode: outputs preserve subpaths below this root

public enum ConflictPolicy { Overwrite, AutoRename, Skip }

public enum FileOutcome
{
    Decrypted,           // password found; MatchedPassword set
    DecryptedNoPassword, // owner restrictions only; empty password worked
    NotEncrypted,        // copied through unchanged
    Skipped,             // output existed and policy was Skip
    NoPasswordMatched,   // none of the candidates worked
    Failed               // corrupt, locked, unreadable…
}

public sealed record FileResult(
    string InputPath, string? OutputPath, FileOutcome Outcome,
    string? MatchedPassword, string Message, string Details);

public sealed record BulkDecryptResult(IReadOnlyList<FileResult> Files);

public enum BulkPhase { ProbingPasswords, Decrypting, Copying }

public sealed record BulkProgress(
    int CompletedFiles, int TotalFiles, string CurrentFile,
    BulkPhase Phase, int Attempt, int AttemptCount, int? DecryptPercent);
```

`BulkDecryptService.DecryptAsync(request, IProgress<BulkProgress>, CancellationToken)`
orchestrates sequentially:

```
for each file:
    probe with empty/cached/MRU candidates, then remaining list in order
    not encrypted            -> copy through `qpdf --decrypt` (pass-through)
    password found           -> decrypt once via QpdfDecryptService
    candidates exhausted     -> NoPasswordMatched
    probe errored            -> Failed
```

Refactor: extract the process plumbing shared by decrypt and probe (spawn, stdin write,
output capture, kill-tree on cancellation, temp cleanup) into an internal
`QpdfProcessRunner`. `QpdfDecryptService` keeps its public API and behavior unchanged.

## Password list file format

| Rule | Behavior |
|---|---|
| Encoding | UTF-8; strip BOM if present |
| Line endings | `\r\n`, `\n`, and `\r` accepted |
| Whitespace | Trim line endings only; leading/trailing spaces are preserved |
| Blank lines | Skipped |
| Duplicates | Deduped, first-occurrence order kept |
| Empty result | Validation error before the run starts |

The parsed list lives in memory only. It is never written to disk, logged, or included in
details text or CSV export, and is cleared when the window closes.

## Output handling

- Default output directory `<input folder>\decrypted`, created on demand.
- Filenames preserved; recursive mode preserves relative subpaths.
- Conflicts resolved per `ConflictPolicy`: overwrite, auto-rename (`name (2).pdf`), or skip.
- The existing safety guarantee carries over: outputs are written to hidden temp files and
  moved into place only after qpdf exits successfully.

## Testing

Extend the console test runner's fake qpdf with a `--requires-password` branch that reads
stdin and consults an accept-list supplied via a sidecar file path in an environment
variable (avoids delimiter collisions in passwords). The fake appends each attempted
password to a log file so tests can assert probe ordering.

New coverage:

1. Single-candidate bulk run decrypts all files.
2. MRU ordering: the second file's first probe is the first file's winning password.
3. All-different passwords: each file finds its own match.
4. Exhausted list yields `NoPasswordMatched`, writes no output, leaves no temp files.
5. Unencrypted file is copied through and counted separately.
6. Corrupt file yields `Failed` and the batch continues.
7. Cancellation mid-search throws, prior outputs remain intact.
8. Conflict policies: overwrite, rename, skip.
9. List parser: BOM, CRLF, blank lines, duplicates, whitespace preservation.
10. Probe disambiguation: exit 2 with stderr maps to `Failed`, not `NotEncrypted`.

## Future (v2)

- Bounded parallel probing across files (probes are independent; decrypt stays sequential).
- CSV export option that includes matched passwords behind an explicit warning.
