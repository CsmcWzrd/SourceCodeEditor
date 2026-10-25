#!/usr/bin/env python3
"""Offline structural/source checks only. This is NOT a .NET compile or GUI test."""
import json
import re
import tempfile
import shutil
from pathlib import Path
from collections import Counter

root = Path(__file__).resolve().parents[1]
results = []
def check(name,condition):
    results.append({'name':name,'status':'PASS' if condition else 'FAIL'})

projects=list(root.glob('*.csproj'))
solutions=list(root.glob('*.sln'))
check('single-solution',len(solutions)==1)
check('single-project',len(projects)==1)
csproj=projects[0].read_text() if projects else ''
includes=re.findall(r'<Compile Include="([^\"]+)"',csproj)
check('explicit-includes',len(includes)>=18 and len(includes)==len(set(includes)))
for item in includes:
    check('source-exists:'+item,(root / item).is_file())
check('single-project-reference-in-solution',len(re.findall(r'^Project\(',solutions[0].read_text(), re.M))==1 if solutions else False)
check('no-auto-compile', '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>' in csproj)
check('framework-net8-windows', 'net8.0-windows' in csproj)
check('x64-only-project', '<PlatformTarget>x64</PlatformTarget>' in csproj)
check('windows-build-workflow-present',(root/'.github/workflows/windows-x64-installer.yml').exists())
check('negative-windows-runner-step', 'Negative file and session regression suite' in (root/'.github/workflows/windows-x64-installer.yml').read_text())
check('negative-windows-self-test-entrypoint', '--self-test-negative' in (root/'Program.cs').read_text())
check('ConPTY-process-attachment', 'pseudoConsole, (IntPtr)IntPtr.Size' in (root/'WindowsPseudoConsole.cs').read_text())
check('session-JSON-corruption-backup', 'RescueCorruptSession' in (root/'MainForm.Session.cs').read_text())
check('missing-closed-file-recovery', 'PrepareClosedDocument' in (root/'MainForm.cs').read_text())
check('missing-file-save-as', 'Save As is required' in (root/'MainForm.cs').read_text())
check('external-modification-conflict', 'DocumentSafety.AssessSaveTarget' in (root/'MainForm.cs').read_text())
check('disk-fingerprint', 'HashDiskFile' in (root/'DocumentSafety.cs').read_text())
check('atomic-file-write', 'File.Move(temp, path, true)' in (root/'DocumentModel.cs').read_text())
check('explorer-shows-hidden', 'AttributesToSkip = 0' in (root/'MainForm.Explorer.cs').read_text())
check('explorer-always-starts-Documents', 'NavigateExplorer(GetDocumentsDirectory())' in (root/'MainForm.cs').read_text())
check('four-navigation-tabs', all(x in (root/'MainForm.cs').read_text()+(root/'MainForm.Explorer.cs').read_text() for x in ('File Explorer','Opened Files','Closed Files','Unsaved Files')))
check('upstream-RTF-editor-preserved',(root/'third_party/CSharpCodeDetective/LineNumberedRichTextBox.cs').exists())
check('v055-version', '<Version>0.5.5</Version>' in csproj)
check('monospaced-font-catalog-is-included', 'MonospacedFontCatalog.cs' in includes)
check('monospaced-font-menu-both-categories', all(x in (root/'MainForm.cs').read_text() for x in ('Monospaced Fonts', 'RTF Source Editor', 'Application Interface')))
check('fixed-width-measured-not-hardcoded', 'TextRenderer.MeasureText' in (root/'MonospacedFontCatalog.cs').read_text())
check('font-catalog-valid-glyph-loop', all(token in (root/'MonospacedFontCatalog.cs').read_text() for token in ('char[] glyphs =', 'foreach (char glyph in glyphs)', 'minimumWidth > 0')))
check('font-catalog-no-illegal-collection-initializer-chain', not re.search(r'\b(?:int|char)\[\]\s+\w+\s*=\s*\{[^}]*\}\s*\.', (root/'MonospacedFontCatalog.cs').read_text(), re.S))
check('font-family-saved-in-session', all(x in (root/'MainForm.Session.cs').read_text() for x in ('ApplicationFontFamily = applicationFontFamily','EditorFontFamily = editorFontFamily')))
check('font-family-restored-in-session', all(x in (root/'MainForm.Session.cs').read_text() for x in ('state.ApplicationFontFamily','state.EditorFontFamily')))
check('file-location-uses-built-in-explorer', 'navigation.SelectedIndex = 0;' in (root/'MainForm.Explorer.cs').read_text().split('private void OpenFileLocation()')[1])
check('file-location-invalidates-cache', 'RefreshDriveTree();' in (root/'MainForm.Explorer.cs').read_text().split('private void ShowFileInExplorer')[1])
check('file-location-nearest-accessible-folder', 'ExplorerNavigationPlan.ForFile' in (root/'MainForm.Explorer.cs').read_text())
check('file-location-no-windows-explorer', 'Process.Start("explorer.exe"' not in (root/'MainForm.Explorer.cs').read_text())
check('persist-unsaved-on-exit', 'TryPersistSession(out string? failure)' in (root/'MainForm.Session.cs').read_text())
check('exit-does-not-discard-unsaved', 'ConfirmCanClose(doc)' not in (root/'MainForm.Session.cs').read_text().split('private void OnMainFormClosing')[1])
check('reopen-closed-option', 'Reopen closed files at startup' in (root/'MainForm.cs').read_text())
check('startup-closed-session-preference', 'SessionRestorePolicy.ShouldReopen(doc, reopenClosedOnStartup)' in (root/'MainForm.Session.cs').read_text())
check('always-blank-editor', 'EnsureAtLeastOneDocument();' in (root/'MainForm.cs').read_text())
check('no-contextmenu-dispose-in-closed-event', not any('menu.Closed +=' in (root/file).read_text() for file in ['MainForm.cs','MainForm.Shell.cs']))
check('UI-error-logging-present', (root/'AppDiagnostics.cs').is_file() and 'Application.ThreadException' in (root/'Program.cs').read_text())
check('editor-regular-default', 'Font = new Font("Consolas", 12f, FontStyle.Regular' in (root/'LineNumberedRichTextBox.cs').read_text())
check('insertion-point-normalized', 'if (length == 0) Editor.SelectionFont = Editor.Font;' in (root/'LineNumberedRichTextBox.cs').read_text())
check('normal-font-on-document-load', 'Editor.SelectAll();\n            Editor.SelectionFont = Editor.Font;' in (root/'LineNumberedRichTextBox.cs').read_text())
check('installer-present',(root/'installer.iss').is_file())
check('Windows-PowerShell-test-harness',(root/'negative-smoke.ps1').exists())
source = (root/'NegativeSelfTest.cs').read_text()
case_names = re.findall(r'Test\("([^\"]+)',source)
check('at-least-100-negative-scenarios',len(case_names)>=100)
check('at-least-70-negative-scenarios',len(case_names)>=70)
check('distinct-negative-test-names',len(case_names)==len(set(case_names)))

# Lexical check: Count delimiters using a C# lexer, avoiding interpolated string
# literals and comments. This does not certify C# syntax correctness.
try:
    from pygments import lex
    from pygments.lexers import CSharpLexer
    from pygments.token import Token
    for path in root.glob('*.cs'):
        stack=[]; good=True; unknown=0
        for t,text in lex(path.read_text(encoding='utf8'),CSharpLexer()):
            if t in Token.Error:
                unknown += 1
            if t in Token.Punctuation:
                for c in text:
                    if c in '({[': stack.append(c)
                    elif c in ')}]':
                        if not stack or stack.pop() != {')':'(', '}':'{', ']':'['}[c]: good=False
        check('balanced-delimiters:'+path.name, good and not stack)
except ImportError:
    check('pygments-lexer-installed',False)

# Real filesystem fixtures on Linux verify negative-case conditions, not C# code.
folder=Path(tempfile.mkdtemp(prefix='sourcecodeeditor-fixtures-'))
try:
    (folder/'parent').mkdir()
    original=folder/'parent'/'closed.cs'
    original.write_text('cached contents')
    cached=original.read_text()
    shutil.rmtree(original.parent)
    check('fixture-deleted-folder-and-cached-buffer',not original.exists() and cached=='cached contents')
    (folder/'parent').mkdir()
    moved=folder/'parent'/'closed.cs'
    moved.write_text('old')
    (folder/'parent').rename(folder/'moved-folder')
    check('fixture-folder-rename-invalidates-old-path',not moved.exists() and (folder/'moved-folder'/'closed.cs').exists())
    newfile=folder/'same.txt'; newfile.write_text('AAAAA')
    stat=newfile.stat(); newfile.write_text('BBBBB')
    import os
    os.utime(newfile,ns=(stat.st_atime_ns,stat.st_mtime_ns))
    check('fixture-rewrite-same-size-and-timestamp',newfile.stat().st_size==stat.st_size and newfile.stat().st_mtime_ns==stat.st_mtime_ns)
    hidden=folder/'.hidden.cs'; hidden.write_text('txt')
    check('fixture-hidden-text-file-detected',hidden.exists() and hidden.name.startswith('.'))
finally:
    shutil.rmtree(folder)

summary={'kind':'structural-and-filesystem-fixture (not compiled C#)', 'total':len(results),
         'passed':sum(r['status']=='PASS' for r in results),'failed':sum(r['status']=='FAIL' for r in results),
         'distinct_headless_negative_tests_in_CSharp':len(case_names),'results':results}
out=root/'tests'/'OFFLINE-CHECK-RESULTS.json'
out.write_text(json.dumps(summary,indent=2))
print(f"Offline checks: {summary['passed']}/{summary['total']} passed, {summary['failed']} failed; C# headless test definitions: {len(case_names)}")
if summary['failed']:
    for r in results:
        if r['status']=='FAIL':print('FAILED',r['name'])
raise SystemExit(0 if summary['failed']==0 else 1)
