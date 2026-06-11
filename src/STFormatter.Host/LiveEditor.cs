using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;
using STFormatter.Core.Formatting;

namespace STFormatter.Host;

internal static class LiveEditor
{
    // COM IServiceProvider for QueryService (different from System.IServiceProvider)
    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    private static readonly Guid SID_SVsFileChangeEx = new Guid("9BC72973-194A-4EA8-B4D5-AFB0B0D0DCB1");
    private static readonly Guid IID_IVsFileChangeEx = new Guid("9BC72973-194A-4EA8-B4D5-AFB0B0D0DCB1");
    private static readonly Guid SID_SVsRunningDocumentTable = new Guid("7D9C954B-1398-4706-B9C1-3E4E36E7C9DA");
    private static readonly Guid IID_IVsRunningDocumentTable = new Guid("A928AA21-EA77-47AC-8A07-355206C94BDD");

    // Get COM IServiceProvider from DTE object via QueryInterface
    private static IComServiceProvider? GetComServiceProvider(EnvDTE.DTE dte)
    {
        try
        {
            // Try direct cast first (works on STA thread when DTE is local)
            var sp = dte as IComServiceProvider;
            if (sp != null)
            {
                Log("GetComServiceProvider: Direct cast succeeded");
                return sp;
            }

            // Try QueryInterface for IServiceProvider (COM IID)
            Guid ispGuid = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
            IntPtr punk = IntPtr.Zero;
            int hr = Marshal.QueryInterface(Marshal.GetIUnknownForObject(dte), ref ispGuid, out punk);
            Log($"GetComServiceProvider: QueryInterface hr=0x{hr:X8}");
            if (hr == 0 && punk != IntPtr.Zero)
            {
                var result = Marshal.GetObjectForIUnknown(punk) as IComServiceProvider;
                Marshal.Release(punk);
                Log($"GetComServiceProvider: QueryInterface result={(result != null ? "OK" : "null")}");
                return result;
            }

            // Fallback: try System.IServiceProvider
            var sysSp = dte as System.IServiceProvider;
            if (sysSp != null)
            {
                Log("GetComServiceProvider: System.IServiceProvider succeeded, wrapping");

                // System.IServiceProvider can be used directly for GetService
                // but we need IComServiceProvider for QueryService
                // Try getting it via COM interop
                try
                {
                    var unknown = Marshal.GetIUnknownForObject(dte);
                    hr = Marshal.QueryInterface(unknown, ref ispGuid, out punk);
                    Marshal.Release(unknown);
                    if (hr == 0 && punk != IntPtr.Zero)
                    {
                        var result = Marshal.GetObjectForIUnknown(punk) as IComServiceProvider;
                        Marshal.Release(punk);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Log($"GetComServiceProvider: Fallback QueryInterface failed: {ex.Message}");
                }
            }

            Log("GetComServiceProvider: All methods failed - IServiceProvider not available");
            return null;
        }
        catch (Exception ex)
        {
            Log($"GetComServiceProvider: FAILED: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    // Get a VS service via COM IServiceProvider.QueryService
    private static T? GetVsService<T>(IComServiceProvider sp, Guid serviceGuid, Guid interfaceGuid) where T : class
    {
        try
        {
            int hr = sp.QueryService(ref serviceGuid, ref interfaceGuid, out IntPtr punk);
            if (hr != 0 || punk == IntPtr.Zero)
            {
                Log($"GetVsService<{typeof(T).Name}>: QueryService hr=0x{hr:X8}");
                return null;
            }

            var obj = Marshal.GetObjectForIUnknown(punk);
            Marshal.Release(punk);
            var result = obj as T;
            if (result == null)
            {
                Log($"GetVsService<{typeof(T).Name}>: COM object type={obj.GetType().FullName}, not castable");
                Marshal.ReleaseComObject(obj);
            }
            return result;
        }
        catch (Exception ex)
        {
            Log($"GetVsService<{typeof(T).Name}>: FAILED: {ex.Message}");
            return null;
        }
    }

    // Alternative: get VS service via System.IServiceProvider (works on STA thread)
    private static T? GetVsServiceViaSystemSP<T>(System.IServiceProvider sp, Type serviceType) where T : class
    {
        try
        {
            var obj = sp.GetService(serviceType);
            if (obj == null) return null;
            var result = obj as T;
            if (result == null && obj != null)
            {
                // Try marshaling - the service might be a COM object that needs QueryInterface
                try
                {
                    var unknown = Marshal.GetIUnknownForObject(obj);
                    var resultObj = Marshal.GetObjectForIUnknown(unknown);
                    Marshal.Release(unknown);
                    result = resultObj as T;
                    if (result == null) Marshal.ReleaseComObject(resultObj);
                }
                catch { }
            }
            return result;
        }
        catch (Exception ex)
        {
            Log($"GetVsServiceViaSystemSP<{typeof(T).Name}>: FAILED: {ex.Message}");
            return null;
        }
    }

    // Tier 2: File write with IVsFileChangeEx suppression + IVsPersistDocData2.ReloadDocData
    public static bool TryFormatViaRdtFileWrite(EnvDTE.DTE dte, string filePath,
        string formattedXml, FormattingEngine engine)
    {
        Log("TryFormatViaRdtFileWrite: Starting...");
        try
        {
            // Try multiple approaches to get IServiceProvider
            IComServiceProvider? comSp = GetComServiceProvider(dte);
            System.IServiceProvider? sysSp = dte as System.IServiceProvider;

            Log($"TryFormatViaRdtFileWrite: comSp={(comSp != null ? "OK" : "null")}, sysSp={(sysSp != null ? "OK" : "null")}");

            if (comSp == null && sysSp == null)
            {
                Log("TryFormatViaRdtFileWrite: No IServiceProvider available");
                return false;
            }

            // Get IVsFileChangeEx (suppress file change notifications)
            IVsFileChangeEx? fileChangeEx = null;
            if (comSp != null)
            {
                fileChangeEx = GetVsService<IVsFileChangeEx>(comSp, SID_SVsFileChangeEx, IID_IVsFileChangeEx);
            }
            if (fileChangeEx == null && sysSp != null)
            {
                try
                {
                    var obj = sysSp.GetService(typeof(SVsFileChangeEx));
                    if (obj != null)
                    {
                        fileChangeEx = obj as IVsFileChangeEx;
                        if (fileChangeEx != null)
                            Log("TryFormatViaRdtFileWrite: IVsFileChangeEx obtained via System.IServiceProvider");
                        else
                        {
                            Log($"TryFormatViaRdtFileWrite: GetService returned {obj.GetType().FullName}, trying Marshal");
                            try
                            {
                                var unk = Marshal.GetIUnknownForObject(obj);
                                fileChangeEx = Marshal.GetObjectForIUnknown(unk) as IVsFileChangeEx;
                                Marshal.Release(unk);
                                if (fileChangeEx != null)
                                    Log("TryFormatViaRdtFileWrite: IVsFileChangeEx obtained via Marshal fallback");
                            }
                            catch (Exception ex2)
                            {
                                Log($"TryFormatViaRdtFileWrite: Marshal fallback failed: {ex2.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"TryFormatViaRdtFileWrite: System.IServiceProvider.GetService for IVsFileChangeEx failed: {ex.Message}");
                }
            }

            bool ignoreActive = false;
            if (fileChangeEx != null)
            {
                int hr = fileChangeEx.IgnoreFile(0, filePath, 1);
                Log($"TryFormatViaRdtFileWrite: IgnoreFile(1) hr=0x{hr:X8}");
                ignoreActive = (hr == 0);
            }
            else
            {
                Log("TryFormatViaRdtFileWrite: IVsFileChangeEx not available");
            }

            try
            {
                // Create backup
                string backupPath = filePath + ".bak";
                File.Copy(filePath, backupPath, true);
                Log($"TryFormatViaRdtFileWrite: Backup created");

                // Write formatted file while notifications are suppressed
                File.WriteAllText(filePath, formattedXml, new UTF8Encoding(false));
                Log("TryFormatViaRdtFileWrite: File written to disk");

                // Re-enable file change notifications BEFORE triggering reload
                // This way the editor will see the change notification
                if (fileChangeEx != null && ignoreActive)
                {
                    int hr = fileChangeEx.IgnoreFile(0, filePath, 0);
                    Log($"TryFormatViaRdtFileWrite: IgnoreFile(0) RE-ENABLED early hr=0x{hr:X8}");
                    ignoreActive = false; // Already restored
                }

                // Try to reload via RDT
                bool reloaded = false;

                // Try IVsRunningDocumentTable via QueryService
                if (comSp != null)
                {
                    var rdt = GetVsService<IVsRunningDocumentTable>(comSp, SID_SVsRunningDocumentTable, IID_IVsRunningDocumentTable);
                    if (rdt != null)
                    {
                        Log("TryFormatViaRdtFileWrite: IVsRunningDocumentTable obtained via QueryService");
                        reloaded = ReloadDocDataViaRdt(rdt, filePath);
                    }
                    else
                    {
                        Log("TryFormatViaRdtFileWrite: IVsRunningDocumentTable not available via QueryService");
                    }
                }

                // Try IVsRunningDocumentTable via System.IServiceProvider
                if (!reloaded && sysSp != null)
                {
                    var rdt = GetVsServiceViaSystemSP<IVsRunningDocumentTable>(sysSp, typeof(SVsRunningDocumentTable));
                    if (rdt != null)
                    {
                        Log("TryFormatViaRdtFileWrite: IVsRunningDocumentTable obtained via System.IServiceProvider");
                        reloaded = ReloadDocDataViaRdt(rdt, filePath);
                    }
                    else
                    {
                        Log("TryFormatViaRdtFileWrite: IVsRunningDocumentTable not available via System.IServiceProvider");
                    }
                }

                if (reloaded)
                {
                    Log("TryFormatViaRdtFileWrite: Document reloaded - SUCCESS");
                    return true;
                }

                // Trigger file change notification now that notifications are re-enabled
                if (fileChangeEx != null)
                {
                    int hr = fileChangeEx.SyncFile(filePath);
                    Log($"TryFormatViaRdtFileWrite: SyncFile hr=0x{hr:X8}");
                }

                Log("TryFormatViaRdtFileWrite: File written and change notification sent");
                return true;
            }
            finally
            {
                // Make sure notifications are restored
                if (fileChangeEx != null && ignoreActive)
                {
                    int hr = fileChangeEx.IgnoreFile(0, filePath, 0);
                    Log($"TryFormatViaRdtFileWrite: IgnoreFile(0) cleanup hr=0x{hr:X8}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaRdtFileWrite: FAILED: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    private static bool ReloadDocDataViaRdt(IVsRunningDocumentTable rdt, string filePath)
    {
        Log("ReloadDocDataViaRdt: Starting...");
        try
        {
            int hr = rdt.FindAndLockDocument(
                (uint)_VSRDTFLAGS.RDT_NoLock,
                filePath,
                out IVsHierarchy hier,
                out uint itemId,
                out IntPtr docDataPtr,
                out uint cookie);

            Log($"ReloadDocDataViaRdt: FindAndLockDocument hr=0x{hr:X8}");

            if (hr != 0 || docDataPtr == IntPtr.Zero)
            {
                Log("ReloadDocDataViaRdt: Document not found in RDT");
                return false;
            }

            try
            {
                var docData = Marshal.GetObjectForIUnknown(docDataPtr);
                Log($"ReloadDocDataViaRdt: DocData type={docData?.GetType().FullName ?? "null"}");

                if (docData is IVsPersistDocData2 pdd2)
                {
                    Log("ReloadDocDataViaRdt: IVsPersistDocData2 available");

                    hr = pdd2.IsDocDataReloadable(out int reloadable);
                    Log($"ReloadDocDataViaRdt: IsDocDataReloadable hr=0x{hr:X8}, reloadable={reloadable}");

                    if (hr == 0 && reloadable != 0)
                    {
                        hr = pdd2.ReloadDocData(1); // RDD_IgnoreFileChange
                        Log($"ReloadDocDataViaRdt: ReloadDocData(1) hr=0x{hr:X8}");

                        if (hr == 0)
                        {
                            Log("ReloadDocDataViaRdt: SUCCESS via IVsPersistDocData2");
                            return true;
                        }

                        hr = pdd2.ReloadDocData(0);
                        Log($"ReloadDocDataViaRdt: ReloadDocData(0) hr=0x{hr:X8}");
                        if (hr == 0)
                        {
                            Log("ReloadDocDataViaRdt: SUCCESS via IVsPersistDocData2 (default flags)");
                            return true;
                        }
                    }
                }

                if (docData is IVsPersistDocData pdd)
                {
                    Log("ReloadDocDataViaRdt: IVsPersistDocData available");
                    hr = pdd.IsDocDataReloadable(out int reloadable);
                    Log($"ReloadDocDataViaRdt: IsDocDataReloadable hr=0x{hr:X8}, reloadable={reloadable}");

                    if (hr == 0 && reloadable != 0)
                    {
                        hr = pdd.ReloadDocData(1);
                        Log($"ReloadDocDataViaRdt: ReloadDocData(1) hr=0x{hr:X8}");
                        if (hr == 0) return true;

                        hr = pdd.ReloadDocData(0);
                        Log($"ReloadDocDataViaRdt: ReloadDocData(0) hr=0x{hr:X8}");
                        if (hr == 0) return true;
                    }
                }

                Log("ReloadDocDataViaRdt: No persist interface supports reload");
                return false;
            }
            finally
            {
                Marshal.Release(docDataPtr);
            }
        }
        catch (Exception ex)
        {
            Log($"ReloadDocDataViaRdt: FAILED: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    // Tier 3: DTE.ExecuteCommand + Clipboard
    // Must run on STA thread. Caller must ensure STA context.
    public static bool TryFormatViaExecuteCommand(EnvDTE.DTE dte, string filePath,
        string? formattedDecl, string? formattedImpl,
        out string? outOriginal, out string? outFormatted)
    {
        outOriginal = null;
        outFormatted = null;
        Log("TryFormatViaExecuteCommand: Starting...");
        try
        {
            if (dte.ActiveDocument == null)
            {
                Log("TryFormatViaExecuteCommand: No active document");
                return false;
            }

            try
            {
                dte.Commands.Item("Edit.SelectAll", -1);
                Log("TryFormatViaExecuteCommand: Edit.SelectAll command found");
            }
            catch
            {
                Log("TryFormatViaExecuteCommand: Edit.SelectAll not found");
                return false;
            }

            bool undoContextOpened = false;
            try
            {
                if (!dte.UndoContext.IsOpen)
                {
                    dte.UndoContext.Open("Format ST Document");
                    undoContextOpened = true;
                    Log("TryFormatViaExecuteCommand: UndoContext opened");
                }
            }
            catch (Exception ex)
            {
                Log($"TryFormatViaExecuteCommand: UndoContext.Open failed: {ex.Message}");
            }

            try
            {
                dte.ActiveDocument.Activate();
                Log("TryFormatViaExecuteCommand: Document activated");

                // Read the current text from the active section via clipboard
                string? savedClipboard = null;
                try { savedClipboard = GetClipboardText(); } catch { }

                // SelectAll + Copy to read current text
                dte.ExecuteCommand("Edit.SelectAll", "");
                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Copy", "");
                System.Threading.Thread.Sleep(100);

                string currentText = GetClipboardText() ?? "";
                Log($"TryFormatViaExecuteCommand: Read {currentText.Length} chars from active section");

                if (string.IsNullOrEmpty(currentText))
                {
                    Log("TryFormatViaExecuteCommand: Empty text, cannot format");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    return false;
                }

                // Format the text inline — decide declaration vs implementation based on content
                var engine = new FormattingEngine(STFormatter.UI.SettingsManager.Current);
                bool isDecl = TwinCatXmlFormatter.LooksLikeDeclaration(currentText);
                Log($"TryFormatViaExecuteCommand: Detected as {(isDecl ? "Declaration" : "Implementation")}");

                // Tell the user about syntax errors instead of silently doing
                // nothing (the formatter refuses to reformat unparseable code).
                bool parsesCleanly = isDecl
                    ? engine.DeclarationParsesCleanly(currentText)
                    : engine.BodyParsesCleanly(currentText);
                if (!parsesCleanly)
                {
                    Log("TryFormatViaExecuteCommand: section has ST syntax errors - not formatting");
                    try { dte.ExecuteCommand("Edit.SelectionCancel", ""); } catch { }
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    Program.ShowInfoMessage(
                        "Could not format: the active section contains ST syntax errors.\n\n" +
                        "Fix the errors (the TwinCAT compiler will point them out) and try again.");
                    outOriginal = currentText;
                    outFormatted = currentText;
                    return true; // handled - prevent fallback tiers from clobbering the editor
                }

                string? formatted;
                if (isDecl)
                {
                    formatted = engine.FormatDeclaration(currentText);
                    Log($"TryFormatViaExecuteCommand: FormatDeclaration result: [{(formatted ?? "<null>")}]");
                    if (NeedsFullFormatFallback(formatted, currentText))
                    {
                        Log("TryFormatViaExecuteCommand: header-only declaration, trying full Format");
                        formatted = engine.Format(currentText);
                    }
                }
                else
                    formatted = engine.FormatBody(currentText);

                Log($"TryFormatViaExecuteCommand: Formatted output: [{(formatted ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n")}]");

                if (string.IsNullOrEmpty(formatted) || formatted == currentText)
                {
                    Log("TryFormatViaExecuteCommand: No changes needed");
                    try { dte.ExecuteCommand("Edit.SelectionCancel", ""); } catch { }
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { }
                    }
                    if (formatted == currentText) { outOriginal = currentText; outFormatted = formatted; }
                    return formatted == currentText;
                }

                // Paste the formatted text — current selection is already SelectAll
                if (!SetClipboardText(formatted))
                {
                    Log("TryFormatViaExecuteCommand: Failed to set clipboard");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    return false;
                }
                Log($"TryFormatViaExecuteCommand: Clipboard set ({formatted.Length} chars)");

                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Delete", "");
                Log("TryFormatViaExecuteCommand: Edit.Delete executed");

                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Paste", "");
                Log("TryFormatViaExecuteCommand: Edit.Paste executed");

                VerifyPasteAndRetryOnce(dte, formatted, "TryFormatViaExecuteCommand");

                RestoreClipboardAfterPaste(savedClipboard);

                Log("TryFormatViaExecuteCommand: SUCCESS - live edit applied");
                outOriginal = currentText;
                outFormatted = formatted;
                return true;
            }
            finally
            {
                if (undoContextOpened)
                {
                    try { dte.UndoContext.Close(); }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaExecuteCommand: FAILED: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    public static bool TryFormatSelectionViaExecuteCommand(EnvDTE.DTE dte,
        out string? outOriginal, out string? outFormatted)
    {
        outOriginal = null;
        outFormatted = null;

        try
        {
            if (dte.ActiveDocument == null)
            {
                Log("TryFormatSelectionViaExecuteCommand: No active document");
                return false;
            }

            bool undoContextOpened = false;
            try
            {
                try
                {
                    if (!dte.UndoContext.IsOpen)
                    {
                        dte.UndoContext.Open("Format Selected Code");
                        undoContextOpened = true;
                    }
                }
                catch (Exception ex)
                {
                    Log($"TryFormatSelectionViaExecuteCommand: UndoContext.Open failed: {ex.Message}");
                }

                dte.ActiveDocument.Activate();

                string? savedClipboard = null;
                try { savedClipboard = GetClipboardText(); } catch { }

                dte.ExecuteCommand("Edit.Copy", "");
                System.Threading.Thread.Sleep(100);

                string selectedText = GetClipboardText() ?? "";

                if (string.IsNullOrEmpty(selectedText))
                {
                    Log("TryFormatSelectionViaExecuteCommand: No text selected (clipboard empty after Edit.Copy)");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    return false;
                }

                Log($"TryFormatSelectionViaExecuteCommand: Read {selectedText.Length} chars from selection");

                Log($"TryFormatSelectionViaExecuteCommand: Original text: [{selectedText.Replace("\r", "\\r").Replace("\n", "\\n")}]");

                var engine = new FormattingEngine(STFormatter.UI.SettingsManager.Current);

                bool isDeclSelection = TwinCatXmlFormatter.LooksLikeDeclaration(selectedText);
                bool selectionParses = isDeclSelection
                    ? engine.DeclarationParsesCleanly(selectedText)
                    : engine.BodyParsesCleanly(selectedText);
                if (!selectionParses)
                {
                    Log("TryFormatSelectionViaExecuteCommand: selection has ST syntax errors - not formatting");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    outOriginal = selectedText; // signals "had text but could not format"
                    return false;
                }

                string? formatted;
                if (isDeclSelection)
                {
                    Log("TryFormatSelectionViaExecuteCommand: Detected as Declaration");
                    formatted = engine.FormatDeclaration(selectedText);
                    Log($"TryFormatSelectionViaExecuteCommand: FormatDeclaration result: [{(formatted ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n")}]");
                    if (NeedsFullFormatFallback(formatted, selectedText))
                    {
                        Log("TryFormatSelectionViaExecuteCommand: header-only declaration, trying full Format");
                        formatted = engine.Format(selectedText);
                    }
                }
                else
                {
                    Log("TryFormatSelectionViaExecuteCommand: Detected as Implementation (body)");
                    formatted = engine.FormatBody(selectedText);
                    Log($"TryFormatSelectionViaExecuteCommand: FormatBody result: [{(formatted ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n")}]");
                }

                Log($"TryFormatSelectionViaExecuteCommand: Final formatted: [{(formatted ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n")}]");

                if (string.IsNullOrEmpty(formatted))
                {
                    Log("TryFormatSelectionViaExecuteCommand: Formatter returned empty — parse error or unsupported snippet");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    return false;
                }

                if (formatted == selectedText)
                {
                    Log("TryFormatSelectionViaExecuteCommand: No changes needed");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    outOriginal = selectedText;
                    outFormatted = formatted;
                    return true;
                }

                if (!SetClipboardText(formatted))
                {
                    Log("TryFormatSelectionViaExecuteCommand: Failed to set clipboard");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    return false;
                }
                Log($"TryFormatSelectionViaExecuteCommand: Clipboard set ({formatted.Length} chars)");

                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Delete", "");
                Log("TryFormatSelectionViaExecuteCommand: Edit.Delete executed");

                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Paste", "");
                Log("TryFormatSelectionViaExecuteCommand: Edit.Paste executed");

                RestoreClipboardAfterPaste(savedClipboard);

                Log("TryFormatSelectionViaExecuteCommand: SUCCESS - selection formatted");
                outOriginal = selectedText;
                outFormatted = formatted;
                return true;
            }
            finally
            {
                if (undoContextOpened)
                {
                    try { dte.UndoContext.Close(); }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"TryFormatSelectionViaExecuteCommand: FAILED: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Synthetic SendKeys merge with physically held modifiers: a user still
    // holding Ctrl/Shift turns our {HOME} into Ctrl+Shift+Home (select to
    // document start) with destructive follow-up. Wait briefly for release.
    private static void WaitForModifierRelease(int timeoutMs = 1000)
    {
        const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            bool held = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
                        (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
                        (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            if (!held) return;
            System.Threading.Thread.Sleep(25);
        }
        Log("WaitForModifierRelease: modifiers still held after timeout - proceeding");
    }

    public static bool InsertLineAbove(EnvDTE.DTE dte, string text)
    {
        Log($"InsertLineAbove: Inserting text ({text.Length} chars)");
        try
        {
            if (dte.ActiveDocument == null)
            {
                Log("InsertLineAbove: No active document");
                return false;
            }

            WaitForModifierRelease();

            bool undoContextOpened = false;
            try
            {
                if (!dte.UndoContext.IsOpen)
                {
                    dte.UndoContext.Open("Insert Line");
                    undoContextOpened = true;
                }

                dte.ActiveDocument.Activate();

                string? savedClipboard = null;
                try { savedClipboard = GetClipboardText(); } catch { }

                if (!SetClipboardText(text + "\r\n"))
                {
                    Log("InsertLineAbove: Failed to set clipboard");
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch { } }
                    return false;
                }

                try
                {
                    dte.ExecuteCommand("Edit.LineStart", "");
                    System.Threading.Thread.Sleep(30);
                }
                catch
                {
                    Log("InsertLineAbove: Edit.LineStart not available, using Home key");
                    System.Windows.Forms.SendKeys.SendWait("{HOME}");
                    System.Threading.Thread.Sleep(30);
                }

                try
                {
                    dte.ExecuteCommand("Edit.BreakLine", "");
                    System.Threading.Thread.Sleep(30);
                }
                catch
                {
                    Log("InsertLineAbove: Edit.BreakLine not available, using Enter key");
                    System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                    System.Threading.Thread.Sleep(50);
                }

                try
                {
                    var sel = dte.ActiveDocument.Selection as EnvDTE.TextSelection;
                    if (sel != null)
                    {
                        sel.LineUp(false, 1);
                        System.Threading.Thread.Sleep(30);
                    }
                }
                catch
                {
                    Log("InsertLineAbove: TextSelection.LineUp failed, using Up key");
                    System.Windows.Forms.SendKeys.SendWait("{UP}");
                    System.Threading.Thread.Sleep(30);
                }

                System.Windows.Forms.SendKeys.SendWait("{HOME}");
                System.Threading.Thread.Sleep(30);

                dte.ExecuteCommand("Edit.Paste", "");

                RestoreClipboardAfterPaste(savedClipboard);

                Log("InsertLineAbove: SUCCESS");
                return true;
            }
            finally
            {
                if (undoContextOpened)
                {
                    try { dte.UndoContext.Close(); }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"InsertLineAbove: FAILED: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    // "Unchanged" from FormatDeclaration means either "already formatted" (a
    // success - do NOT reformat) or "construct it does not handle" (header-only
    // declarations without VAR/TYPE). Only the latter warrants a full Format()
    // pass, and only when the text parses cleanly - emitting from a tree with
    // parse errors drops content.
    private static bool NeedsFullFormatFallback(string? formatted, string original)
    {
        if (!string.IsNullOrEmpty(formatted) && formatted != original)
            return false;
        return original.IndexOf("VAR", StringComparison.OrdinalIgnoreCase) < 0 &&
               original.IndexOf("TYPE", StringComparison.OrdinalIgnoreCase) < 0 &&
               TwinCatXmlFormatter.ParsesWithoutErrors(original);
    }

    // Tier 4: SendKeys fallback
    public static bool TryFormatViaSendKeys(EnvDTE.DTE dte, string filePath,
        string? formattedDecl, string? formattedImpl,
        out string? outOriginal, out string? outFormatted)
    {
        outOriginal = null;
        outFormatted = null;
        Log("TryFormatViaSendKeys: Starting...");
        try
        {
            if (dte.ActiveDocument == null)
            {
                Log("TryFormatViaSendKeys: No active document");
                return false;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(formattedDecl))
                sb.Append(formattedDecl);
            if (!string.IsNullOrEmpty(formattedImpl))
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(formattedImpl);
            }

            string combined = sb.ToString();
            if (string.IsNullOrEmpty(combined))
            {
                Log("TryFormatViaSendKeys: Empty formatted text");
                return false;
            }

            dte.ActiveDocument.Activate();
            Log("TryFormatViaSendKeys: Document activated");

            // Use Win32 clipboard
            if (!SetClipboardText(combined))
            {
                Log("TryFormatViaSendKeys: Failed to set clipboard");
                return false;
            }

            // Use DTE ExecuteCommand instead of SendKeys for select all and delete
            try
            {
                dte.ExecuteCommand("Edit.SelectAll", "");
                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Delete", "");
                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Paste", "");
                Log("TryFormatViaSendKeys: Commands executed via DTE");
            }
            catch (Exception ex)
            {
                Log($"TryFormatViaSendKeys: DTE commands failed, trying SendKeys: {ex.Message}");
                // Fallback to SendKeys only if DTE commands fail
                try
                {
                    WaitForModifierRelease();
                    System.Windows.Forms.SendKeys.SendWait("^a");
                    System.Threading.Thread.Sleep(100);
                    System.Windows.Forms.SendKeys.SendWait("{DELETE}");
                    System.Threading.Thread.Sleep(100);
                    System.Windows.Forms.SendKeys.SendWait("^v");
                    Log("TryFormatViaSendKeys: SendKeys executed");
                }
                catch (Exception ex2)
                {
                    Log($"TryFormatViaSendKeys: SendKeys also failed: {ex2.Message}");
                    return false;
                }
            }

            // DO NOT write to disk — the editor already has the formatted content.
            // Writing to disk would trigger a "file changed on disk" reload dialog.
            // The editor will persist the changes when the user saves.

            Log("TryFormatViaSendKeys: SUCCESS - live edit applied, no disk write");
            outOriginal = "(pre-format via SendKeys)";
            outFormatted = combined;
            return true;
        }
        catch (Exception ex)
        {
            Log($"TryFormatViaSendKeys: FAILED: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    // Whole-section formatting can verify its own result: re-read the section
    // and compare against what we pasted. If the editor dropped the paste
    // (asynchronous command handling), the section is empty after the Delete -
    // re-paste once. SelectAll after the check leaves the section selected, so
    // a retry paste replaces cleanly.
    private static void VerifyPasteAndRetryOnce(EnvDTE.DTE dte, string expected, string logPrefix)
    {
        try
        {
            System.Threading.Thread.Sleep(150);

            // Pre-clear so a no-op Copy (empty section) cannot leave our own
            // pasted text on the clipboard and fake a successful verification.
            SetClipboardText("");
            dte.ExecuteCommand("Edit.SelectAll", "");
            System.Threading.Thread.Sleep(50);
            dte.ExecuteCommand("Edit.Copy", "");
            System.Threading.Thread.Sleep(100);

            string actual = GetClipboardText() ?? "";
            if (string.Equals(actual.TrimEnd('\r', '\n'), expected.TrimEnd('\r', '\n'), StringComparison.Ordinal))
            {
                Log($"{logPrefix}: paste verified ({actual.Length} chars)");
                try { dte.ExecuteCommand("Edit.SelectionCancel", ""); } catch { }
                return;
            }

            Log($"{logPrefix}: PASTE VERIFY MISMATCH (section has {actual.Length} chars, expected {expected.Length}) - retrying paste");
            if (SetClipboardText(expected))
            {
                System.Threading.Thread.Sleep(50);
                dte.ExecuteCommand("Edit.Paste", "");
                System.Threading.Thread.Sleep(200);
                Log($"{logPrefix}: retry paste executed");
            }
            try { dte.ExecuteCommand("Edit.SelectionCancel", ""); } catch { }
        }
        catch (Exception ex)
        {
            Log($"{logPrefix}: paste verification failed: {ex.Message}");
        }
    }

    // The PLC editor may process Edit.Paste asynchronously: restoring the
    // user's clipboard immediately after issuing the command can race the
    // editor's actual paste, which then inserts the restored (old) content -
    // or nothing - after the Delete already removed the selection. Give the
    // editor time to consume the formatted text before touching the clipboard.
    private static void RestoreClipboardAfterPaste(string? savedClipboard)
    {
        System.Threading.Thread.Sleep(300);
        if (savedClipboard != null)
        {
            try { SetClipboardText(savedClipboard); } catch { }
        }
    }

    // Win32 clipboard API — works from any apartment (MTA or STA)
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint uFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private static bool SetClipboardText(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                if (!EmptyClipboard()) return false;

                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (IntPtr)bytes.Length);
                if (hMem == IntPtr.Zero) return false;

                IntPtr ptr = GlobalLock(hMem);
                if (ptr == IntPtr.Zero)
                {
                    GlobalFree(hMem);
                    return false;
                }
                try
                {
                    Marshal.Copy(bytes, 0, ptr, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(hMem);
                }

                // If SetClipboardData fails the system did NOT take ownership -
                // free the memory and report failure so the caller never pastes
                // stale clipboard content over the document.
                if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
                {
                    GlobalFree(hMem);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return false;
        }
    }

    private static string? GetClipboardText()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
                IntPtr hMem = GetClipboardData(CF_UNICODETEXT);
                if (hMem == IntPtr.Zero) return null;
                IntPtr ptr = GlobalLock(hMem);
                if (ptr == IntPtr.Zero) return null;
                try
                {
                    return Marshal.PtrToStringUni(ptr);
                }
                finally
                {
                    GlobalUnlock(hMem);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return null;
        }
    }

    private static void Log(string message)
    {
        STFormatter.Core.Configuration.HostLog.Append("LiveEditor", message);
    }
}