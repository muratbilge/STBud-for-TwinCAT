using System;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;

namespace STFormatter.Discover;

internal static class VsShellProbe
{
    public static void Probe(DTE dte, DualLogger log)
    {
        log.WriteSection("VS Shell Probes (via DTE IServiceProvider)");

        IServiceProvider? serviceProvider = null;
        try
        {
            serviceProvider = dte as IServiceProvider;
        }
        catch (Exception ex)
        {
            log.WriteError("Failed to get IServiceProvider from DTE", ex);
            return;
        }

        if (serviceProvider == null)
        {
            log.WriteError("IServiceProvider is null (DTE does not implement IServiceProvider)");
            return;
        }

        ProbeMonitorSelection(serviceProvider, log);
        ProbeUIShell(serviceProvider, log);
        ProbeRunningDocumentTable(serviceProvider, log);
    }

    private static void ProbeMonitorSelection(IServiceProvider serviceProvider, DualLogger log)
    {
        log.WriteSection("IVsMonitorSelection");
        try
        {
            var monitorSelection = serviceProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (monitorSelection == null)
            {
                log.WriteLine("  Failed to get IVsMonitorSelection (not available via COM interop in this mode)");
                log.WriteLine("  This is expected when attaching externally via DTE");
                return;
            }

            int hr = monitorSelection.GetCurrentSelection(
                out IVsHierarchy hierarchy,
                out uint itemId,
                out IVsMultiItemSelect multiItemSelect,
                out ISelectionContainer selectionContainer);

            log.WriteLine($"  GetCurrentSelection hr=0x{hr:X8}");

            if (hr == 0)
            {
                if (hierarchy != null)
                {
                    try
                    {
                        string typeName = hierarchy.GetType().FullName;
                        log.WriteLine($"  Hierarchy type: {typeName}");
                        log.WriteLine($"  ItemId: 0x{itemId:X8}");

                        try
                        {
                            hr = hierarchy.GetProperty(itemId, (int)VSHPROPID.Name, out object nameObj);
                            if (hr == 0 && nameObj != null)
                                log.WriteLine($"  Hierarchy name: '{nameObj}'");
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        log.WriteLine($"  Hierarchy error: {ex.Message}");
                    }
                }
                else
                {
                    log.WriteLine("  Hierarchy = null");
                }

                log.WriteLine($"  MultiItemSelect = {(multiItemSelect != null ? "present" : "null")}");
                log.WriteLine($"  SelectionContainer = {(selectionContainer != null ? "present" : "null")}");
            }
        }
        catch (Exception ex)
        {
            log.WriteError("IVsMonitorSelection probe failed", ex);
        }
    }

    private static void ProbeUIShell(IServiceProvider serviceProvider, DualLogger log)
    {
        log.WriteSection("IVsUIShell");
        try
        {
            var uiShell = serviceProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell == null)
            {
                log.WriteLine("  Failed to get IVsUIShell (not available via COM interop in this mode)");
                log.WriteLine("  This is expected when attaching externally via DTE");
                return;
            }

            log.WriteLine("  IVsUIShell obtained successfully");

            try
            {
                int hr = uiShell.GetDocumentWindowEnum(out IEnumWindowFrames enumFrames);
                log.WriteLine($"  GetDocumentWindowEnum hr=0x{hr:X8}");

                if (hr == 0 && enumFrames != null)
                {
                    var frames = new IVsWindowFrame[1];
                    uint fetched = 0;
                    int docIdx = 0;

                    while (enumFrames.Next(1, frames, out fetched) == 0 && fetched > 0)
                    {
                        var frame = frames[0];
                        if (frame == null) continue;

                        try
                        {
                            string caption = "";
                            string viewTypeName = "null";
                            string dataTypeName = "null";

                            try
                            {
                                frame.GetProperty((int)VSFPROPID.Caption, out object captionObj);
                                caption = captionObj?.ToString() ?? "";
                            } catch { }

                            try
                            {
                                frame.GetProperty((int)VSFPROPID.DocView, out object docView);
                                if (docView != null)
                                {
                                    viewTypeName = docView.GetType().FullName;
                                    Marshal.ReleaseComObject(docView);
                                }
                            } catch { }

                            try
                            {
                                frame.GetProperty((int)VSFPROPID.DocData, out object docData);
                                if (docData != null)
                                {
                                    dataTypeName = docData.GetType().FullName;
                                    Marshal.ReleaseComObject(docData);
                                }
                            } catch { }

                            log.WriteLine($"  [{docIdx}] Caption='{caption}'");
                            log.WriteLine($"      DocView type='{viewTypeName}'");
                            log.WriteLine($"      DocData type='{dataTypeName}'");
                        }
                        catch (Exception ex)
                        {
                            log.WriteLine($"  [{docIdx}] <error: {ex.Message}>");
                        }

                        Marshal.ReleaseComObject(frame);
                        docIdx++;
                    }

                    log.WriteLine($"  Total document windows: {docIdx}");
                    Marshal.ReleaseComObject(enumFrames);
                }
            }
            catch (Exception ex)
            {
                log.WriteError("GetDocumentWindowEnum failed", ex);
            }
        }
        catch (Exception ex)
        {
            log.WriteError("IVsUIShell probe failed", ex);
        }
    }

    private static void ProbeRunningDocumentTable(IServiceProvider serviceProvider, DualLogger log)
    {
        log.WriteSection("IVsRunningDocumentTable");
        try
        {
            var rdt = serviceProvider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt == null)
            {
                log.WriteLine("  Failed to get IVsRunningDocumentTable (not available via COM interop in this mode)");
                return;
            }

            int hr = rdt.GetRunningDocumentsEnum(out IEnumRunningDocuments enumDocs);
            log.WriteLine($"  GetRunningDocumentsEnum hr=0x{hr:X8}");

            if (hr == 0 && enumDocs != null)
            {
                uint[] cookies = new uint[1];
                uint fetched = 0;
                int docIdx = 0;

                while (enumDocs.Next(1, cookies, out fetched) == 0 && fetched > 0)
                {
                    try
                    {
                        hr = rdt.GetDocumentInfo(cookies[0],
                            out uint flags,
                            out uint readLocks,
                            out uint editLocks,
                            out string moniker,
                            out IVsHierarchy hierarchy,
                            out uint itemId,
                            out IntPtr docDataPtr);

                        object? docData = null;
                        if (docDataPtr != IntPtr.Zero)
                        {
                            try { docData = Marshal.GetObjectForIUnknown(docDataPtr); }
                            catch { }
                        }

                        if (hr == 0)
                        {
                            string dataTypeName = docData?.GetType().FullName ?? "null";
                            string hierTypeName = hierarchy?.GetType().FullName ?? "null";

                            bool isPlcFile = moniker != null && (
                                moniker.IndexOf(".TcPOU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                moniker.IndexOf(".TcDUT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                moniker.IndexOf(".TcGVL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                moniker.IndexOf(".TcIO", StringComparison.OrdinalIgnoreCase) >= 0);

                            if (isPlcFile)
                            {
                                log.WriteLine($"  [{docIdx}] *** PLC DOCUMENT ***");
                                log.WriteLine($"      Moniker: '{moniker}'");
                                log.WriteLine($"      DocData type: '{dataTypeName}'");
                                log.WriteLine($"      Hierarchy type: '{hierTypeName}'");
                                log.WriteLine($"      ItemId: 0x{itemId:X8}, Flags=0x{flags:X8}, ReadLocks={readLocks}, EditLocks={editLocks}");
                            }
                            else
                            {
                                string shortMoniker = moniker ?? "";
                                if (shortMoniker.Length > 80)
                                    shortMoniker = "..." + shortMoniker.Substring(shortMoniker.Length - 77);

                                log.WriteLine($"  [{docIdx}] '{shortMoniker}' (DocData='{dataTypeName}')");
                            }

                            if (hierarchy != null)
                                Marshal.ReleaseComObject(hierarchy);
                            if (docData != null)
                                Marshal.ReleaseComObject(docData);
                            if (docDataPtr != IntPtr.Zero)
                                Marshal.Release(docDataPtr);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.WriteLine($"  [{docIdx}] <error: {ex.Message}>");
                    }

                    docIdx++;
                }

                log.WriteLine($"  Total documents: {docIdx}");
                Marshal.ReleaseComObject(enumDocs);
            }
        }
        catch (Exception ex)
        {
            log.WriteError("IVsRunningDocumentTable probe failed", ex);
        }
    }
}