using System;
using System.Runtime.InteropServices;
using EnvDTE;

namespace STFormatter.Discover;

internal static class ActiveWindowProbe
{
    public static void Probe(DTE dte, DualLogger log)
    {
        log.WriteSection("Active Window");

        try
        {
            var win = dte.ActiveWindow;
            if (win == null)
            {
                log.WriteLine("  ActiveWindow = null (no active window)");
            }
            else
            {
                string caption = "";
                string kind = "";
                string objectKind = "";
                string type = "";
                try { caption = win.Caption ?? ""; } catch { }
                try { kind = win.Kind ?? ""; } catch { }
                try { objectKind = win.ObjectKind ?? ""; } catch { }
                try { type = win.Type.ToString(); } catch { }

                log.WriteLine($"  Caption:      '{caption}'");
                log.WriteLine($"  Kind:         '{kind}'");
                log.WriteLine($"  ObjectKind:   '{objectKind}'");
                log.WriteLine($"  Type:         '{type}'");
            }
        }
        catch (Exception ex)
        {
            log.WriteError("Failed to get ActiveWindow", ex);
        }

        try
        {
            var doc = dte.ActiveDocument;
            if (doc != null)
            {
                log.WriteLine("  --- Active Document ---");
                string docName = "";
                string docPath = "";
                string docLang = "";
                string docExt = "";
                try { docName = doc.Name ?? ""; } catch { }
                try { docPath = doc.FullName ?? ""; } catch { }
                try { docLang = doc.Language ?? ""; } catch { }
                try { docExt = System.IO.Path.GetExtension(docPath); } catch { }

                log.WriteLine($"  Name:         '{docName}'");
                log.WriteLine($"  FullName:     '{docPath}'");
                log.WriteLine($"  Extension:    '{docExt}'");
                log.WriteLine($"  Language:     '{docLang}'");

                Marshal.ReleaseComObject(doc);
            }
            else
            {
                log.WriteLine("  ActiveDocument = null");
            }
        }
        catch (Exception ex)
        {
            log.WriteError("Failed to get ActiveDocument", ex);
        }

        log.WriteSection("All Open Windows");
        try
        {
            var windows = dte.Windows;
            int count = windows.Count;
            log.WriteLine($"  Total windows: {count}");

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    var w = windows.Item(i);
                    if (w == null) continue;

                    string caption = "";
                    string kind = "";
                    string objectKind = "";
                    string type = "";
                    bool isVisible = false;

                    try { caption = w.Caption ?? ""; } catch { }
                    try { kind = w.Kind ?? ""; } catch { }
                    try { objectKind = w.ObjectKind ?? ""; } catch { }
                    try { type = w.Type.ToString(); } catch { }
                    try { isVisible = w.Visible; } catch { }

                    log.WriteLine($"  [{i:D3}] Caption='{caption}', Kind='{kind}', ObjectKind='{objectKind}', Visible={isVisible}");

                    Marshal.ReleaseComObject(w);
                }
                catch (Exception ex)
                {
                    log.WriteLine($"  [{i:D3}] <error: {ex.Message}>");
                }
            }
        }
        catch (Exception ex)
        {
            log.WriteError("Failed to enumerate Windows", ex);
        }
    }
}