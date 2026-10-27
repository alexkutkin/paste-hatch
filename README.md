<!-- prettier-ignore -->
<div align="center">

<img src="./docs/images/icon.png" alt="" align="center" height="96" />

# PasteHatch

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square&logo=windows)
![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet)
![WinUI 3](https://img.shields.io/badge/WinUI-3-0067C0?style=flat-square)
![tests](https://img.shields.io/badge/tests-301%20passing-2ea44f?style=flat-square)

A light Windows utility that gathers temporary context for local AI agents: files,
folders, screenshots, text and links land in "baskets" — ordinary folders on disk.
No servers, no cloud, no save dialogs.

[Features](#features) • [Install](#install) • [Usage](#usage) • [Data](#where-data-lives) • [Design](#how-it-is-built) • [Development](#development) • [FAQ](#faq)

![PasteHatch window](./docs/images/screenshot.png)

</div>

PasteHatch lives in the notification area and opens on a click on its icon. Everything you
paste or drop into the window is saved as a separate file in the active basket's folder —
that folder can be handed to a local agent as is.

> [!TIP]
> Press «Скопировать путь» (Copy path) on the command bar and give the path to the agent —
> it reads the materials straight from the file system, with no export or syncing.

## Features

- 🧺 **Baskets are folders** — every basket is a separate folder
  `%LOCALAPPDATA%\PasteHatch\Baskets\<UID>`; the path is shown in the status bar and copied
  with one button.
- 🗂 **Basket rail on the left** — up to 10 baskets, the active one highlighted; `+` creates,
  `×` closes, double click renames, dragging reorders. Baskets, their names, order, selected
  folders and contents survive a restart.
- 🌳 **One file tree** — folders and files together in the middle; folders expand on demand,
  the selected folder is remembered per basket.
- 📥 **Universal paste** — `Ctrl+V` or the «Вставить» (Paste) button: a screenshot is saved
  as `.png`, text as `.txt`, a link as `.url`, files and folders from Explorer are copied
  into the current folder. Folders copy recursively, name collisions get the ` (2)` suffix.
- 🔄 **Lazy live refresh** — while the window is visible the app watches the active basket
  through `FileSystemWatcher`: no disk polling, with a debounce on event batches. Hidden in
  the tray the app watches nothing; `F5` remains the fallback.
- 🖱 **Drag & Drop** — files and folders can be dropped into the window or onto a folder in
  the tree.
- 👀 **Preview without a separate window** — `.txt`/`.md` text, `.url` contents and images
  show on the right; other files show type, size and date.
- 📋 **File path** — a button in the preview bar and a context menu item copy the full path
  of the selected file or folder; the «Скопировать путь» (Copy path) button on the bar
  copies the current folder.
- ✏️ **Rename** — `F2` or the context menu: only the name is edited, the extension stays
  visible and unchanged. Folders rename the same way.
- 📁 **Folder operations** — create (`Ctrl+Shift+N`), rename and delete to the Windows
  Recycle Bin with confirmation.
- ↕️ **Move** — a file or folder can be dragged onto another folder; `Ctrl+X`/`Ctrl+C`/`Ctrl+V`
  work like in Explorer. Plain sorting: folders first, then files.
- ✍️ **Quick note** — `Enter` in the note box saves a `.txt` named after the weekday and time
  (e.g. `Пн, 13-02.txt`) into the current folder, with no save dialog.
- 🧹 **Safe actions** — deleting and closing a basket ask for confirmation and use the
  Windows Recycle Bin; rename never changes the extension.
- 📌 **Tray** — a regular launch shows no window; clicking the icon toggles it and always
  brings it to the front, right click opens the menu.
- ⚙️ **Settings** — start in tray, remember window position and always-on-top; changes apply
  immediately.
- 🪶 **A native Windows app** — standard WinUI 3 controls (`ListView` rail, `TreeView`,
  `BreadcrumbBar`, `CommandBar`, `InfoBar`), system theme, native context menus and
  confirmation dialogs.

## Install

### Requirements

- Windows 10 version 1809+ or Windows 11 (verified on Windows 11, build 26200).
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) — to build and run from
  sources. Windows App SDK and WinUI 3 ship with the application
  (`WindowsAppSDKSelfContained`), no separate install needed.

### Build and run

The shortest path is the provided scripts:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # Release build, prints the exe path
powershell -ExecutionPolicy Bypass -File scripts\run.ps1     # builds when needed and runs
```

The `--show` argument opens the window right away instead of waiting for a tray click.
Architecture is x64; ARM64 can be built if needed.

<details>
<summary>Manual build (dotnet, ARM64)</summary>

```powershell
dotnet build src\PasteHatch.App\PasteHatch.App.csproj -c Release -p:Platform=x64
.\src\PasteHatch.App\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\PasteHatch.exe
```

For ARM64 replace `Platform` with `ARM64` and `win-x64` with `win-arm64`.

</details>

### Desktop shortcut

The default build is framework-dependent: the app needs the installed **.NET 9 Desktop
Runtime** (part of the SDK). The shortcut is created like this:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-shortcut.ps1            # create the desktop shortcut
powershell -ExecutionPolicy Bypass -File scripts\install-shortcut.ps1 -Uninstall # remove the shortcut
```

The shortcut points at `src\PasteHatch.App\bin\x64\Release\...\PasteHatch.exe`; rebuilding
does not change the target.

## Usage

1. Launch the app — the first launch opens the window, afterwards PasteHatch lives in the
   tray.
2. Pick a basket in the rail on the left, then a folder in the file tree.
3. Add materials: `Ctrl+V`, the «Вставить» (Paste) button, the quick note box or drag and
   drop.
4. Copy the current folder path with «Скопировать путь» (Copy path) or a single file path
   with the preview bar button — and hand it to the agent.

### Hotkeys and gestures

| Action | How |
| --- | --- |
| Add clipboard content | `Ctrl+V` with focus in the window or the «Вставить» button |
| Add files or folders | drop into the window or onto a folder in the tree |
| Select a basket | click in the rail on the left |
| Select a file or folder | click in the tree |
| Expand/collapse a folder | the chevron, double click on the folder or `Enter` |
| Open a file | double click on the file or `Enter` |
| Go to a folder | click a breadcrumb or the `↑` button |
| Go one level up | the `↑` button or `Alt+↑` |
| Rename a file or folder | `F2` or the context menu |
| Create a folder | `Ctrl+Shift+N` or the «Новая папка» button |
| Copy the path | the preview button or the context menu; on the bar — the current folder path |
| Copy / cut / paste | `Ctrl+C` / `Ctrl+X` / `Ctrl+V` |
| Move a file or folder | drag onto a folder |
| Delete a file or folder | `Delete` (with confirmation) |
| Refresh the basket | `F5` or the «Обновить» button |
| Rename a basket | double click on the rail row, `Enter` saves, `Esc` cancels |
| Create/close a basket | `+` in the rail / `×` on the row (with confirmation); `Ctrl+T` / `Ctrl+W` |
| Jump to a basket | `Ctrl+1…8`, `Ctrl+9` — the last one |
| Reorder baskets | drag a rail row or `Ctrl+Shift+↑`/`Ctrl+Shift+↓` |
| Show/hide the window | click the tray icon |
| Minimize to tray | close the window (title `×`) or `Alt+F4` |
| Settings | the «…» button on the command bar or right click on the tray icon |
| Quit | the tray menu «Выход» |

### Preview and note

The preview opens on the right and creates no separate window. For `.txt`, `.md` and `.url`
it shows the text, for images a scaled picture, for other files the type, size and
modification date. Text can be selected and copied. Markdown and HTML are shown as source,
without rendering; PDF and archives do not open in place — they have an «Открыть» (Open)
button. Large text is read from the beginning only, so the panel never stalls.

While the window is visible the app lazily watches the active basket through
`FileSystemWatcher`: OS events, no disk polling, a batch of changes becomes one refresh
after about 0.5 s. Hiding to the tray turns the watcher off; `F5` and showing the window
from the tray remain fallback refresh paths.

A quick note is saved as `.txt` in the current folder, named after the weekday and time.
Windows forbids a colon in file names, so in file names it becomes a dash
(`Пн, 13-02.txt`), while the note title keeps `Пн, 13:02`.

## Where data lives

| What | Where |
| --- | --- |
| Basket materials | `%LOCALAPPDATA%\PasteHatch\Baskets\<UID>\` |
| Open baskets, their names, active basket, each basket's selected folder, window geometry | `%LOCALAPPDATA%\PasteHatch\state.json` |
| Application settings | `%LOCALAPPDATA%\PasteHatch\settings.json` |
| Deleted materials | the standard Windows Recycle Bin (files can be restored) |

> [!NOTE]
> Files on disk are the source of truth: basket state restores both from `state.json` and
> from the folders in `Baskets`, so losing the state file loses no materials. A basket
> folder is named with a short UID (6 characters without look-alike `I/O/0/1`), while the
> display name is stored separately. `state.json` version 2 is read: the old manual
> material order is ignored, the files stay in place.

For automated tests the data path can be overridden with `PASTEHATCH_DATA_ROOT` (unset in
normal work).

## How it is built

```
src/PasteHatch.Core     — domain: baskets, file tree, materials, naming, preview, state.json
src/PasteHatch.App      — WinUI 3: window, basket rail, file tree, preview, settings
src/PasteHatch.App/Platform — clipboard, tray, Recycle Bin, TaskDialog, single instance, FileSystemWatcher, Win32
tests/PasteHatch.Core.Tests — xUnit domain tests
scripts                 — build/run/icon generation/shortcut/headless smoke runs
docs                    — brief, architecture, accepted decisions, prototype
```

- **PasteHatch.Core** knows nothing about WinUI: the rules (the 10-basket limit, "the last
  basket never closes", tree sorting and traversal, name collisions, type classification,
  UID, restore) and the contracts `IClipboardReader` / `IShellService` /
  `IConfirmationService` / `ITabStateStore`. Covered by xUnit tests.
- **PasteHatch.App/Platform** implements the contracts with native Windows means: WinForms
  `TaskDialog` / `NotifyIcon` / `Clipboard`, `Microsoft.VisualBasic.FileIO` for the Recycle
  Bin, Win32 `SetWindowPos` / `SetForegroundWindow` / monitor / pin, `Mutex` + an event for
  the single instance, `FileSystemWatcher` for lazy watching.
- **PasteHatch.App** is the view: standard WinUI 3 controls (`ListView` rail,
  `CommandBar`, `TreeView`, `BreadcrumbBar`, `TextBox`, `Image`, `InfoBar`, `MenuFlyout`)
  and the standard window title bar; system light/dark theme; a separate settings window.

Details: [`docs/architecture-overview.md`](docs/architecture-overview.md), decision
rationale: [`docs/decisions.md`](docs/decisions.md).

## Development

### Tests

Core unit tests (naming, classification, state, baskets, file tree, sorting, collisions,
restore, preview):

```powershell
dotnet test tests\PasteHatch.Core.Tests\PasteHatch.Core.Tests.csproj
```

A single UI run verifies the note, basket creation, the single instance, the file tree,
live refresh, text preview, path copying, text and nested folder paste, folder creation
and saving the current location. Close PasteHatch through the tray menu first. Build Debug
and Release with the commands above, then run:

```powershell
powershell -STA -ExecutionPolicy Bypass -File scripts\verify.ps1
# To verify the Release build:
powershell -STA -ExecutionPolicy Bypass -File scripts\verify.ps1 -Configuration Release
```

The script uses separate data and terminates only the processes it started. The window
works off-screen without a tray. The checks use the shared clipboard and restore it after
the run; do not copy anything else while they run. Evidence is saved under
`.scratch/verify-pastehatch/<GUID>/`. Coverage map and detailed instructions:
[.agents/skills/verify-pastehatch/SKILL.md](.agents/skills/verify-pastehatch/SKILL.md).

<details>
<summary>Debug seams for automated checks</summary>

| Variable | Purpose |
| --- | --- |
| `PASTEHATCH_DATA_ROOT` | an isolated data folder instead of `%LOCALAPPDATA%\PasteHatch` |
| `PASTEHATCH_TEST_OFFSCREEN=1` | the window goes off-screen without activation |
| `PASTEHATCH_TEST_NO_TRAY=1` | the app creates no tray icon |

In normal work these variables are unset, so the checks never disturb the user.

</details>

## FAQ

### Why is no window visible after launch?

PasteHatch starts in the tray. Click the icon in the notification area — the window appears
and comes to the front. The exception is the very first launch, when there is no data yet:
the window opens right away so the app is easy to find. `--show` forces the window open.

### How do I hand a basket to the agent?

Press «Скопировать путь» (Copy path) on the command bar or open the folder with the
«Открыть папку» button and give the path to the agent. The materials are ordinary files,
there is no intermediate format.

### Why can't I reorder files by hand?

The tree uses Explorer sorting: folders first, then files, by name with natural number
order (`file2` before `file10`). Manual order was only possible in the flat list and got in
the way of dragging files into folders, so it is gone. Files on disk and their names do not
change.

### How does the tree update when a file changes outside?

While the window is visible the app lazily watches the active basket through
`FileSystemWatcher`. These are OS events, no disk polling; a batch of changes becomes one
refresh after about 0.5 s. Hiding to the tray turns the watcher off. `F5` and showing the
window from the tray remain fallback refresh paths.

### Can I drop a folder?

Yes. The folder copies recursively into the current folder like in Explorer; name
collisions get the ` (2)` suffix. Junctions and symlinks are not expanded, so copying
cannot loop.

### Where do deleted materials go?

To the standard Windows Recycle Bin, so a file can be restored. Before deleting the app
asks for confirmation.

## Known limitations

- The last basket cannot be closed: the app always works with at least one basket.
- Only single selection is supported for now; multiple selection is the next step.
- No built-in HTML or PDF rendering: HTML shows as source, PDF opens with the «Открыть»
  button.
- Large preview text is read from the beginning only (64 KB).
- Live watching covers the active basket and runs while the window is visible; in the tray
  nothing is watched.
- Unit tests cover the domain; the UI is verified by headless smoke runs (window off-screen,
  no tray) and manual runs.
- .NET 9 is Standard Term Support: support ends 2026-11-10. The code uses no .NET 9-only
  APIs, so moving to .NET 10 LTS is a target framework rename
  (`net9.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0`) after installing SDK 10.

## Links

- [Project brief](docs/discovery/brief.txt) — the original requirements.
- [Architecture](docs/architecture-overview.md) — boundaries and C4 diagrams.
- [Accepted decisions](docs/decisions.md) — why WinUI 3, tray and confirmations were
  chosen and how the decisions were verified.
- [Visual prototype](docs/discovery/prototype/README.md) — the original mock (as a screen
  inventory source only; the app uses standard controls).
