# SourceCodeEditor v0.5.4 — verification summary

## Change scope

- View → Monospaced Fonts with distinct Application Interface / RTF Source Editor submenus, populated from Windows-installed regular fixed-width font families.
- Persistence of independent font family choices in session snapshots, with backward-compatible defaults.
- Tools → Locate Current File in File Explorer now focuses the built-in explorer tab, reenumerates drives and reveals the file, or shows an informative missing/unsaved-file message.
- New ExplorerNavigationPlan isolates deleted/renamed path handling from the GUI.

## Automated checks

- `python3 tests/offline_source_checks.py` — validates project wiring, source structure, and Linux fixture edge cases. This is **not** a C# compile.
- `negative-smoke.ps1` on Windows x64 — runs the application's native negative suite including the new folder-path, session/font-persistence, and installed-font cases.
- `conpty-smoke.ps1` on Windows x64 — tests the integrated CMD backend.

## Outstanding qualification

Visual Studio 2022 build, live GUI menu use and selection, full Windows Explorer-tree navigation, and the x64 installer must still be run on a Windows host. Do not infer those results from successful offline source checks.
