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
        // Log unexpected runtime failures in the user's local profile instead
        // of showing an unhelpful Just-In-Time debugging dump. Fatal errors
        // are still fatal; they are recorded for diagnosis, not hidden.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppDiagnostics.ReportUiException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppDiagnostics.Record(e.ExceptionObject as Exception ??
                new Exception("Unknown non-UI exception"), "fatal-background");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppDiagnostics.Record(e.Exception, "unobserved-task");
            e.SetObserved();
        };
        Application.Run(new MainForm(args));
        return 0;
    }
}
