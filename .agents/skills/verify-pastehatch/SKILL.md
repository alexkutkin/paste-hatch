---
name: verify-pastehatch
description: Verify PasteHatch WinUI desktop notes, baskets and file workspace through UI Automation with actual disk and state evidence.
---

# Verify PasteHatch

## Launch

From the repo root on Windows, build both configurations, then drive one owned instance through all mapped journeys:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Configuration Debug
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
powershell -STA -ExecutionPolicy Bypass -File scripts\verify.ps1
```

To verify Release, use `powershell -STA -ExecutionPolicy Bypass -File scripts\verify.ps1 -Configuration Release`.
The script does not build automatically. It starts from an empty GUID-specific data root. Ready means its own PID's UIA window, note input and initialized state exist. No auth or seed is needed; workspace fixtures are added later as external filesystem changes.

SingleInstanceGuard uses a session-wide mutex: changing PASTEHATCH_DATA_ROOT does not permit concurrent instances. The helper refuses if PasteHatch is already running. Ask the user to exit through tray Exit; never stop their instance without authorization. Tests use the shared Windows clipboard: avoid copying during a run. The helper materializes existing clipboard formats before its first clipboard operation and restores them during cleanup; providers with unusual delayed/custom formats may not round-trip perfectly.

## Doctor

Read-only Doctor checks the owned Process object's liveness, exact binary path and usable FileTree / QuickNoteTextBox controls. doctor.json records PID, start time, binary hash and data root. Readiness checks are preconditions, not separate product test cases. A failed drive ends the run; inspect failure.txt and stderr.txt, then rerun from a fresh instance. Do not keep driving uncertain UI state.

## Drive

One script, one sequential session, one lifecycle: `scripts/verify.ps1`. The [feature index](features/README.md) owns the coverage list. ValuePattern fills the real note field; InvokePattern invokes UI commands; SelectionItemPattern and ExpandCollapsePattern navigate real tree nodes. Assertions compare persisted files, state and displayed text. Fixtures do not replace the UI action being tested.

The script fails immediately on an unmet result. Subsequent features are then unverified, even when prior checks passed. Keyboard accelerators, drag/drop, destructive dialogs, rename, basket reorder/close, settings, tray interaction, restart recovery and image appearance are outside this bounded smoke suite. UIA title/placeholder checks do not prove image decoding, so no image-preview claim is made.

## Evidence

Each invocation prints .scratch/verify-pastehatch/<GUID>/. Evidence survives teardown: actions.txt records inputs and commands; checks.txt lists completed assertions; doctor.json identifies the process/build; note/preview/workspace/baskets.ui.txt and matching .state.json capture intermediate results. observed-data/ preserves actual files and state on success or failure. stdout.txt, stderr.txt and optional failure.txt support diagnosis. result.json reports passed, processStopped, secondaryStopped, dataRemoved and check count.

Read result.json directly after cleanup; success requires all four booleans true. Inspect relevant snapshots and observed-data bytes, not just PASS output. Evidence stays local and is ignored by Git. Offscreen/no-tray flags suppress activation/tray creation, while production handlers and storage remain in use; no visible screenshot proof is provided.

## Cleanup

The finally block stops only primary/secondary Process objects returned by this run, restores process environment and saved clipboard, copies disposable storage into observed-data/, then removes only data/ inside this run's GUID directory. It checks evidence still exists. An interrupted shell may skip finally: verify doctor.json PID, start time and binary path against the live process before manually stopping it, and delete only that run's disposable data/. Keep proof artifacts.

## Helpers

The sole executable helper is scripts/verify.ps1 at repository root; both invocations are shown above. UI selectors are grounded in MainWindow.xaml; handlers are in MainWindow.xaml.cs, with domain behavior in BasketWorkspace.cs. Use explicit `verification maintain` to source-audit every mapped feature and drive the whole suite. Product fixes are outside maintenance scope.
