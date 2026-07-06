using System;
using System.Runtime.InteropServices;
using System.Text;
using STFormatter.Core.Configuration;
using STFormatter.Core.Formatting;

namespace STBud.Git.Editor
{
    /// <summary>Outcome of a Git "restore to editor" attempt.</summary>
    public enum RestoreOutcome
    {
        AppliedLive,        // pasted into the live editor (single undo, no reload)
        AppliedDisk,        // wrote the section's CDATA on disk (TwinCAT will prompt reload)
        NotFoundInEditor,   // block not present in the active editor → committed on clipboard
        Ambiguous,          // block occurs >1 time → committed on clipboard
        WrongTabClipboard,  // wrong tab + couldn't disk-write → committed on clipboard
        NoEditor,           // no active document
        ReadFailed,         // couldn't read the editor
    }

    /// <summary>
    /// Git restore-to-editor and disk-write fallback, isolated from the STFormatter
    /// formatter/live-edit code (no dependency on STFormatter.Host). When the matching
    /// editor tab is active it reuses the proven clipboard live-edit (SelectAll → Delete →
    /// Paste of a section we computed); when the wrong tab is active (and TcXaeShell can't be
    /// switched) it replaces the section's CDATA on the .TcPOU/.TcGVL on disk. Self-contained:
    /// its own Win32 clipboard + DTE foreground helpers.
    /// </summary>
    public static class EditorRestore
    {
        /// <summary>
        /// Find <paramref name="working"/> (the current text of the changed block) in the active
        /// editor section and replace it with <paramref name="committed"/>. <paramref name="sectionTag"/>
        /// ("decl"/"impl"/null) gates the active-tab check; on a mismatch this falls back to a
        /// disk write of the correct section.
        /// </summary>
        public static RestoreOutcome Apply(EnvDTE.DTE dte, string committed, string working, string? sectionTag)
        {
            try
            {
                if (dte.ActiveDocument == null) { Log("Apply: no active document"); return RestoreOutcome.NoEditor; }

                // Section guard. Skip for GVL/DUT (declaration only — no wrong tab to hit).
                if (!string.IsNullOrEmpty(sectionTag))
                {
                    bool skipGuard = false;
                    try
                    {
                        string? ext = dte.ActiveDocument?.FullName == null ? null
                            : System.IO.Path.GetExtension(dte.ActiveDocument!.FullName);
                        if (string.Equals(ext, ".TcGVL", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ext, ".TcDUT", StringComparison.OrdinalIgnoreCase))
                            skipGuard = true;
                    }
                    catch (Exception ex) { LogSwallow("Apply: extension check", ex); }

                    if (!skipGuard)
                    {
                        try { dte.ActiveDocument.Activate(); } catch (Exception ex) { LogSwallow("Apply: Activate", ex); }
                        EnsureEditorForeground(dte);

                        string? activeSection = null;
                        for (int attempt = 0; attempt < 2 && activeSection == null; attempt++)
                        {
                            if (attempt > 0) System.Threading.Thread.Sleep(120);
                            activeSection = DetectActiveSection(dte);
                        }

                        if (activeSection != null && activeSection != sectionTag)
                        {
                            string wantLabel = sectionTag == "decl" ? "Declaration" : "Implementation";
                            Log($"Apply: section mismatch — wants {wantLabel}; trying disk-write fallback");
                            if (TryDiskWriteRestore(dte, committed, working, sectionTag))
                                return RestoreOutcome.AppliedDisk;
                            SetClipboardText(committed);
                            return RestoreOutcome.WrongTabClipboard;
                        }
                        // activeSection == null → detection failed; fall through and paste.
                    }
                }

                bool undoOpened = false;
                try
                {
                    try
                    {
                        if (!dte.UndoContext.IsOpen)
                        {
                            dte.UndoContext.Open("Restore lines from git");
                            undoOpened = true;
                        }
                    }
                    catch (Exception ex) { Log($"Apply: UndoContext.Open failed: {ex.Message}"); }

                    dte.ActiveDocument.Activate();

                    // Read the whole active section, replace the matched block IN MEMORY, then
                    // write the whole section back via the proven SelectAll+Delete+Paste path
                    // (the CODESYS PLC editor exposes no usable TextSelection).
                    string? editorText = ReadActiveSectionText(dte);
                    if (string.IsNullOrEmpty(editorText)) { Log("Apply: could not read editor text"); SetClipboardText(committed); return RestoreOutcome.ReadFailed; }

                    int code = TryReplaceBlock(editorText!, working, committed, out string newText);
                    if (code == -1) { Log("Apply: block not found"); SetClipboardText(committed); return RestoreOutcome.NotFoundInEditor; }
                    if (code == -2) { Log("Apply: block ambiguous"); SetClipboardText(committed); return RestoreOutcome.Ambiguous; }
                    if (string.Equals(newText, editorText, StringComparison.Ordinal)) { Log("Apply: already matches"); return RestoreOutcome.AppliedLive; }

                    string? savedClipboard = null;
                    try { savedClipboard = GetClipboardText(); } catch (Exception ex) { LogSwallow("clipboard preserve", ex); }

                    if (!SetClipboardText(newText))
                    {
                        Log("Apply: failed to set clipboard");
                        if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch (Exception ex) { LogSwallow("clipboard restore", ex); } }
                        return RestoreOutcome.ReadFailed;
                    }

                    dte.ExecuteCommand("Edit.SelectAll", "");
                    System.Threading.Thread.Sleep(50);
                    dte.ExecuteCommand("Edit.Delete", "");
                    System.Threading.Thread.Sleep(30);
                    dte.ExecuteCommand("Edit.Paste", "");
                    Log($"Apply: rewrote section ({newText.Length} chars)");

                    RestoreClipboardAfterPaste(savedClipboard);
                    return RestoreOutcome.AppliedLive;
                }
                finally
                {
                    if (undoOpened) { try { dte.UndoContext.Close(); } catch (Exception ex) { LogSwallow("UndoContext.Close", ex); } }
                }
            }
            catch (Exception ex)
            {
                Log($"Apply: FAILED: {ex.GetType().Name} - {ex.Message}");
                return RestoreOutcome.ReadFailed;
            }
        }

        /// <summary>Read the active editor section's text via SelectAll+Copy+clipboard.</summary>
        public static string? ReadActiveSectionText(EnvDTE.DTE dte)
        {
            try
            {
                if (dte.ActiveDocument == null) return null;

                string? savedClipboard = null;
                try { savedClipboard = GetClipboardText(); } catch (Exception ex) { LogSwallow("clipboard preserve", ex); }
                try
                {
                    SetClipboardText("");
                    dte.ExecuteCommand("Edit.SelectAll", "");
                    System.Threading.Thread.Sleep(50);
                    dte.ExecuteCommand("Edit.Copy", "");
                    System.Threading.Thread.Sleep(100);
                    string? text = GetClipboardText();
                    try { dte.ExecuteCommand("Edit.SelectionCancel", ""); } catch (Exception ex) { LogSwallow("SelectionCancel", ex); }
                    return text;
                }
                finally
                {
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch (Exception ex) { LogSwallow("clipboard restore", ex); } }
                }
            }
            catch (Exception ex)
            {
                Log($"ReadActiveSectionText: failed: {ex.Message}");
                return null;
            }
        }

        // ----- staged-accept disk write (the stable "Save" path) + one-level undo -----

        /// <summary>One staged accept to write: replace <paramref name="Working"/> (the current
        /// text of the block) with <paramref name="Committed"/> (HEAD's text) inside the section
        /// ("decl"/"impl"/null). Empty Committed deletes the located block.</summary>
        public readonly struct DiskBlock
        {
            public readonly string Committed;
            public readonly string Working;
            public readonly string? Section;
            public DiskBlock(string committed, string working, string? section)
            { Committed = committed; Working = working; Section = section; }
        }

        public readonly struct DiskApplyResult
        {
            public readonly int Applied;
            public readonly int Failed;
            public readonly byte[]? UndoSnapshot; // pre-write file bytes (null when nothing written)
            public DiskApplyResult(int applied, int failed, byte[]? undo)
            { Applied = applied; Failed = failed; UndoSnapshot = undo; }
        }

        /// <summary>
        /// Apply every staged accept block straight to the file on disk (the diff's working side
        /// was read from this same file, so each block is located deterministically). All blocks
        /// are applied to one in-memory copy and the file is written once. Returns how many landed
        /// and the pre-write bytes so the caller can offer a single-level undo.
        /// </summary>
        public static DiskApplyResult ApplyBlocksToDisk(string path, System.Collections.Generic.IReadOnlyList<DiskBlock> blocks)
        {
            if (string.IsNullOrEmpty(path) || blocks == null || blocks.Count == 0)
                return new DiskApplyResult(0, 0, null);

            byte[] original; string current; Encoding enc; byte[] preamble;
            try
            {
                original = System.IO.File.ReadAllBytes(path);
                (current, enc, preamble) = DecodeFile(original);
            }
            catch (Exception ex) { Log($"ApplyBlocksToDisk: read failed: {ex.Message}"); return new DiskApplyResult(0, blocks.Count, null); }

            bool isXml = IsTwinCatXmlFile(path);
            int applied = 0, failed = 0;
            foreach (var b in blocks)
            {
                bool ok;
                if (isXml)
                {
                    bool declaration = string.Equals(b.Section, "decl", StringComparison.OrdinalIgnoreCase);
                    var outcome = TwinCatXmlFormatter.ReplaceStBlockInSection(current, declaration, b.Working, b.Committed, out string updatedXml);
                    ok = outcome == TwinCatXmlFormatter.StReplaceResult.Replaced;
                    if (ok) current = updatedXml;
                    else Log($"ApplyBlocksToDisk: block outcome={outcome} section={b.Section ?? "null"}");
                }
                else
                {
                    int code = TryReplaceBlock(current, b.Working, b.Committed, out string updatedRaw);
                    ok = code == 1;
                    if (ok) current = updatedRaw;
                    else Log($"ApplyBlocksToDisk: raw block code={code}");
                }
                if (ok) applied++; else failed++;
            }

            if (applied == 0) return new DiskApplyResult(0, failed, null);

            try
            {
                System.IO.File.WriteAllBytes(path, EncodeFile(current, enc, preamble));
                Log($"ApplyBlocksToDisk: wrote {applied} block(s) to {System.IO.Path.GetFileName(path)} ({failed} failed)");
                return new DiskApplyResult(applied, failed, original);
            }
            catch (Exception ex) { Log($"ApplyBlocksToDisk: write failed: {ex.Message}"); return new DiskApplyResult(0, failed, null); }
        }

        /// <summary>Write a previously captured snapshot back to disk (undo the last save).</summary>
        public static bool RestoreBytes(string path, byte[] snapshot)
        {
            try
            {
                System.IO.File.WriteAllBytes(path, snapshot);
                Log($"RestoreBytes: reverted {System.IO.Path.GetFileName(path)} ({snapshot.Length} bytes)");
                return true;
            }
            catch (Exception ex) { Log($"RestoreBytes: failed: {ex.Message}"); return false; }
        }

        // ----- disk-write fallback -----

        private static bool TryDiskWriteRestore(EnvDTE.DTE dte, string committed, string working, string? sectionTag)
        {
            string? path = null;
            try { path = dte.ActiveDocument?.FullName; } catch (Exception ex) { LogSwallow("ActiveDocument.FullName", ex); }
            if (string.IsNullOrEmpty(path) || !IsTwinCatXmlFile(path!))
            {
                Log("TryDiskWriteRestore: no TwinCAT XML file path — cannot disk-write");
                return false;
            }

            string xml; Encoding enc; byte[] preamble;
            try
            {
                var bytes = System.IO.File.ReadAllBytes(path!);
                (xml, enc, preamble) = DecodeFile(bytes);
            }
            catch (Exception ex) { Log($"TryDiskWriteRestore: read failed: {ex.Message}"); return false; }

            bool declaration = string.Equals(sectionTag, "decl", StringComparison.OrdinalIgnoreCase);
            var outcome = TwinCatXmlFormatter.ReplaceStBlockInSection(xml, declaration, working, committed, out string newXml);
            if (outcome != TwinCatXmlFormatter.StReplaceResult.Replaced)
            {
                Log($"TryDiskWriteRestore: section replace outcome={outcome} — not writing");
                return false;
            }

            try
            {
                System.IO.File.WriteAllBytes(path!, EncodeFile(newXml, enc, preamble));
                Log($"TryDiskWriteRestore: wrote {(declaration ? "Declaration" : "Implementation")} section to disk ({newXml.Length} chars)");
                return true;
            }
            catch (Exception ex) { Log($"TryDiskWriteRestore: write failed: {ex.Message}"); return false; }
        }

        private static bool IsTwinCatXmlFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path);
            return ext.Equals(".TcPOU", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".TcDUT", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".TcGVL", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".TcIO", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".TcTO", StringComparison.OrdinalIgnoreCase);
        }

        private static (string text, Encoding enc, byte[] preamble) DecodeFile(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3),
                    new UTF8Encoding(false), new byte[] { 0xEF, 0xBB, 0xBF });
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
                    Encoding.Unicode, new byte[] { 0xFF, 0xFE });
            return (new UTF8Encoding(false).GetString(bytes), new UTF8Encoding(false), Array.Empty<byte>());
        }

        private static byte[] EncodeFile(string text, Encoding enc, byte[] preamble)
        {
            byte[] body = enc.GetBytes(text);
            if (preamble.Length == 0) return body;
            var outBytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, outBytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, outBytes, preamble.Length, body.Length);
            return outBytes;
        }

        // ----- block match (line-based, trailing-WS-insensitive) -----

        private static int TryReplaceBlock(string editorText, string working, string committed, out string newText)
        {
            newText = editorText;
            string nl = editorText.Contains("\r\n") ? "\r\n" : "\n";

            var ed = editorText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var edTrim = new string[ed.Length];
            for (int i = 0; i < ed.Length; i++) edTrim[i] = ed[i].TrimEnd();

            var wk = SplitLinesTrimEnd(working);
            if (wk.Length == 0 || ed.Length < wk.Length) return -1;

            int found = -1, count = 0;
            for (int i = 0; i + wk.Length <= ed.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < wk.Length; j++)
                    if (!string.Equals(edTrim[i + j], wk[j], StringComparison.Ordinal)) { match = false; break; }
                if (match) { count++; if (found < 0) found = i; if (count > 1) return -2; }
            }
            if (count != 1) return -1;

            // Empty committed = delete the located block entirely (no blank-line residue).
            var committedLines = committed.Length == 0
                ? Array.Empty<string>()
                : committed.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var result = new System.Collections.Generic.List<string>(ed.Length - wk.Length + committedLines.Length);
            for (int i = 0; i < found; i++) result.Add(ed[i]);
            result.AddRange(committedLines);
            for (int i = found + wk.Length; i < ed.Length; i++) result.Add(ed[i]);

            newText = string.Join(nl, result);
            return 1;
        }

        private static string[] SplitLinesTrimEnd(string s)
        {
            var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();
            return lines;
        }

        // ----- active-section detection -----

        private static string? DetectActiveSection(EnvDTE.DTE dte)
        {
            try
            {
                string? savedClipboard = null;
                try { savedClipboard = GetClipboardText(); } catch (Exception ex) { LogSwallow("clipboard preserve", ex); }
                try
                {
                    SetClipboardText("");
                    dte.ExecuteCommand("Edit.SelectAll", "");
                    System.Threading.Thread.Sleep(50);
                    dte.ExecuteCommand("Edit.Copy", "");
                    System.Threading.Thread.Sleep(100);
                    string? text = GetClipboardText();
                    try { dte.ExecuteCommand("Edit.SelectionCancel", ""); } catch (Exception ex) { LogSwallow("SelectionCancel", ex); }

                    if (string.IsNullOrWhiteSpace(text)) { Log("DetectActiveSection: empty — cannot classify"); return null; }
                    bool isDecl = TwinCatXmlFormatter.LooksLikeDeclaration(text);
                    Log($"DetectActiveSection: classified as {(isDecl ? "decl" : "impl")} ({text!.Length} chars)");
                    return isDecl ? "decl" : "impl";
                }
                finally
                {
                    if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch (Exception ex) { LogSwallow("clipboard restore", ex); } }
                }
            }
            catch (Exception ex) { Log($"DetectActiveSection: failed: {ex.Message}"); return null; }
        }

        // ----- editor foreground + Win32 clipboard (self-contained) -----

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Bring the TcXaeShell window to the foreground so VS re-checks the file on disk and
        /// shows its native "file changed outside the editor — reload?" prompt. After a Git disk
        /// write the editor stays in the background (our diff is a modal dialog), so the prompt
        /// never fires until VS is activated — this is the only lever (the IVsFileChangeEx / RDT
        /// reload APIs don't work from an external process; see AGENTS.md). Git-only.
        /// </summary>
        public static void BringToForeground(EnvDTE.DTE dte)
        {
            try
            {
                try { dte.ActiveDocument?.Activate(); } catch (Exception ex) { LogSwallow("Activate", ex); }
                EnsureEditorForeground(dte);
            }
            catch (Exception ex) { Log($"BringToForeground: failed: {ex.Message}"); }
        }

        private static bool EnsureEditorForeground(EnvDTE.DTE dte)
        {
            try
            {
                var hwnd = (IntPtr)(long)dte.MainWindow.HWnd;
                if (hwnd == IntPtr.Zero) return false;
                if (GetForegroundWindow() == hwnd) return true;
                SetForegroundWindow(hwnd);
                System.Threading.Thread.Sleep(80);
                return GetForegroundWindow() == hwnd;
            }
            catch (Exception ex) { Log($"EnsureEditorForeground: failed: {ex.Message}"); return false; }
        }

        private static void RestoreClipboardAfterPaste(string? savedClipboard)
        {
            System.Threading.Thread.Sleep(300);
            if (savedClipboard != null) { try { SetClipboardText(savedClipboard); } catch (Exception ex) { LogSwallow("clipboard restore", ex); } }
        }

        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint uFormat);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        // OpenClipboard fails transiently when another process is mid-clipboard-op
        // (a frequent cause of "failed to set clipboard"). Retry briefly before giving up.
        private static bool OpenClipboardRetry()
        {
            for (int i = 0; i < 12; i++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                System.Threading.Thread.Sleep(25);
            }
            return false;
        }

        private static bool SetClipboardText(string text)
        {
            try
            {
                if (!OpenClipboardRetry()) return false;
                try
                {
                    if (!EmptyClipboard()) return false;
                    byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                    IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (IntPtr)bytes.Length);
                    if (hMem == IntPtr.Zero) return false;
                    IntPtr ptr = GlobalLock(hMem);
                    if (ptr == IntPtr.Zero) { GlobalFree(hMem); return false; }
                    try { Marshal.Copy(bytes, 0, ptr, bytes.Length); }
                    finally { GlobalUnlock(hMem); }
                    if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero) { GlobalFree(hMem); return false; }
                    return true;
                }
                finally { CloseClipboard(); }
            }
            catch { return false; }
        }

        private static string? GetClipboardText()
        {
            try
            {
                if (!OpenClipboardRetry()) return null;
                try
                {
                    if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
                    IntPtr hMem = GetClipboardData(CF_UNICODETEXT);
                    if (hMem == IntPtr.Zero) return null;
                    IntPtr ptr = GlobalLock(hMem);
                    if (ptr == IntPtr.Zero) return null;
                    try { return Marshal.PtrToStringUni(ptr); }
                    finally { GlobalUnlock(hMem); }
                }
                finally { CloseClipboard(); }
            }
            catch { return null; }
        }

        private static void Log(string message) => HostLog.Append("GitEditor", message);

        // Best-effort operations (clipboard preserve/restore, selection cancel, activation)
        // keep their fallback behavior, but the failure is logged instead of vanishing —
        // silent swallows made real problems ("saving is unstable") undiagnosable.
        private static void LogSwallow(string what, Exception ex) => Log($"{what} failed (ignored): {ex.Message}");
    }
}
