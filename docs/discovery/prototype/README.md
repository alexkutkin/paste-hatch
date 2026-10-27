# Context Box — visual prototype

The prototype repeats the screen from `../screen.png` and shows the states that can be
imagined before building the application.

Open `index.html` in a browser. Alpine.js needs an internet connection.

What can be tried:

- switch between the ten tabs and see what a full rail looks like; the counter shows
  `10/10` and the `+` button is disabled;
- double click a tab to rename it, then press `Enter`; new tabs get a name from the weekday
  and creation time;
- see the UID and path of the active basket, then press «Открыть папку» (Open folder);
- press `×` on a tab to send the basket with its folder and files to the Windows Recycle
  Bin or cancel; afterwards a new basket can be created with the `+` button;
- reload the page and make sure the remaining open tabs, names and contents are restored;
- click a file to select it, double click to simulate opening it in the default Windows
  program, press `F2` or pick rename in the context menu; the context menu opens with a
  right click or `Shift+F10`, deletion is the `Delete` key;
- press `×` next to a file and confirm sending it to the Windows Recycle Bin or cancel the
  deletion;
- type a short note and press `Enter` to add a `.txt` file named after the weekday and time;
- minimize the window with the cross, open the simulated tray menu with a right click and
  show the window again.

All data is demo data. Prototype state is kept in the browser `localStorage` only to fake
restore between launches; no files in `%LOCALAPPDATA%` are created or deleted. Opening a
file or folder shows a tooltip instead of launching a Windows program or Explorer; file
operations, the system tray, clipboard paste and drag & drop are not implemented.
