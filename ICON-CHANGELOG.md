# SourceCodeEditor icon change — v0.5.6

- Replaced `SourceCodeEditor.ico` with a blue notepad and a large centered six-arm asterisk.
- Windows icon resolutions: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 px, 32-bit RGBA.
- The SDK project already had `<ApplicationIcon>SourceCodeEditor.ico</ApplicationIcon>`; this remains unchanged.
- `MainForm.cs` already uses `Icon.ExtractAssociatedIcon(Application.ExecutablePath)`; the form icon will update with the executable icon.
- Added `SetupIconFile=SourceCodeEditor.ico` to `installer.iss` so setup gets the same branding.
- Build after cleaning `bin` and `obj`. Windows Explorer icon cache may take time to refresh after an upgrade.
- No source editing features were modified.
- Windows compilation and runtime verification were not available in this environment.
