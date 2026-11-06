# PasteHatch — architecture overview

The diagrams match the implemented application. Stack and decision rationale:
[decisions.md](decisions.md).

- **Platform:** C#/.NET 10, WinUI 3 + Windows App SDK runtime 2.5.1 (self-contained, no Windows App
  SDK runtime install), Windows 10 1809+ / Windows 11.
- **Storage:** basket folders and a JSON state file in `%LOCALAPPDATA%\PasteHatch`; the
  file system is the source of truth, `state.json` keeps basket order, basket names, the
  active basket, each basket's selected folder, recent copied filesystem paths and window geometry.

Each basket has a stable UID independent of its mutable directory name. State version 4
persists their association; older files are upgraded at the read boundary without renaming
existing directories. Explicit rename moves the directory and records a durable rename intent
before the move so interrupted state writes can recover the association on startup. Paths
copied before a rename remain literal historical values and may become stale.

Raw pasted text uses its first five words for a Windows-safe, collision-free `.txt` filename.
Quick Note, URL and image naming retain their separate existing rules. Basket deletion uses
one preference-aware policy with confirmation enabled by default; files and nested folders
retain their own confirmations.

The right panel width is stored in `state.json` as `previewWidth` in logical pixels. It is
saved after a drag ends and restored on startup; in a narrow window the shown width is
clamped to the available space without losing the saved value.

## System context

PasteHatch is a Windows application that gathers files and clipboard content into separate
baskets. The user and a local AI agent work with those files through
`%LOCALAPPDATA%\PasteHatch\Baskets`.

```mermaid
flowchart LR
    user((User))
    app[PasteHatch<br/>Windows · C#/.NET]
    windows[Windows<br/>clipboard · tray · Explorer · Recycle Bin · TaskDialog]
    baskets[(Basket files<br/>%LOCALAPPDATA%\PasteHatch\Baskets)]
    state[(Tab state<br/>%LOCALAPPDATA%\PasteHatch\state.json)]
    agent[Local AI agent]

    user -->|creates baskets and adds materials| app
    app -->|shows contents and path| user
    app <-->|clipboard · shell · tray · recycle bin · dialogs| windows
    app <-->|reads and writes files| baskets
    app <-->|restores baskets and window| state
    agent -->|reads the folder handed over by the user| baskets
```

## Internal application boundaries

```mermaid
flowchart LR
    user((User))
    os[Windows API and Shell]
    agent[Local AI agent]

    subgraph pastehatch[PasteHatch · C#/.NET]
        ui[PasteHatch.App<br/>WinUI 3 · window · basket rail · file tree<br/>breadcrumb · preview]
        core[PasteHatch.Core<br/>baskets · file tree · materials · naming<br/>state.json · restore · classification]
        platform[PasteHatch.App/Platform<br/>clipboard · tray · Recycle Bin · TaskDialog<br/>FileSystemWatcher · single instance · Win32]
    end

    baskets[(Basket files<br/>%LOCALAPPDATA%\PasteHatch\Baskets)]
    state[(Tab state<br/>%LOCALAPPDATA%\PasteHatch\state.json)]

    user -->|actions| ui
    ui -->|commands and change subscriptions| core
    core <-->|file reads and writes| baskets
    core <-->|tab restore| state
    core -->|system operations through interfaces| platform
    platform <-->|Windows API| os
    agent -->|reads the folder handed over by the user| baskets
```

Responsibility boundaries:

- **File transfer** is concentrated in `FileTransfer`: import, explicit copy and move share
  path checks, collision handling and partial-success handling. `BasketWorkspace` publishes
  the final result and records copy/cut intent together with each file-clipboard write; the window
  passes user actions through. `MaterialNaming` owns filename safety and collision allocation for
  both files and folders.
- **Tree state** is held by the workspace-owned `BasketTreeNavigation`: selection and expanded folders belong to
  the basket UID, and the current folder is derived from the selection (a selected folder is
  current, a selected file's parent is current), so no other module keeps a copy of it. Persistence reads navigation directly; constructing a
  workspace needs no callback through the window.
  Operations receive the folder explicitly and the state file records it. A vanished selected
  path falls back to the nearest existing parent. Switching baskets selects the first root
  entry in the existing sort order; initial startup retains saved-folder restoration and
  same-basket refresh retains the selected item. WinUI focuses the realized selected row
  after a basket switch while the window is active. WinUI builds nodes lazily, renders the
  breadcrumb prepared by the navigation and reconciles the tree with one call.
- **Live refresh** is coordinated by `WorkspaceRefreshCoordinator`: watcher lifetime depends
  on window visibility and the active basket. A second, nonrecursive watcher observes the
  baskets root; visible folders copied there are adopted as baskets with their original names,
  subject to the 100-basket limit. Stale notifications are dropped, errors cause
  a re-walk. Status messages do not rebuild the tree; content changes defer their own disk
  notifications. `IWorkspaceWatcher` is implemented by OS watching and a controllable test
  variant.

- **PasteHatch.Core** knows nothing about WinUI: the domain rules (the 100-basket limit,
  "the last basket never closes", tree sorting and traversal, name collisions, type
  classification, UID, restore) and the contracts `IClipboardReader` / `IShellService` /
  `IConfirmationService` / `ITabStateStore`. Every basket item is one `BasketEntry` record with
  a kind discriminator, and `RenameEntry` / `DeleteEntry` / `OpenEntry` carry the kind rules
  inside. Covered by xUnit tests.
- **PasteHatch.App/Platform** implements those contracts with native Windows means
  (WinForms `TaskDialog`/`NotifyIcon`/`Clipboard`, `Microsoft.VisualBasic.FileIO` for the
  Recycle Bin, Win32 `SetWindowPos`/monitor/pin, `Mutex`+event for the single instance).
  Offscreen verification with an explicit data root scopes its mutex and event to that root,
  allowing disposable UI checks beside the owner's normal instance without activating it.
- **PasteHatch.App** is the view: standard WinUI 3 controls (`ListView` rail, `CommandBar`,
  `TreeView`, `BreadcrumbBar`, `TextBox`, `Image`, `InfoBar`, `MenuFlyout`) and the standard
  window title bar; system light/dark theme; a separate settings window on `ToggleSwitch`.
  Layout is three columns: basket rail, file tree, preview. While the window is visible,
  `FolderWatcher` instances listen to changes in the active basket and the baskets root.

The diagrams separate external context from internal duties, as C4 recommends for the
overview level; deeper levels are added when they help answer a specific architecture
question. See [C4: diagram types](https://c4model.com/diagrams).

## Implementation map

`MainWindow.xaml.cs` composes the application and coordinates view refreshes. Its partial files
keep basket rail, tree, preview, commands, drag/drop, layout and settings state beside their
handlers. They remain one WinUI window rather than pass-through controller layers.

`JsonFile<T>` owns forgiving JSON reads and atomic replacement for both metadata stores.
`TabStateStore` retains schema migration. `DiskRename` owns case-only disk moves and rollback;
basket durable rename intent and startup recovery remain in the workspace.
`ClipboardAccess` owns the Windows contention retry policy; readers retain an empty-payload
fallback and writers propagate failures. External drops check that their captured basket is
still active after asynchronous clipboard retrieval.

Build, run, shortcut and verification scripts obtain the executable location through
`project-output.ps1`, which asks MSBuild for evaluated output properties. Test locale resources
are copied from the application sources into test output, including external artifacts folders.
