# Quick note

## Sub-features

Save nonempty text as a timestamp-named .txt in the selected folder; clear input after success.

## How to get to it (user POV)

Type in Быстрая заметка at the bottom of the window, choose Добавить or press Enter.

## Driving it with UI Automation

Run scripts/verify.ps1 as documented in the parent skill. It fills QuickNoteTextBox with a unique value via ValuePattern, invokes QuickNoteSaveButton and waits for exactly one file with identical full contents plus an empty input. Read actions.txt, note.ui.txt and actual bytes under observed-data/Baskets/. Source: MainWindow.xaml.cs SaveQuickNote / OnQuickNoteKeyDown; BasketWorkspace.AddQuickNote.

## Gotchas

Whitespace-only input does not save. Check contents, not a guessed timestamp filename. The automated journey covers the button in a fresh basket root; Enter and nested-note destinations are unverified.
