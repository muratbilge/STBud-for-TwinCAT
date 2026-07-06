using System;

namespace STFormatter.UI
{
    /// <summary>
    /// A tiny in-process hook the Host wires up at startup so the Git tab (in the UI
    /// assembly) can reach the live TcXaeShell editor without the UI taking a COM/DTE
    /// dependency. The UI invokes these; the Host implements them.
    /// </summary>
    public static class GitEditorBridge
    {
        /// <summary>
        /// Apply a committed version of line(s) into the live editor by CONTENT, not by
        /// line number: the Host finds <paramref name="working"/> (the current text of the
        /// changed block) in the active editor section and replaces that occurrence with
        /// <paramref name="committed"/>. Because the search runs against the editor's own
        /// text, the coordinates are correct by construction. <paramref name="sectionTag"/>
        /// is "decl"/"impl" (or null) and lets the Host refuse a cross-tab apply. Returns
        /// true only when a unique match was found and replaced; on zero/multiple matches
        /// the Host copies <paramref name="committed"/> to the clipboard and returns false.
        /// Args: (committed, working, sectionTag, pid).
        /// </summary>
        public static Func<string, string, string?, int, bool>? RestoreToEditor;

        /// <summary>
        /// Read the active editor section's text via SelectAll+Copy+clipboard for the
        /// TcXaeShell instance identified by <paramref name="pid"/>. Returns null when
        /// the editor content cannot be read. Used by the diff viewer to refresh after a
        /// restore without needing the user to save first.
        /// </summary>
        public static Func<int, string?>? ReadEditorSection;

        /// <summary>
        /// Write staged accepts straight to the working file on disk (the stable "Save").
        /// Each block is (committed, working, sectionTag); the Host locates `working` in the
        /// right section's CDATA and replaces it with `committed` (empty committed deletes the
        /// block), applies them all to one in-memory copy, writes once, and stashes the
        /// pre-write bytes for a one-level undo. Returns (applied, failed) counts.
        /// Args: (filePath, blocks, pid).
        /// </summary>
        public static Func<string, System.Collections.Generic.IReadOnlyList<(string committed, string working, string? section)>, int, (int applied, int failed)>? WriteAcceptsToDisk;

        /// <summary>Undo the last <see cref="WriteAcceptsToDisk"/> for this instance by writing
        /// the stashed snapshot back to disk. Returns true when a snapshot was restored.</summary>
        public static Func<int, bool>? UndoLastSave;

        /// <summary>The file path of the document active in TcXaeShell, or null.</summary>
        public static Func<string?>? GetActiveFilePath;

        /// <summary>
        /// The directory of the active TwinCAT solution (.sln), or null. Used as a
        /// second anchor for locating the git repo root, since a POU file can live in
        /// a subtree that doesn't reach the folder where `git init` was run.
        /// </summary>
        public static Func<string?>? GetActiveSolutionDir;
    }
}