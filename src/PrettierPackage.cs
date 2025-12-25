using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using CategoryAttribute = System.ComponentModel.CategoryAttribute;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DisplayNameAttribute = System.ComponentModel.DisplayNameAttribute;

namespace PrettierX64
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", Vsix.Version, IconResourceID = 400)]
    [Guid(PackageGuids.guidPrettierPackageString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(OptionPageGrid), "Prettier", "General", 0, 0, true)]
    [ProvideAutoLoad(
        cmdUiContextGuid: VSConstants.UICONTEXT.SolutionExistsAndNotBuildingAndNotDebugging_string,
        flags: PackageAutoLoadFlags.BackgroundLoad
    )]
    public sealed class PrettierPackage : AsyncPackage
    {
        internal static PrettierPackage Instance { get; private set; }

        internal NodeProcess Node { get; private set; }

        internal RunningDocumentTable _runningDocTable;
        internal OptionPageGrid optionPage;

        internal readonly HashSet<string> IncludedExtensionSet = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        protected override async System.Threading.Tasks.Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress
        )
        {
            Instance = this;

            Logger.Log("PrettierPackage.InitializeAsync starting...");

            await base.InitializeAsync(cancellationToken, progress);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _runningDocTable = new RunningDocumentTable(this);
            _runningDocTable.Advise(new RunningDocTableEventsHandler(this));

            optionPage = (OptionPageGrid)GetDialogPage(typeof(OptionPageGrid));
            UpdateIncludedExtensions(optionPage.IncludedExtensions);

            Node = new NodeProcess(this);

            if (!Node.IsReadyToExecute())
            {
                // Fire-and-forget install with logging
                JoinableTaskFactory
                    .RunAsync(async () =>
                    {
                        try
                        {
                            await Node.EnsurePackageInstalledAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(ex);
                        }
                    })
                    .FileAndForget("PrettierX64/EnsurePrettierInstalled");
            }

            // DEV: dump content types to the output window
            // await DumpContentTypesAsync();
        }

        internal void UpdateIncludedExtensions(string raw)
        {
            IncludedExtensionSet.Clear();

            if (string.IsNullOrWhiteSpace(raw))
                return;

            string[] parts = raw.Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );

            for (int i = 0; i < parts.Length; i++)
            {
                string ext = parts[i].Trim();
                if (ext.Length == 0)
                    continue;

                if (ext[0] == '.')
                    ext = ext.Substring(1);

                ext = ext.ToLowerInvariant();

                IncludedExtensionSet.Add(ext);
            }
        }

        internal async Task DumpContentTypesAsync()
        {
            // Get the component model (MEF host)
            if (!(await GetServiceAsync(typeof(SComponentModel)) is IComponentModel componentModel))
            {
                Logger.Log("DumpContentTypesAsync: Could not get IComponentModel.");
                return;
            }

            IContentTypeRegistryService registry =
                componentModel.GetService<IContentTypeRegistryService>();
            if (registry == null)
            {
                Logger.Log("DumpContentTypesAsync: Could not get IContentTypeRegistryService.");
                return;
            }

            var types = registry.ContentTypes.OrderBy(ct => ct.TypeName).ToList();

            Logger.Log("=== Dumping Visual Studio content types ===");
            foreach (IContentType ct in types)
            {
                string baseTypes = string.Join(", ", ct.BaseTypes.Select(b => b.TypeName));

                Logger.Log(
                    string.Format(
                        "ContentType: {0}; BaseTypes: {1}",
                        ct.TypeName,
                        string.IsNullOrEmpty(baseTypes) ? "(none)" : baseTypes
                    )
                );
            }
            Logger.Log("=== End content types dump ===");
        }
    }

    public class OptionPageGrid : DialogPage
    {
        [Category("Prettier")]
        [DisplayName("Format On Save")]
        [Description("Run Prettier whenever a file is saved")]
        public bool FormatOnSave { get; set; }

        [Category("Prettier")]
        [DisplayName("File extensions to format")]
        [Description(
            "Comma-separated list of extensions without dots. Example: js,jsx,ts,tsx,json,css,scss,less,html,htm,md,markdown,xml,yml,yaml"
        )]
        public string IncludedExtensions { get; set; } =
            "js,jsx,ts,tsx,json,css,scss,less,html,htm,md,markdown,xml,yml,yaml";

        // Keep in sync with message below until interpolated strings
        // can be used in the Description.
        internal const string PrettierFallbackVersion = "latest";

        [Category("Prettier")]
        [DisplayName("Prettier version for embedded usage")]
        [Description(
            "This extension downloads its own install of Prettier to run if "
                + "Prettier is not installed via npm in your current project. "
                + "Leave this set to 'latest' to always use the latest Prettier from npm, "
                + "or enter an explicit version such as '3.7.3' to pin the embedded version."
        )]
        public string EmbeddedVersion { get; set; } = PrettierFallbackVersion;

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);

            PrettierPackage pkg = PrettierPackage.Instance;
            pkg?.UpdateIncludedExtensions(IncludedExtensions);
        }
    }
}
