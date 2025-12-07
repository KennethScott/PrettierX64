using System;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace PrettierX64
{
    internal sealed class RunningDocTableEventsHandler : IVsRunningDocTableEvents3
    {
        private readonly PrettierPackage _package;

        public RunningDocTableEventsHandler(PrettierPackage package)
        {
            _package = package;
        }

        public int OnAfterFirstDocumentLock(
            uint docCookie,
            uint dwRDTLockType,
            uint dwReadLocksRemaining,
            uint dwEditLocksRemaining
        ) => VSConstants.S_OK;

        public int OnBeforeLastDocumentUnlock(
            uint docCookie,
            uint dwRDTLockType,
            uint dwReadLocksRemaining,
            uint dwEditLocksRemaining
        ) => VSConstants.S_OK;

        public int OnAfterSave(uint docCookie) => VSConstants.S_OK;

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;

        public int OnBeforeDocumentWindowShow(
            uint docCookie,
            int fFirstShow,
            IVsWindowFrame pFrame
        ) => VSConstants.S_OK;

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) =>
            VSConstants.S_OK;

        public int OnAfterAttributeChangeEx(
            uint docCookie,
            uint grfAttribs,
            IVsHierarchy pHierOld,
            uint itemidOld,
            string pszMkDocumentOld,
            IVsHierarchy pHierNew,
            uint itemidNew,
            string pszMkDocumentNew
        ) => VSConstants.S_OK;

        public int OnBeforeSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_package.optionPage.FormatOnSave)
            {
                RunningDocumentInfo docInfo = _package._runningDocTable.GetDocumentInfo(docCookie);
                Logger.Log($"OnBeforeSave: {docInfo.Moniker}");

                Document doc = null;
                foreach (Document d in _package._dte.Documents)
                {
                    if (
                        string.Equals(
                            d.FullName,
                            docInfo.Moniker,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        doc = d;
                        break;
                    }
                }

                if (doc != null)
                {
                    IVsTextView vsTextView = GetIVsTextView(doc.FullName);
                    if (vsTextView == null)
                    {
                        return VSConstants.S_OK;
                    }

                    IWpfTextView wpfTextView = GetWpfTextView(vsTextView);
                    if (wpfTextView == null)
                    {
                        return VSConstants.S_OK;
                    }

                    if (
                        wpfTextView.Properties.TryGetProperty<PrettierCommand>(
                            "prettierCommand",
                            out PrettierCommand cmd
                        )
                    )
                    {
                        Logger.Log("OnBeforeSave: invoking MakePrettierAsync via Run");
                        ThreadHelper.JoinableTaskFactory.Run(() => cmd.MakePrettierAsync());
                        Logger.Log("OnBeforeSave: MakePrettierAsync completed");
                    }
                    else
                    {
                        Logger.Log("OnBeforeSave: no PrettierCommand found for this view");
                    }
                }
            }
            return VSConstants.S_OK;
        }

        private IVsTextView GetIVsTextView(string filePath)
        {
            return VsShellUtilities.IsDocumentOpen(
                _package,
                filePath,
                Guid.Empty,
                out _,
                out _,
                out IVsWindowFrame windowFrame
            )
                ? VsShellUtilities.GetTextView(windowFrame)
                : null;
        }

        private static IWpfTextView GetWpfTextView(IVsTextView vTextView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(vTextView is IVsUserData userData))
                return null;

            IWpfTextView view = null;
            if (userData != null)
            {
                Guid guidViewHost = Microsoft.VisualStudio.Editor.DefGuidList.guidIWpfTextViewHost;
                userData.GetData(ref guidViewHost, out object holder);
                var viewHost = (IWpfTextViewHost)holder;
                view = viewHost.TextView;
            }

            return view;
        }
    }
}
