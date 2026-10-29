# Accepted decisions

This file records product constraints and reasons that implementation alone cannot explain.
Current behavior and structure belong in the README and architecture overview.

- **Use native Fluent controls and the system theme.** A custom visual language is outside
  the product scope.
- **Keep files on disk as the source of truth.** Losing application state must not lose
  materials. Basket renaming must preserve paths already handed to an agent; folders
  copied into the baskets root must remain accessible without moving or renaming them.
- **Keep baskets in a left rail, with at most 100 open.** There must always be a usable
  basket, so the last one cannot close.
- **Confirm recycling with Cancel as the default.** Recoverability through the Windows
  Recycle Bin does not replace the owner's requirement for an explicit confirmation.
- **Use one tray instance and one state writer.** Routine launches should avoid desktop
  clutter; the first launch must show enough UI for the app to be discoverable.
- **Give every action a keyboard path.** Commands must remain discoverable outside context
  menus, and labels for commands requiring confirmation must use an ellipsis.
- **Keep the preview inside the main window.** COM preview handlers and WebView2 add an
  unnecessary hosting and security surface for temporary context materials. Bound reads
  of large text to keep the UI responsive.
- **Copy paths as plain text without quotes.** Their recipient is an agent, not a shell.
- **Disable tree-row entrance animations.** Otherwise basket switching briefly appears to
  show an empty tree.
- **Generate the icon from its script.** Keep the artwork reproducible instead of editing
  the binary directly.
- **Use domain tests and one bounded UI smoke suite.** Long UI scenarios are expensive to
  maintain. Isolated storage and offscreen modes let automated checks run without touching
  the owner's materials or opening windows on their desktop.
