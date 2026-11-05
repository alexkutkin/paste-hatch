# Basket workflow analysis

Historical planning note preceding issues #1–#8, implemented in v1.2.0. The observations
and proposals below describe that earlier analysis; current behavior is documented in
the README and architecture overview.

The owner requested requirements analysis, not implementation. `brief.md` contains the agreed product requirements; no product questions remain open. Recommendations below are proposals, not accepted requirements.

## Evidence and constraints

- `Platform/TrayIcon.cs` already uses WinForms `ContextMenuStrip` and `ToolStripSeparator`. Microsoft documents a separator as a line that groups menu items and `ToolStripLabel` as a nonselectable text item. Standard section labels and separators can be attempted within the existing stack; final layout and keyboard behavior require a UI check. The reference screenshot supplies visual grouping, not additional features. Sources: [ToolStripSeparator](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.toolstripseparator), [ToolStripLabel](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.toolstriplabel).
- `BasketWorkspace.CopyActiveBasketPath` and `CopyEntryPath` already copy filesystem paths through `ShellService.CopyText`. The owner replaced the whole-folder tray-copy requirement with a history of copied filesystem paths. Record successful semantic filesystem-path-copy operations and reuse text clipboard writes when replaying them; file-drop copying remains a separate operation. `CopyEntryContent` also copies the HTTP/HTTPS target of saved link materials; the owner explicitly excluded these web addresses and ordinary content copying from history.
- `BasketWorkspace.RenameTab` changes only the label. `LoadTabs`, `AdoptFolders`, and `CreateBasketInternal` equate UID with the directory name. `Storage/WorkspaceState.cs` persists that assumption. Separate stable identity from the directory component throughout restoration, adoption, saving, navigation and consumers; changing just the rename handler would lose baskets on restart.
- The owner explicitly accepted breaking previously copied paths on basket rename, superseding the previous path-preservation rule in `docs/decisions.md` for that action. The new deletion preference also supersedes unconditional basket confirmation. Existing folders remain untouched during upgrade.
- Microsoft says, "Do not assume case sensitivity." Its naming rules also exclude reserved characters and device names and trailing spaces or periods. Reuse and extend the existing naming policy rather than adding competing validators. Source: [Naming Files, Paths, and Namespaces](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file).
- No copy history is persisted. Basket list ordering is user-controlled and must remain distinct from copied-link history. Activation and editing are not history events.

## Proposed canonical shape and implementation

1. Keep one material naming policy in `Naming/MaterialNaming.cs`. For newly pasted raw text, use the accepted first-five-words policy and join words with underscores. Propose a 60-character bound before the extension; sanitize and make unique using existing collision handling. Use a localized fallback for unusable names. Preserve imported filenames, contents and existing URL handling. Do not rename a document again when its contents change.
2. Keep basket deletion in `BasketWorkspace.CloseTab`. Propose confirmation enabled by default, with one persisted basket-only preference used by all deletion entry points. Recycle rather than permanently delete, retain the existing last-basket restriction, and handle Delete only in the basket list outside text editing.
3. Route both creation buttons to the same command, including its availability at the basket limit. Dispatch file-name double-clicks to the existing inline rename flow and consume that gesture before row opening. Retain other file-opening and folder-expansion behavior. Move hover spacing to precede Copy Path.
4. Introduce one basket record with stable ID and independently persisted directory name. Read existing state through a bounded upgrade and write only the current shape. Leave existing disk names untouched until explicit rename. Normalize word separators in the physical folder name to underscores. Reject invalid or occupied names with an actionable error; do not merge directories or silently append an ID. Reconcile disk and state after partial failure, and preserve disk-based recovery when state is lost. Update all path consumers and existing-folder adoption together.
5. Keep one bounded, persisted copy history under the existing state owner. Record only successful operations in the filesystem-path scope, after the clipboard write succeeds. Propose keeping the last five events, including repeats, as literal copied strings; renamed or deleted targets do not silently rewrite past copied values. Replay through the same semantic copy command. Render long labels with a leading ellipsis, preserve the final filename/folder name, and expose the full value in a tooltip. Keep manual basket order independent. Regenerate all application icon assets through `scripts/generate-icon.ps1`; propose a simple recognizable shelf silhouette that remains legible at tray size.

## Proposed Delete List

- Replace the UID-equals-directory assumption in current runtime code and persisted writes; retain only the necessary old-state reader at the ingress.
- Replace date-only naming in the confirmed pasted-text scope.
- Replace unconditional basket confirmation with the single preference-aware policy.
- Remove the hover divider currently separating Open from the copy actions.
- Replace the old icon artwork and generated outputs together.
- Avoid adding separate creation, rename or clipboard implementations for the new entry points, or a basket-activity tracker for the replaced recent-basket proposal.

## Verification for implementation

- Verify name sanitization and collisions, deletion preference persistence and focus behavior, and both creation controls at the limit.
- Verify history order across restart, successful versus failed copies, repeats, replay, deletion and rename; verify that activation and editing do not add entries. Verify exact clipboard strings and leading-ellipsis labels with a visible target name.
- Verify explicit rename, case-only rename, conflicts, locked folders, state-save failure recovery, upgrade from existing state, and recovery without state.
- Verify double-click targets, F2/Enter/Escape, hover grouping and tray grouping in the actual UI, plus icon legibility at small sizes.
- Run focused domain tests, the bounded UI smoke suite, Debug build and `scripts/build.ps1` for Release x64. No code changes or builds were performed for this analysis.

## Character preservation

- The owner declined transliteration. Preserve Cyrillic characters and use underscores between words in generated text filenames and explicitly renamed basket folders. No transliteration dependency or mapping is required.
