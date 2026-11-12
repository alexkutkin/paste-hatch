# File workspace

## Sub-features

External filesystem changes appear without refresh; the toolbar has no refresh button; folders expand; selected text appears in preview; preview fills the available height without footer actions; file/folder rows provide Open and exact path copying; text and recursive folder paste target the selected parent; local file copies retain clipboard identity; new folders are created there and unchanged-name submission closes their inline editor.

## How to get to it (user POV)

Hover a file or folder in the central tree to reveal Open and copy-path buttons; keyboard focus also reveals them. Select a text file for a full-height preview. Choose Paste to import the clipboard into the current folder; choose New folder (also Ctrl+Shift+N) to create a folder.

## Driving it with UI Automation

Run scripts/verify.ps1. It checks that RefreshButton is absent. After the note journey it creates external folder2/inside.txt and folder10 fixtures. Without refresh it waits for both nodes, selects/expands folder2, selects inside.txt and compares PreviewText to exact file contents. It checks that PreviewText reaches the bottom of the tree area and the preview has no action buttons/path footer. It finds enabled EntryOpenButton controls and right padding, then invokes each row's EntryCopyPathButton (inside.txt and folder2), comparing the clipboard with exact file and folder paths and checking persisted history order. PasteButton pastes Cyrillic text twice, verifying its first-five-word filename, numbered collision suffix and exact contents, then imports a FileDropList source containing deep/nested.txt.

The helper creates clipboard-copy.bin externally, selects its row and invokes EntryCopyContentButton. It checks the native file-drop path, nonempty PasteHatch.LocalTransferToken.v1 GUID and Preferred DropEffect=Copy. Pasting into the same folder must create an exact numbered copy; ordinary Import would skip it. Replacing the clipboard externally with the identical source path must produce the already-in-basket status without a third copy or a history event. clipboard-transfer.ui.txt, clipboard-local.formats.txt and observed-data/ preserve the evidence.

It reselects folder2, waits for selectedFolder to equal folder2 exactly, invokes NewFolderButton and checks one new child directory. It submits the initial name through the actual inline Save name button without changing its text, then checks that the editor disappears, the original row label returns, the directory path is unchanged and every basket file hash is unchanged. rename-unchanged-editing/rename-unchanged snapshots and rename-unchanged.files.txt record that journey. actions.txt also records row runtime identities, because a later tree rebuild can mask a missing explicit editor transition. A later basket-root copy adds one history event; creation, activation and paste add none. Read preview/workspace snapshots, actions.txt and observed-data/. Source: MainWindow.xaml.cs tree selection, CreateNewFolder, rename and paste handlers; Platform/FolderWatcher.cs; BasketWorkspace folder, clipboard, paste and preview operations.

## Gotchas

Selecting a file determines its parent as the paste target. New-folder creation enters inline rename; UI selection can become the new folder while persisted selection remains its parent during inline rename. Folder names are localized: compare directory changes rather than a fixed English name. Clipboard is shared and restored by the helper. Copy buttons are invoked through UIA; Open presence is checked, but launching external applications, pointer hover appearance and splitter dragging are not covered. The rename journey covers unchanged-name submission for a newly created folder; F2/double-click entry, changed names, file rename and deferred focus during context switches remain unverified. Cut, partial/uncertain native transfers, breadcrumb/up, keyboard operations, drag/drop, deletion, image appearance and selection restoration after restart are not covered.
