# File workspace

## Sub-features

External filesystem changes appear without refresh; folders expand; selected text appears in preview; its full path copies; text and recursive folder paste target the selected parent; new folders are created there and selection is persisted.

## How to get to it (user POV)

Select/expand a folder in the central tree, select a text file for preview, choose its copy-path button. Choose Вставить to import the clipboard into the current folder; choose Новая папка (also Ctrl+Shift+N) to create a folder.

## Driving it with UI Automation

Run scripts/verify.ps1. After the note journey it creates external folder2/inside.txt and folder10 fixtures. Without refresh it waits for both nodes, selects/expands folder2, selects inside.txt and compares PreviewText to exact file contents. It invokes PreviewCopyPathButton and compares the clipboard path. PasteButton imports unique clipboard text, then a FileDropList source containing deep/nested.txt; assertions check exact destination bytes beneath folder2. It reselects folder2, waits for selectedFolder to equal folder2 exactly, invokes NewFolderButton and checks one new child directory. Read preview/workspace snapshots, actions.txt and observed-data/. Source: MainWindow.xaml.cs tree selection, CreateNewFolder and paste handlers; Platform/FolderWatcher.cs; BasketWorkspace folder, paste and preview operations.

## Gotchas

Selecting a file determines its parent as the paste target. New-folder creation enters inline rename; UI selection can become the new folder while persisted selection remains its parent during inline rename. Folder names are localized: compare directory changes rather than a fixed English name. Clipboard is shared and restored by the helper. Breadcrumb/up, keyboard operations, collisions, drag/drop, rename, deletion, image appearance and selection restoration after restart are not covered.
