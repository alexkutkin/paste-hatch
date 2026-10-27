# PasteHatch

Baskets for files and clipboard content.

## Terms

**Basket**:
A named workspace with its own folder, files and nested folders.
_Do not confuse with_: the Windows Recycle Bin where deleted items go.

**Material**:
A file inside a basket: text, an image, a link or another file. A folder is not a material.

**Current folder**:
The folder targeted by pasting and by creating new items. When a file is selected, it is
that file's parent folder.

**Transfer**:
The umbrella term for import, explicit copy and move of files or folders into a basket.

**Import**:
Adding external files and folders while keeping the original. Items already in this
basket are skipped.

**Explicit copy**:
Creating one more copy of the selected item, including inside the same basket.

**Move**:
Relocating an item to another folder, removing the source location after the move
succeeds. Moving to the same folder changes nothing.

**Tree state**:
The selected item and the expanded folders of a basket. It defines the current folder and
is restored on tree refresh and on basket switches.
