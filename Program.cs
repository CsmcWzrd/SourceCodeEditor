using System;
using System.Windows.Forms;

namespace SourceCodeEditor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--self-test-conpty", StringComparison.OrdinalIgnoreCase))
            return ConPtySelfTest.Run(args.Length > 1 ? args[1] : null);
        if (args.Length > 0 && string.Equals(args[0], "--self-test-negative", StringComparison.OrdinalIgnoreCase))
            return NegativeSelfTest.Run(args.Length > 1 ? args[1] : null);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
        return 0;
    }
}
