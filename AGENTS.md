# Working agreement

## Communication
- Communicate with the owner in Russian.
- Keep everything the repository stores in English: code and comments, commit
  messages, test names, README, docs, ADRs, AGENTS.md, config comments.
- Content, not documentation, keeps its own language: product strings in locale
  resource files (`Strings/ru-RU` and alike) and personal financial documents.

## Git
- Work directly on `main`. Create branches or pull requests only when requested.
- Use focused Conventional Commits: `feat:`, `fix:`, `docs:`, `refactor:`, `chore:`.
- Do not add AI co-authorship or tool credits to commits or pull requests.
- Add `Signed-off-by` only when required.
- Commit and push only when requested. Push to `origin/main` unless otherwise specified.
- Do not force-push or rewrite published history without explicit approval.

## Project
- **Build Release together with Debug.** After code changes run `scripts\build.ps1`
  (Release x64) alongside the Debug build: the desktop shortcut launches the Release
  build, so a Debug-only change stays invisible there.

## Release
- Set `Version` in `src/PasteHatch.App/PasteHatch.App.csproj`.
- Run `powershell -ExecutionPolicy Bypass -File scripts\package.ps1` to build both
  configurations, test, and create a self-contained x64 ZIP in `dist/`.
- Extract the ZIP; with PasteHatch closed, run `scripts\verify.ps1 -ExecutablePath
  <extracted-PasteHatch.exe>` in `powershell -STA` before distributing it.
- When publishing is requested, commit and push the matching source, then create
  GitHub release `v<Version>` with the ZIP attached. Keep user data and logs out of it.
