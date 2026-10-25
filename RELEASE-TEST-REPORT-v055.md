# SourceCodeEditor v0.5.5 — Monospaced font catalog syntax hotfix

## User-reported compiler errors

- `MonospacedFontCatalog.cs`, previous lines 49–51: `int[] widths = { ... }.Select(...)` was invalid C# syntax.
- This malformed declaration caused CS1003, CS1002, and the cascading CS0103 (`Select` not found).
- The code now uses an explicit `char[] glyphs` array and a `foreach` measurement loop without LINQ.
- CA1859 and IDE0305 were analyzer suggestions rather than compiler errors.

## Validation

- Offline structural/file-fixture tests run on Linux via `python3 tests/offline_source_checks.py`.
- **No .NET SDK/Windows runtime is installed in this environment** and outbound networking is unavailable, so Windows compilation and UI execution were not possible.
- On Windows, open `SourceCodeEditor.sln` and rebuild `Release | x64`. Run `negative-smoke.ps1` before generating the installer.

## Scope

Only the catalog compile fix, version bump, and related offline regression checks were changed. The original single Visual Studio solution and RTF editor source are preserved.
