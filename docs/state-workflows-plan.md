# State workflow fixes

Baseline: `c4de6f0` on `main`. Implement the reviewed state-workflow design without
adding a state-machine dependency. Keep files authoritative, stable basket UIDs,
external clipboard import semantics, legacy state recovery, and independent UI work.
The implementation was initially completed without publication. The subsequent
requested release cycle is tracked below.

## Progress

- [x] Ground: trace requirements, production callers, and existing tests; reproduce
  clipboard identity/consumption and refresh scheduling failures using current Core.
- [x] Sketch: compare minimal interface shapes, screen ownership and persisted
  contracts, and record the selected signatures below.
- [x] Implement: add focused failing regressions, then replace the affected paths.
- [x] Review: inspect the combined implementation and remove superseded state.
- [x] Verify: run Core tests, build Debug and Release x64, and verify the desktop
  application against isolated storage. Revisit the design if implementation
  requires duplicated ownership or hidden caller ordering.

## Canonical shape and implementation steps

1. Clipboard identity and transfer outcomes.
   - Carry an opaque local transfer token in the same Windows DataObject as FileDrop.
     The workspace owns the local Copy/Cut lifecycle; FileTransfer owns disk effects.
   - Distinguish a failed clipboard read from a successful empty/replacement read.
     Only a matching current local token can authorize Move. Copy is repeatable;
     confirmed Cut is consumed; uncertain effects require reconciliation before retry.
   - Return structured per-item transfer effects. Derive counts and messages from
     those effects. Use direct directory moves for sources in this workspace,
     including other baskets. Redirected cross-basket paths retain the existing
     Copy/Recycle path, selected before mutation instead of retrying a failed Move.
     Preserve supported external transfer behavior.
   - Verify external Copy of identical paths, repeated Cut after source recreation,
     clipboard failures, same-folder no-op, cross-basket folder moves, partial copies,
     and failures after a destination has been created.
2. Basket rename and durable recovery.
   - Use one direct filesystem rename, including case-only changes on .NET 10.
     Skip unchanged operations and avoid a disk journal for display-only changes.
   - Retain one pending intent until the UID/directory association is established.
     Distinguish confirmed absence from unreadable disk observations. Ordinary saves
     must preserve unresolved recovery evidence; new renames cannot overwrite it.
   - Read existing v4 temporary-path journals at the recovery boundary. Do not write
     a temporary path for new renames. Preserve all supported older state migrations.
   - Verify intent/final-save failures, old journals, exact casing, ambiguous paths,
     refresh/save/restart, and accessible materials with the original UID.
   - Reject state writes and basket creation before initialization completes. After
     a startup filesystem failure, Refresh retries the full state load rather than
     adopting folders into an empty model. Verify preserved metadata before and
     after root repair; ordinary Hide/Exit saves must not erase it.
3. Refresh scheduling and watcher lifetime.
   - Store the outcome of each watch attempt before publishing diagnostics.
     Retry on show, F5, target change, or watcher Error, not ordinary Status events.
   - Coalesce work by required scope and release the queued-work marker even when
     reading or rendering fails. Keep generation rejection and deferred echo checks.
   - Verify missing root/recovery, render failures, exactly one failed start per
     attempt, second-watcher failure, stale callbacks, hide/show, and disposal.
4. Editing and deferred UI work.
   - End inline editing explicitly on Submit, independently of tree-change events.
     Keep one owner of each draft and derive editing presentation from it.
   - Validate captured basket, path, and request when deferred selection/focus runs.
     Keep independent drops independent. Preserve existing preview cancellation.
   - Verify unchanged-name Submit, success/error/cancel, stale deferred callbacks,
     target deletion during a drop, and stale preview success/error suppression.
5. Integration and verification.
   - Update architecture documentation and localized diagnostics where behavior changes.
   - Run the original reproductions and focused regressions, then the full Core suite.
   - Close the owner's running application normally before replacing its binaries.
     Build both configurations and run the existing isolated Release smoke suite.
   - Use targeted UI checks for changed behavior absent from the smoke suite; report
     any remaining verification limitation precisely.

## Delete list

- Path-set equality as clipboard operation identity and clipboard lifecycle fields
  in the disk transfer executor.
- Two-step runtime case-only rename, rollback, and new temporary-path journals.
  Existing persisted temporary-path recovery remains supported.
- Recursive watcher-start event coupling and redundant dirty flags when superseded
  by a single pending-work representation.
- Inline-editor completion through incidental tree destruction, duplicated editing
  ownership, and unguarded deferred actions in the touched flows.

## Design evidence and contracts

- Product constraints: README, docs/decisions.md, and originating issues #2, #5, #6.
- Current Core reproductions demonstrate stale Cut intent and a permanently queued
  refresh marker after DirectoryNotFoundException. Baseline: 324 Core tests pass.
- .NET 10 directly supports case-only file/directory moves; verified on this Windows
  host and in [FileSystem.cs](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.cs#L27-L42)
  / [FileSystem.Windows.cs](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs#L157-L178).
- User data, Windows FileDrop compatibility, state schema migration, literal path
  history, external Import, and supported external Move are protected contracts.

## Selected interfaces and review notes

The selected shape retains the existing owners. Two independent design reviews
compared this with cohesive ClipboardTransfers/BasketCatalog operation modules.
Both selected the existing owners: extracting those modules would require moving
the complete clipboard/target/transfer workflow or the complete tabs/state writer.
Partial extraction would expose caller ordering or duplicate ownership.

The comparison criteria were invariant ownership, caller interface size, protected
contract preservation, and deletion of redundant transitions. The selected shape
keeps all four local; the alternative adds ownership migration without an additional
production caller. No cross-judge disagreement or unresolved contract choice remains.

Caller usage remains one command per intent:

```csharp
workspace.CopyEntryToClipboard(entry, move: true);
workspace.AddFromClipboard(relativeFolder);
workspace.RenameTab(uid, name);
liveRefresh.RequestRefresh();
```

The implementation sketch uses these seams:

```csharp
PastePayload IClipboardReader.Read(); // Failed native read throws after retry.
Guid IShellService.CopyFilesToClipboard(IReadOnlyList<string> paths, bool move);
Guid? PastePayload.LocalTransferToken { get; }
// Private workspace local operation: token + Copy/CutPending/CutConsumed/CutUncertain.
// FileTransfer returns per-item source/destination/effect/error; totals are derived.
bool BasketWorkspace.Refresh();
void BasketWorkspace.ReportError(string message);
void WorkspaceRefreshCoordinator.RequestRefresh();
// Private coordinator: WatchAttempt(Target, Watching/Unavailable) and PendingWork.
// Private workspace pending rename is included by every state snapshot.
```

Read failure is expressed through the existing workspace catch rather than a new
clipboard wrapper type. The platform writes FileDrop, Preferred DropEffect and the
private identity together. Native clipboard encoding stays in Platform. The workspace
owns consumption; the transfer executor never stores clipboard state.

Watch attempt outcome is stored before error publication. Pending work is ordered
None, Presentation, Tree, ReadDisk, RestartWatchAndReadDisk. Visibility, disposal and
presence of a queued callback remain separate facts. Error text has one owner in
workspace status. F5 asks the coordinator to retry, not the window to manage watchers.

The transfer strategy is selected before mutation. Same-basket moves retain their
existing direct operation; ordinary cross-basket moves become direct. A reparse-point
ancestor on either cross-basket path keeps the prior Copy/Recycle behavior. A real
C:/D: junction probe confirmed the need for that distinction. HRESULT-dependent
retries and link-target traversal were rejected because upfront selection is smaller
and preserves the verified behavior without another transition.

The rename journal remains recovery data rather than a second basket-status field.
New intents explicitly identify source and target. Old temporary-path journals remain
readable because existing state files are a verified external contract.

## Verification results

Completed on Windows with .NET 10.0.14 before release preparation. The previously
observed normal app instance had already exited before
the builds. Verification used only owned temporary data.

| Check | Result | Evidence |
| --- | --- | --- |
| Original clipboard regressions before fixes | 3 failed, 5 passed | `.scratch/state-clipboard-red.log` |
| Clipboard/transfer affected scenarios after fixes | 65 passed | `.scratch/state-crossvolume-probe/focused-tests-upfront.log` |
| Refresh/rename/startup targeted checks | 89 passed | `.scratch/state-workflows/refresh-rename/refresh-rename-startup-green.trx` |
| Full Core Debug | 360 passed, 0 failed, 0 skipped | `.scratch/state-workflows/results/core-debug.trx` |
| Full Core Release | 360 passed, 0 failed, 0 skipped | `.scratch/state-workflows/results/core-release.trx` |
| Debug x64 app build | 0 warnings, 0 errors | `.scratch/state-build-debug.log` |
| Release x64 app build | 0 warnings, 0 errors | `.scratch/state-build-release.log` |
| Release desktop smoke | 56 checks passed; both processes stopped; disposable data removed | `.scratch/verify-pastehatch/2678cb4493da4f308f54ff48f0c2a54b/result.json` |
| C:/D: redirected basket | Public Cut/Paste preserved exact destination bytes and removed the source | `.scratch/state-crossvolume-probe/after-upfront-selection.log` |
| Convergence and formatting | No superseded symbols; `git diff --check` passed | Final source search |

The initial focused clipboard run recorded:

```text
Failed!  - Failed:     3, Passed:     5, Skipped:     0, Total:     8
```

The final complete run in each configuration recorded:

```text
Passed!  - Failed:     0, Passed:   360, Skipped:     0, Total:   360
```

The native smoke proved a nonempty private clipboard identity alongside FileDrop
and Preferred DropEffect, exact local duplication, and Import after an external
replacement with identical paths. Both binary files have SHA256
`DA2CB6AD175BC966DE5E79C6E16777F8A98B610C2424A894132DF2815BE50677`.

Unchanged-name Submit closed the actual editor while preserving TreeItem RuntimeId
`42,2821720,4,337` and all file hashes. The earlier baseline journey had been masked
by watcher replacement; its row RuntimeIds differed. The final helper explicitly
requires the same row and reacquires its editor immediately before invocation.

Independent reviews found the redirected-basket regression and failed-startup
metadata overwrite. Both were reproduced and corrected before the integrated run.
All GUID-scoped C:/D: probe directories were validated and cleaned; the smoke
restored the shared clipboard and retained snapshots/observed bytes as evidence.

Foreground keyboard focus, actual Cut gestures, async drop/context-switch timing,
and image appearance are not claimed by the offscreen smoke. Core regressions cover
the Cut lifecycle and disk effects; deferred UI actions now check their captured
context. Preview cancellation was preserved without a speculative replacement.

## Release cycle

The owner requested a separate branch, pull request, and publication after the
implementation. Use `fix/workspace-state-workflows` and the already declared,
unreleased version 1.3.1. The latest published release was 1.3.0 when checked.

- [x] Create the release branch from the implementation on main.
- [x] Run scripts/package.ps1 for Debug/Release builds, tests, and the self-contained ZIP.
- [x] Extract the ZIP and verify its executable in STA against isolated storage.

The package build passed with zero warnings/errors and 360/360 Release Core tests.
The extracted executable passed all 56 smoke checks; both owned processes stopped
and disposable data was removed. Evidence:
`.scratch/verify-pastehatch/45e38d9e42b243ce8008ec5c6d7788b4/result.json`.

`dist/PasteHatch-1.3.1-win-x64.zip` contains 536 files and is 83,124,554 bytes.
SHA256: `be37b9002f06423d77cea4870fee1bea57b1cd342e91bd4ced1fff243de34b11`.
Source/debug files, user state/settings, environment files and logs were excluded.

Publish by committing and pushing the reviewed branch, creating a pull request to
main, merging the verified head without rewriting history, and attaching this ZIP
to v1.3.1 at the matching main commit. The GitHub pull request and release are the
completion records for these remote publication steps.
