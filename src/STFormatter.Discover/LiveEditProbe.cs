using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;

namespace STFormatter.Discover;

internal static class LiveEditProbe
{
    public static void Probe(DTE dte, DualLogger log)
    {
        log.WriteSection("Live Edit Probes");

        ProbeDteExecuteCommand(dte, log);
        ProbeDteTextSelection(dte, log);
        ProbeDteUndoContext(dte, log);
        ProbeVsServices(dte, log);
        ProbeRdtDocData(dte, log);
    }

    private static void ProbeDteExecuteCommand(DTE dte, DualLogger log)
    {
        log.WriteSection("DTE.ExecuteCommand Availability");
        string[] commands = {
            "Edit.SelectAll",
            "Edit.Delete",
            "Edit.Paste",
            "Edit.Cut",
            "Edit.Copy",
            "Edit.ReplaceInFiles",
            "Edit.FindReplace",
            "Edit.FormatDocument",
            "Edit.FormatSelection",
        };

        foreach (var cmd in commands)
        {
            try
            {
                var command = dte.Commands.Item(cmd, -1);
                if (command != null)
                {
                    log.WriteLine($"  {cmd}: FOUND (Name='{command.Name}', LocalizedName='{command.LocalizedName}')");
                }
                else
                {
                    log.WriteLine($"  {cmd}: NOT FOUND (Item returned null)");
                }
            }
            catch (Exception ex)
            {
                log.WriteLine($"  {cmd}: NOT AVAILABLE ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }

    private static void ProbeDteTextSelection(DTE dte, DualLogger log)
    {
        log.WriteSection("DTE TextSelection / TextDocument Probe");

        try
        {
            var doc = dte.ActiveDocument;
            if (doc == null)
            {
                log.WriteLine("  No active document - cannot probe TextSelection");
                return;
            }

            log.WriteLine($"  ActiveDocument: '{doc.FullName}'");

            var selection = doc.Selection as TextSelection;
            log.WriteLine($"  doc.Selection as TextSelection: {(selection != null ? "AVAILABLE" : "NULL")}");

            if (selection != null)
            {
                try
                {
                    log.WriteLine($"  Selection.Text length: {selection.Text?.Length ?? -1}");
                    log.WriteLine($"  Selection.TopLine: {selection.TopLine}");
                    log.WriteLine($"  Selection.BottomLine: {selection.BottomLine}");
                    log.WriteLine($"  Selection.IsActiveEndGreater: {selection.IsActiveEndGreater}");
                }
                catch (Exception ex)
                {
                    log.WriteLine($"  Selection property access failed: {ex.GetType().Name} - {ex.Message}");
                }
            }

            try
            {
                var textDoc = doc.Object("TextDocument") as EnvDTE.TextDocument;
                log.WriteLine($"  doc.Object(\"TextDocument\") as TextDocument: {(textDoc != null ? "AVAILABLE" : "NULL")}");

                if (textDoc != null)
                {
                    var ep = textDoc.CreateEditPoint();
                    log.WriteLine($"  CreateEditPoint: AVAILABLE");
                    log.WriteLine($"  EditPoint.GetText(textDoc.EndPoint): length={ep.GetText(textDoc.EndPoint).Length}");
                }
            }
            catch (Exception ex)
            {
                log.WriteLine($"  doc.Object(\"TextDocument\") failed: {ex.GetType().Name} - {ex.Message}");
            }

            try
            {
                var obj = doc.Object("");
                log.WriteLine($"  doc.Object(\"\"): {(obj != null ? $"type={obj.GetType().FullName}" : "NULL")}");
            }
            catch (Exception ex)
            {
                log.WriteLine($"  doc.Object(\"\") failed: {ex.GetType().Name} - {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            log.WriteError("TextSelection probe failed", ex);
        }
    }

    private static void ProbeDteUndoContext(DTE dte, DualLogger log)
    {
        log.WriteSection("DTE.UndoContext Probe");

        try
        {
            var undoCtx = dte.UndoContext;
            log.WriteLine($"  UndoContext available: YES");
            log.WriteLine($"  UndoContext.IsOpen: {undoCtx.IsOpen}");

            try
            {
                undoCtx.Open("STFormatter.Discover.Probe");
                log.WriteLine($"  UndoContext.Open: SUCCESS");
                undoCtx.Close();
                log.WriteLine($"  UndoContext.Close: SUCCESS");
            }
            catch (Exception ex)
            {
                log.WriteLine($"  UndoContext Open/Close test FAILED: {ex.GetType().Name} - {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            log.WriteError("UndoContext probe failed", ex);
        }
    }

    private static void ProbeVsServices(DTE dte, DualLogger log)
    {
        log.WriteSection("VS Shell Services via IServiceProvider");

        IServiceProvider? sp = null;
        try
        {
            sp = dte as IServiceProvider;
        }
        catch (Exception ex)
        {
            log.WriteError("Failed to get IServiceProvider from DTE", ex);
            return;
        }

        if (sp == null)
        {
            log.WriteError("IServiceProvider is NULL (DTE does not implement IServiceProvider)", null);
            return;
        }

        log.WriteLine("  IServiceProvider: AVAILABLE");

        ProbeService<IVsFileChangeEx>(sp, typeof(SVsFileChangeEx), "IVsFileChangeEx", log);
        ProbeService<IVsRunningDocumentTable>(sp, typeof(SVsRunningDocumentTable), "IVsRunningDocumentTable", log);
    }

    private static void ProbeService<T>(IServiceProvider sp, Type serviceType, string name, DualLogger log) where T : class
    {
        try
        {
            var svc = sp.GetService(serviceType) as T;
            if (svc != null)
            {
                log.WriteLine($"  {name}: AVAILABLE (type={svc.GetType().FullName})");

                if (svc is IVsFileChangeEx fce)
                {
                    TestFileChangeEx(fce, log);
                }
                else if (svc is IVsRunningDocumentTable rdt)
                {
                    TestRdt(rdt, log);
                }

                Marshal.ReleaseComObject(svc);
            }
            else
            {
                log.WriteLine($"  {name}: GetService returned NULL");
            }
        }
        catch (Exception ex)
        {
            log.WriteLine($"  {name}: GetService FAILED ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static void TestFileChangeEx(IVsFileChangeEx fce, DualLogger log)
    {
        log.WriteLine("    --- IVsFileChangeEx Method Tests ---");

        try
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "STFormatter_IgnoreFile_Test.tmp");
            File.WriteAllText(tempFile, "test");
            int hr = fce.IgnoreFile(0, tempFile, 1);
            log.WriteLine($"    IgnoreFile(0, \"{Path.GetFileName(tempFile)}\", 1) hr=0x{hr:X8} {(hr == 0 ? "SUCCESS" : "FAILED")}");

            if (hr == 0)
            {
                hr = fce.SyncFile(tempFile);
                log.WriteLine($"    SyncFile(\"{Path.GetFileName(tempFile)}\") hr=0x{hr:X8} {(hr == 0 ? "SUCCESS" : "FAILED")}");

                hr = fce.IgnoreFile(0, tempFile, 0);
                log.WriteLine($"    IgnoreFile(0, \"{Path.GetFileName(tempFile)}\", 0) hr=0x{hr:X8} {(hr == 0 ? "SUCCESS" : "FAILED")}");
            }

            File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            log.WriteLine($"    IVsFileChangeEx test failed: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void TestRdt(IVsRunningDocumentTable rdt, DualLogger log)
    {
        log.WriteLine("    --- IVsRunningDocumentTable - PLC Documents ---");

        try
        {
            int hr = rdt.GetRunningDocumentsEnum(out IEnumRunningDocuments enumDocs);
            log.WriteLine($"    GetRunningDocumentsEnum hr=0x{hr:X8}");

            if (hr == 0 && enumDocs != null)
            {
                uint[] cookies = new uint[1];
                uint fetched = 0;
                int plcCount = 0;

                while (enumDocs.Next(1, cookies, out fetched) == 0 && fetched > 0)
                {
                    try
                    {
                        hr = rdt.GetDocumentInfo(cookies[0],
                            out uint flags, out uint readLocks, out uint editLocks,
                            out string moniker, out IVsHierarchy hierarchy,
                            out uint itemId, out IntPtr docDataPtr);

                        object? docData = null;
                        if (docDataPtr != IntPtr.Zero)
                        {
                            try { docData = Marshal.GetObjectForIUnknown(docDataPtr); }
                            catch { }
                        }

                        if (hr == 0 && moniker != null)
                        {
                            bool isPlc = moniker.IndexOf(".TcPOU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         moniker.IndexOf(".TcDUT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         moniker.IndexOf(".TcGVL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         moniker.IndexOf(".TcIO", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isPlc)
                            {
                                plcCount++;
                                string docDataType = docData?.GetType().FullName ?? "null";
                                string hierType = hierarchy?.GetType().FullName ?? "null";
                                log.WriteLine($"    [{plcCount}] Cookie={cookies[0]}: '{Path.GetFileName(moniker)}'");
                                log.WriteLine($"        DocData: {docDataType}");
                                log.WriteLine($"        Hierarchy: {hierType}");
                                log.WriteLine($"        ItemId: 0x{itemId:X8}, Flags=0x{flags:X8}, ReadLocks={readLocks}, EditLocks={editLocks}");

                                if (docData != null)
                                    ProbeDocDataInterfaces(docData, cookies[0], log);

                                if (docData != null) Marshal.ReleaseComObject(docData);
                                if (hierarchy != null) Marshal.ReleaseComObject(hierarchy);
                            }
                        }

                        if (docDataPtr != IntPtr.Zero)
                            Marshal.Release(docDataPtr);
                    }
                    catch (Exception ex)
                    {
                        log.WriteLine($"    Cookie {cookies[0]}: <error: {ex.Message}>");
                    }
                }

                if (plcCount == 0)
                {
                    log.WriteLine("    No PLC documents found in RDT. Open a .TcPOU file in TcXaeShell first.");
                }
                else
                {
                    log.WriteLine($"    Total PLC documents in RDT: {plcCount}");
                }

                Marshal.ReleaseComObject(enumDocs);
            }
        }
        catch (Exception ex)
        {
            log.WriteError("RDT probe failed", ex);
        }
    }

    private static void ProbeDocDataInterfaces(object docData, uint cookie, DualLogger log)
    {
        log.WriteLine("        --- DocData Interface Probe ---");

        if (docData is IVsPersistDocData2 persistDocData2)
        {
            log.WriteLine("        IVsPersistDocData2: AVAILABLE");

            try
            {
                int hr = persistDocData2.IsDocDataReloadable(out int reloadable);
                log.WriteLine($"          IsDocDataReloadable: hr=0x{hr:X8}, reloadable={reloadable}");
            }
            catch (Exception ex)
            {
                log.WriteLine($"          IsDocDataReloadable FAILED: {ex.GetType().Name} - {ex.Message}");
            }

            try
            {
                int hr = persistDocData2.IsDocDataDirty(out int dirty);
                log.WriteLine($"          IsDocDataDirty: hr=0x{hr:X8}, dirty={dirty}");
            }
            catch (Exception ex)
            {
                log.WriteLine($"          IsDocDataDirty FAILED: {ex.GetType().Name} - {ex.Message}");
            }
        }
        else
        {
            log.WriteLine("        IVsPersistDocData2: NOT AVAILABLE");
        }

        if (docData is IVsPersistDocData persistDocData)
        {
            log.WriteLine("        IVsPersistDocData: AVAILABLE");
        }
        else
        {
            log.WriteLine("        IVsPersistDocData: NOT AVAILABLE");
        }

        try
        {
            var type = docData.GetType();
            var ifaces = type.GetInterfaces();
            log.WriteLine($"        DocData runtime type: {type.FullName}");
            log.WriteLine($"        DocData interfaces ({ifaces.Length}):");
            foreach (var iface in ifaces)
            {
                log.WriteLine($"          {iface.FullName}");
            }
        }
        catch (Exception ex)
        {
            log.WriteLine($"        Reflection on DocData failed: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void ProbeRdtDocData(DTE dte, DualLogger log)
    {
        log.WriteSection("ActiveDocument vs RDT Cross-Reference");

        try
        {
            var doc = dte.ActiveDocument;
            if (doc == null)
            {
                log.WriteLine("  No active document");
                return;
            }

            string filePath = doc.FullName;
            log.WriteLine($"  ActiveDocument.FullName: '{filePath}'");

            IServiceProvider? sp = dte as IServiceProvider;
            if (sp == null)
            {
                log.WriteLine("  Cannot cross-reference: IServiceProvider is null");
                return;
            }

            var rdt = sp.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt == null)
            {
                log.WriteLine("  Cannot cross-reference: IVsRunningDocumentTable is null");
                return;
            }

            int hr = rdt.FindAndLockDocument(
                (uint)_VSRDTFLAGS.RDT_NoLock,
                filePath,
                out IVsHierarchy hier,
                out uint itemId,
                out IntPtr docDataPtr,
                out uint cookie);

            log.WriteLine($"  FindAndLockDocument hr=0x{hr:X8}");

            if (hr == 0 && docDataPtr != IntPtr.Zero)
            {
                try
                {
                    var docDataObj = Marshal.GetObjectForIUnknown(docDataPtr);
                    string typeName = docDataObj?.GetType().FullName ?? "null";
                    log.WriteLine($"  DocData type: {typeName}");

                    bool hasIPLCData = false;
                    try
                    {
                        hasIPLCData = docDataObj.GetType().GetInterfaces().Any(i => i.Name == "IPLCData");
                        log.WriteLine($"  Has IPLCData: {hasIPLCData}");
                    }
                    catch { }

                    bool hasPersistDocData2 = docDataObj is IVsPersistDocData2;
                    log.WriteLine($"  Has IVsPersistDocData2: {hasPersistDocData2}");

                    if (hasPersistDocData2)
                    {
                        var pdd2 = (IVsPersistDocData2)docDataObj;
                        try
                        {
                            hr = pdd2.IsDocDataReloadable(out int reloadable);
                            log.WriteLine($"  IsDocDataReloadable: hr=0x{hr:X8}, reloadable={reloadable}");
                        }
                        catch (Exception ex)
                        {
                            log.WriteLine($"  IsDocDataReloadable FAILED: {ex.Message}");
                        }

                        try
                        {
                            hr = pdd2.IsDocDataDirty(out int dirty);
                            log.WriteLine($"  IsDocDataDirty: hr=0x{hr:X8}, dirty={dirty}");
                        }
                        catch (Exception ex)
                        {
                            log.WriteLine($"  IsDocDataDirty FAILED: {ex.Message}");
                        }
                    }

                    Marshal.ReleaseComObject(docDataObj);
                }
                finally
                {
                    Marshal.Release(docDataPtr);
                }
            }
            else
            {
                log.WriteLine($"  Document not found in RDT (not open in editor?)");
            }

            Marshal.ReleaseComObject(rdt);
        }
        catch (Exception ex)
        {
            log.WriteError("ActiveDocument vs RDT cross-reference failed", ex);
        }
    }
}