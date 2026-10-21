# File workspace

## Sub-features

External filesystem changes appear without refresh; the toolbar has no refresh button; folders expand; selected text appears in preview; preview fills the available height without footer actions; file/folder rows provide Open and exact path copying; text and recursive folder paste target the selected parent; new folders are created there and selection is persisted.

## How to get to it (user POV)

Hover a file or folder in the central tree to reveal Open and copy-path buttons; keyboard focus also reveals them. Select a text file for a full-height preview. Choose Вставить to import the clipboard into the current folder; choose Новая папка (also Ctrl+Shift+N) to create a folder.

## Driving it with UI Automation

Run scripts/verify.ps1. It checks that RefreshButton is absent. After the note journey it creates external folder2/inside.txt and folder10 fixtures. Without refresh it waits for both nodes, selects/expands folder2, selects inside.txt and compares PreviewText to exact file contents. It checks that PreviewText reaches the bottom of the tree area and the preview has no action buttons/path footer. It finds enabled EntryOpenButton controls and right padding, then invokes each row's EntryCopyPathButton (inside.txt and folder2), comparing the clipboard with exact file and folder paths. PasteButton imports unique clipboard text, then a FileDropList source containing deep/nested.txt; assertions check exact destination bytes beneath folder2. It reselects folder2, waits for selectedFolder to equal folder2 exactly, invokes NewFolderButton and checks one new child directory. Read preview/workspace snapshots, actions.txt and observed-data/. Source: MainWindow.xaml.cs tree selection, CreateNewFolder and paste handlers; Platform/FolderWatcher.cs; BasketWorkspace folder, paste and preview operations.

## Gotchas

Selecting a file determines its parent as the paste target. New-folder creation enters inline rename; UI selection can become the new folder while persisted selection remains its parent during inline rename. Folder names are localized: compare directory changes rather than a fixed English name. Clipboard is shared and restored by the helper. Copy buttons are invoked through UIA; Open presence is checked, but launching external applications, pointer hover appearance and splitter dragging are not covered. Breadcrumb/up, keyboard operations, collisions, drag/drop, rename, deletion, image appearance and selection restoration after restart are not covered.
