using System.ComponentModel.Composition;
using System.IO;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace PrettierX64
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("code")] // Covers JS, TS, CSS, JSON, etc.
    [ContentType("text")] // Covers Markdown, YAML, and fallbacks
    [TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
    internal sealed class CommandRegistration : IVsTextViewCreationListener
    {
#pragma warning disable RCS1170 // Use read-only auto-implemented property; MEF requirement
        [Import]
        private IVsEditorAdaptersFactoryService AdaptersFactory { get; set; }

        [Import]
        private ITextDocumentFactoryService DocumentService { get; set; }

        [Import]
        private ITextBufferUndoManagerProvider UndoProvider { get; set; }
#pragma warning restore RCS1170 // Use read-only auto-implemented property; MEF requirement

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            IWpfTextView view = AdaptersFactory.GetWpfTextView(textViewAdapter);

#if DEBUG
            // DEBUG: Log the content type to find out what HTML is using
            Logger.Log($"View Created. ContentType: {view.TextDataModel.ContentType.TypeName}");
#endif

            if (
                !DocumentService.TryGetTextDocument(
                    view.TextDataModel.DocumentBuffer,
                    out ITextDocument doc
                )
            )
                return;

            PrettierPackage pkg = PrettierPackage.Instance;
            if (pkg == null || pkg.optionPage == null)
                return;

            string filePath = doc.FilePath;
            if (string.IsNullOrEmpty(filePath))
                return;

            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext))
                return;

            // normalize extension (no dot, lowercase)
            if (ext[0] == '.')
                ext = ext.Substring(1);

            ext = ext.ToLowerInvariant();

            // Only attach Prettier for extensions the user configured
            if (!pkg.IncludedExtensionSet.Contains(ext))
                return;

            ITextBufferUndoManager undoManager = UndoProvider.GetTextBufferUndoManager(
                view.TextBuffer
            );

            var cmd = new PrettierCommand(view, undoManager, doc.Encoding, doc.FilePath);
            view.Properties.AddProperty("prettierCommand", cmd);

            AddCommandFilter(textViewAdapter, cmd);
        }

        private void AddCommandFilter(IVsTextView textViewAdapter, BaseCommand command)
        {
            textViewAdapter.AddCommandFilter(command, out IOleCommandTarget next);
            command.Next = next;
        }
    }
}
