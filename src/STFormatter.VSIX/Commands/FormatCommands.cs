using System;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace STFormatter.VSIX.Commands;

internal sealed class FormatDocumentCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new("a3b4c5d6-e7f8-4a5b-9c0d-1e2f3a4b5c6d");

    private readonly AsyncPackage package;

    private FormatDocumentCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(Execute, menuCommandID);
        commandService.AddCommand(menuItem);
    }

    public static FormatDocumentCommand Instance { get; private set; } = null!;

    public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        Instance = new FormatDocumentCommand(package, commandService!);
    }

    private void Execute(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        FormatHelper.FormatDocument(package);
    }
}

internal sealed class FormatSelectionCommand
{
    public const int CommandId = 0x0101;
    public static readonly Guid CommandSet = new("a3b4c5d6-e7f8-4a5b-9c0d-1e2f3a4b5c6d");

    private readonly AsyncPackage package;

    private FormatSelectionCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(Execute, menuCommandID);
        commandService.AddCommand(menuItem);
    }

    public static FormatSelectionCommand Instance { get; private set; } = null!;

    public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        Instance = new FormatSelectionCommand(package, commandService!);
    }

    private void Execute(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        FormatHelper.FormatSelection(package);
    }
}
