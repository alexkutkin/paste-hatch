# Untangle decision log

Baseline: `21e4ef8` (1.3.0). Preserve behavior, persisted formats, localization and Windows integration. Comments and test cleanup are included. Changes are prepared on a review branch; no merge or release is included.

## Decisions and problems

- Let the workspace own its navigation lifecycle.
  - Workspace persistence calls back through the window to navigation, which itself requires the workspace.
  - Test setup reproduces this construction cycle with a closure and a navigation dictionary; an unused workspace list is retained.
- Put filesystem naming and path rules beside the operations they govern.
  - Folder creation imports collision naming from transfer internals.
  - Material naming depends on a timestamp formatter for sanitization; timestamp names are sanitized twice.
  - Workspace folder resolution repeats normalization and path composition across reads and writes.
  - Basket and entry renames duplicate temporary-name moves; entry rename lacks rollback.
- Share the JSON persistence mechanism, keeping migration in the state reader.
  - Settings and workspace stores duplicate JSON configuration, forgiving reads and atomic writes.
- Share clipboard access policy and remove redundant app indirection.
  - Clipboard reads and writes repeat the same OS contention retry policy.
  - Error-dialog and icon fallback handling are repeated; a theme constant is unused.
- Make tests depend on behavior and packaged resources.
  - Resource lookup assumes build output is inside the checkout.
  - One render assertion can pass on an empty sequence.
  - A state round-trip test repeats stronger coverage.
  - UI smoke tests assert absence of historical controls rather than useful behavior.
- Resolve script outputs from project metadata.
  - Build/run/shortcut/verification duplicate framework and runtime output paths.

## Structural corrections

- File clipboard writes now replace the remembered transfer intent together. Copying generic
  file contents after cutting the same file no longer accidentally moves it on the next paste.
- Shared file/folder collision allocation permits suffix `(9999)` consistently; the previous
  file loop rejected it one iteration early.
- Case-only entry rename attempts rollback when the final move fails, as basket rename already did.
- Async external drops abandon a destination if its basket ceased to be active during retrieval.
- MainWindow is split by existing features, with feature-owned fields beside their handlers.

## Required special cases

Preserve Windows clipboard contention retries, filesystem watcher debounce and echo suppression, durable basket rename intent/recovery, case-only rename on Windows, legacy state migrations, offscreen verification isolation, best-effort OS cosmetic operations, partial transfer results and bounded previews. They represent actual OS/product constraints rather than interchangeable branches.

## Validation

Independent follow-up reviews found no additional actionable issues in the changed code.

- Baseline Linux Debug: 321 tests, 292 passed, 29 failed.
- Final Linux Debug and Release: 324 tests each, 297 passed, 27 failed. Every remaining
  failure is present in the baseline. Two old filename-formatter tests were replaced after
  removing that filesystem wrapper; filesystem naming remains tested at the naming boundary.
- Platform failures involve Windows filename restrictions, file attributes, sharing locks and
  named synchronization events. Tests were not weakened or disabled to make Linux green.
- Core compilation succeeds. Settings/state/navigation/transfer regression checks pass.
- Full Release tests also ran with artifacts outside the checkout, validating resource packaging.
- PowerShell parses cleanly. MSBuild output resolution was checked for both platforms,
  configurations and an external artifacts directory.
- All App C# files pass Roslyn syntax and semantic checks against actual MSBuild references;
  scratch substitutes supplied the XAML-generated fields and InitializeComponent methods.
- Debug and Release desktop builds reach the Windows XAML compiler, which cannot execute on
  Linux (`Exec format error`). Actual XAML generation, Windows builds and the UI smoke suite
  remain unverified and must run on Windows before release.
- `git diff --check` passes. No merge, release or user-data operations were performed.

Validation does not establish Windows runtime equivalence; the patch is prepared for review,
with the platform-specific verification boundary recorded explicitly.
