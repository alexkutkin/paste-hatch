# PasteHatch feature map

| Feature | Proof | Journey |
| --- | --- | --- |
| Quick note | Exact entered text saved and input cleared | [quick-note.md](quick-note.md) |
| Baskets and startup | New basket active/persisted; second launch exits | [baskets.md](baskets.md) |
| File workspace | Watcher, tree, preview, path copy, text/folder import, new folder | [folders.md](folders.md) |

Run `powershell -STA -ExecutionPolicy Bypass -File scripts\verify.ps1` from the repository root. All rows run sequentially on one owned instance, starting from empty data. There are no per-feature modes or alternate harnesses. See the parent skill for prerequisites, evidence, teardown and excluded surfaces. Alternate keyboard entry points are documented but not claimed as automated coverage.
