using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;

namespace PrettierX64
{
    internal sealed class PrettierCommand : BaseCommand
    {
        private readonly Guid _commandGroup = PackageGuids.guidPrettierPackageCmdSet;
        private const uint _commandId = PackageIds.PrettierCommandId;

        private readonly IWpfTextView _view;
        private readonly ITextBufferUndoManager _undoManager;
        private readonly NodeProcess _node;
        private readonly Encoding _encoding;
        private readonly string _filePath;
        private bool _isRunning;

        public PrettierCommand(
            IWpfTextView view,
            ITextBufferUndoManager undoManager,
            Encoding encoding,
            string filePath
        )
        {
            _view = view;
            _undoManager = undoManager;
            _encoding = encoding;
            _filePath = filePath;

            _node = PrettierPackage.Instance?.Node;
        }

        public override int Exec(
            ref Guid pguidCmdGroup,
            uint nCmdID,
            uint nCmdexecopt,
            IntPtr pvaIn,
            IntPtr pvaOut
        )
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == _commandGroup && nCmdID == _commandId)
            {
                if (_node?.IsReadyToExecute() == true)
                {
                    PrettierPackage
                        .Instance?.JoinableTaskFactory.RunAsync(async () =>
                        {
                            try
                            {
                                await MakePrettierAsync();
                            }
                            catch (Exception ex)
                            {
                                Logger.Log(ex);
                            }
                        })
                        .FileAndForget("PrettierX64/MakePrettier");
                }

                return VSConstants.S_OK;
            }

            return Next != null
                ? Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
                : (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        public async Task<bool> MakePrettierAsync()
        {
            // Prevent re-entrancy / overlapping runs
            if (_isRunning)
            {
                Logger.Log(
                    $"Prettier: skipping run for '{_filePath}' because one is already in progress."
                );
                return false;
            }

            var sw = Stopwatch.StartNew();

            try
            {
                _isRunning = true;

                string input = _view.TextDataModel.DocumentBuffer.CurrentSnapshot.GetText();
                string output = await _node.ExecuteProcessAsync(input, _encoding, _filePath);

                VirtualSnapshotPoint snapshotPoint = _view.Selection.ActivePoint;

                if (string.IsNullOrEmpty(output))
                {
                    sw.Stop();
                    Logger.Log(
                        $"Prettier: no output for '{_filePath}' (elapsed {sw.ElapsedMilliseconds} ms)."
                    );
                    return false;
                }

                if (input == output)
                {
                    sw.Stop();
                    Logger.Log(
                        $"Prettier: no changes for '{_filePath}' ({sw.ElapsedMilliseconds} ms)."
                    );
                    return false;
                }

                using (ITextEdit edit = _view.TextBuffer.CreateEdit())
                using (
                    ITextUndoTransaction undo =
                        _undoManager.TextBufferUndoHistory.CreateTransaction("Make Prettier")
                )
                {
                    edit.Replace(0, _view.TextBuffer.CurrentSnapshot.Length, output);
                    edit.Apply();

                    undo.Complete();
                }

                ITextSnapshot currSnapShot = _view.TextBuffer.CurrentSnapshot;
                var newSnapshotPoint = new SnapshotPoint(
                    currSnapShot,
                    Math.Min(snapshotPoint.Position.Position, currSnapShot.Length)
                );
                _view.Caret.MoveTo(newSnapshotPoint);
                _view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(newSnapshotPoint, 0));

                // Re-save using the view properties for the flag
                if (
                    _view.TextDataModel.DocumentBuffer.Properties.TryGetProperty(
                        typeof(ITextDocument),
                        out ITextDocument doc
                    )
                )
                {
                    try
                    {
                        _view.Properties.AddProperty("PrettierFormatting", true);
                        await PrettierPackage.Instance.JoinableTaskFactory.SwitchToMainThreadAsync();
                        doc.Save();
                    }
                    finally
                    {
                        _view.Properties.RemoveProperty("PrettierFormatting");
                    }
                }

                sw.Stop();
                Logger.Log($"Prettier: formatted '{_filePath}' in {sw.ElapsedMilliseconds} ms.");

                return true;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Log(
                    $"Prettier: error formatting '{_filePath}' after {sw.ElapsedMilliseconds} ms."
                );

                Logger.Log(ex);
                return false;
            }
            finally
            {
                _isRunning = false;
            }
        }

        public override int QueryStatus(
            ref Guid pguidCmdGroup,
            uint cCmds,
            OLECMD[] prgCmds,
            IntPtr pCmdText
        )
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == _commandGroup && prgCmds[0].cmdID == _commandId)
            {
                if (_node != null)
                {
                    if (_node.IsReadyToExecute())
                    {
                        SetText(pCmdText, "Make Prettier");
                        prgCmds[0].cmdf =
                            (uint)OLECMDF.OLECMDF_ENABLED | (uint)OLECMDF.OLECMDF_SUPPORTED;
                    }
                    else
                    {
                        SetText(pCmdText, "Make Prettier (installing npm modules...)");
                        prgCmds[0].cmdf = (uint)OLECMDF.OLECMDF_SUPPORTED;
                    }
                }

                return VSConstants.S_OK;
            }

            return Next != null
                ? Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText)
                : (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        private static void SetText(IntPtr pCmdTextInt, string text)
        {
            try
            {
                var pCmdText = (OLECMDTEXT)Marshal.PtrToStructure(pCmdTextInt, typeof(OLECMDTEXT));
                char[] menuText = text.ToCharArray();

                // Get the offset to the rgsz param.  This is where we will stuff our text
                IntPtr offset = Marshal.OffsetOf(typeof(OLECMDTEXT), "rgwz");
                IntPtr offsetToCwActual = Marshal.OffsetOf(typeof(OLECMDTEXT), "cwActual");

                // The max chars we copy is our string, or one less than the buffer size,
                // since we need a null at the end.
                int maxChars = Math.Min((int)pCmdText.cwBuf - 1, menuText.Length);

                Marshal.Copy(menuText, 0, (IntPtr)((long)pCmdTextInt + (long)offset), maxChars);

                // append a null character
                Marshal.WriteInt16((IntPtr)((long)pCmdTextInt + (long)offset + (maxChars * 2)), 0);

                // write out the length +1 for the null char
                Marshal.WriteInt32(
                    (IntPtr)((long)pCmdTextInt + (long)offsetToCwActual),
                    maxChars + 1
                );
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
    }
}
