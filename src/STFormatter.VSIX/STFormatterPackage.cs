using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace STFormatter.VSIX;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(STFormatterPackage.PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(Options.STFormatterOptionPage), "TwinCAT", "ST Formatter", 0, 0, true)]
[ProvideProfile(typeof(Options.STFormatterOptionPage), "TwinCAT", "ST Formatter", 0, 0, true)]
[InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
public sealed class STFormatterPackage : AsyncPackage
{
    public const string PackageGuidString = "8d2e3a4f-b5c1-4a7e-9f3d-2c1e5b6a9d4e";

    public static STFormatterPackage? Instance { get; private set; }

    protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        Instance = this;
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await Commands.FormatDocumentCommand.InitializeAsync(this);
        await Commands.FormatSelectionCommand.InitializeAsync(this);
    }
}
