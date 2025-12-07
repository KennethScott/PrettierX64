using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PrettierX64
{
    internal static class Logger
    {
        private static IVsOutputWindowPane _pane;
        private static IVsOutputWindow _output;

        // Stable GUID so we get the same pane each session
        private static readonly Guid PaneGuid = new Guid("8AF8C5D1-3A7D-4A07-BB09-6F6C0D1A9A3E");

        private static readonly Regex AnsiRegex = new Regex(
            @"\x1B\[[0-9;]*m",
            RegexOptions.Compiled
        );

        public static void Log(object message)
        {
            try
            {
                string text = message?.ToString() ?? string.Empty;
                text = StripAnsi(text);

                // If the package isn't ready yet, fall back to Debug
                PrettierPackage pkg = PrettierPackage.Instance;
                if (pkg == null)
                {
                    Debug.WriteLine(text);
                    return;
                }

                // Use the *package's* JoinableTaskFactory so work is tracked (VSSDK007)
                pkg.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            await pkg.JoinableTaskFactory.SwitchToMainThreadAsync();

                            if (!EnsurePane())
                                return;

                            _pane.OutputStringThreadSafe(
                                $"{DateTime.Now}: {text}{Environment.NewLine}"
                            );
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex);
                        }
                    })
                    .FileAndForget("PrettierX64/Logger");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        [SuppressMessage(
            "Usage",
            "VSTHRD010:Invoke single-threaded types on Main thread",
            Justification = "EnsurePane is only called after switching to the UI thread in Logger.Log's JTF delegate."
        )]
        private static bool EnsurePane()
        {
            if (_pane != null)
                return true;

            if (_output == null)
            {
                _output = (IVsOutputWindow)
                    ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow));

                if (_output == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "PrettierX64.Logger: SVsOutputWindow unavailable."
                    );
                    return false;
                }
            }

            // static readonly field can't be passed by ref, so use a local copy
            Guid guid = PaneGuid;

            _output.CreatePane(ref guid, Vsix.Name, fInitVisible: 1, fClearWithSolution: 1);

            _output.GetPane(ref guid, out _pane);

            return _pane != null;
        }

        private static string StripAnsi(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return AnsiRegex.Replace(input, string.Empty);
        }
    }
}
