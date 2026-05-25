using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace STFormatter.Discover
{
    // Service GUID classes for GetService (only those not in Microsoft.VisualStudio.Interop)
    [ComVisible(true)]
    [Guid("72D910D0-A483-4E0E-9BCE-2526CD1B2C27")]
    internal class SVsShellMonitorSelection { }

    // VS interop interfaces not fully covered by the NuGet package
    [ComImport]
    [Guid("72D910D0-A483-4E0E-9BCE-2526CD1B2C27")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVsMonitorSelection
    {
        [PreserveSig]
        int GetCurrentSelection(
            out IVsHierarchy ppHier,
            out uint pitemid,
            out IVsMultiItemSelect ppMIS,
            out ISelectionContainer ppSC);

        [PreserveSig]
        int GetCmdUIContextCookie(ref Guid rguidCmdUI, out uint pdwCmdUICookie);

        [PreserveSig]
        int IsCmdUIContextActive(uint dwCmdUICookie, out int pfActive);
    }

    [ComImport]
    [Guid("804E932A-5342-471E-92C6-488784EAABBC")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVsMultiItemSelect { }

    [ComImport]
    [Guid("3380CD7E-3A4E-4A0D-AAB3-0675C740AAA4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISelectionContainer { }

    internal struct OLECMD
    {
        public uint cmdID;
        public uint cmdf;
    }

    internal enum VSHPROPID
    {
        Name = -1002
    }

    internal enum _VSRDTFLAGS
    {
        RDT_NoLock = 0,
        RDT_ReadOnly = 1,
        RDT_EditLock = 2,
        RDT_ReadLock = 4,
    }

    internal enum VSFPROPID
    {
        Caption = -4004,
        DocView = -4006,
        DocData = -4007,
        guidEditorType = -4012,
    }
}