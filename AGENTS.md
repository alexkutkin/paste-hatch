# Working agreement

## Communication
- Communicate with the owner in Russian.
- Write commit messages and test names in English.

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
