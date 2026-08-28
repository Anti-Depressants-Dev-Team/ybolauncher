using System.Runtime.InteropServices;
using System.Text;

namespace Launcher.Core.Interop;

/// <summary>
/// Win32 and COM declarations used by shortcut resolution and icon extraction.
/// <para>
/// Classic <c>[ComImport]</c> / <c>[DllImport]</c> interop is used rather than the
/// source-generated equivalents: several of these signatures (HRESULT-throwing COM
/// methods, StringBuilder out-params) are not expressible with
/// <c>[GeneratedComInterface]</c> / <c>[LibraryImport]</c>, and the app is not published
/// with NativeAOT, which is the only reason to prefer them.
/// </para>
/// </summary>
internal static class NativeMethods
{
    internal const int MaxPath = 260;

    /// <summary>CLSID_ShellLink.</summary>
    internal static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    /// <summary>IID_IShellItemImageFactory.</summary>
    internal static readonly Guid ShellItemImageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>PKEY_AppUserModel_ID - the AUMID a shortcut points at, when it has one.</summary>
    internal static readonly PropertyKey AppUserModelIdKey =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    /// <summary>SLGP_RAWPATH: return the target without resolving environment variables.</summary>
    internal const uint SlgpRawPath = 0x4;

    /// <summary>SLR_NO_UI | SLR_NOSEARCH | SLR_NOTRACK: never prompt, never hunt for a moved target.</summary>
    internal const uint SlrNoUiNoSearchNoTrack = 0x1 | 0x10 | 0x2;

    /// <summary>STGM_READ.</summary>
    internal const uint StgmRead = 0x0;

    /// <summary>VT_LPWSTR.</summary>
    internal const ushort VtLpwstr = 31;

    /// <summary>SIIGBF_BIGGERSIZEOK: allow a larger source image, we scale it ourselves.</summary>
    internal const int SiigbfBiggerSizeOk = 0x1;

    /// <summary>SIIGBF_ICONONLY: never return a document thumbnail in place of the app icon.</summary>
    internal const int SiigbfIconOnly = 0x4;

    /// <summary>BI_RGB.</summary>
    internal const int BiRgb = 0;

    /// <summary>DIB_RGB_COLORS.</summary>
    internal const uint DibRgbColors = 0;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cch,
            IntPtr pfd,
            uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cch,
            out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    /// <summary>IPersistFile. Inherits IPersist, so GetClassID must stay first in the vtable.</summary>
    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount(out uint cProps);

        void GetAt(uint iProp, out PropertyKey pkey);

        void GetValue(ref PropertyKey key, out PropVariant pv);

        void SetValue(ref PropertyKey key, ref PropVariant pv);

        void Commit();
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    /// <summary>
    /// Enough of PROPVARIANT to read a VT_LPWSTR. The union starts at offset 8 on both
    /// 32- and 64-bit, after the 2-byte type and three reserved words.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr Pointer2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size(int cx, int cy)
    {
        public int Width = cx;
        public int Height = cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public int Color0;
        public int Color1;
        public int Color2;
        public int Color3;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant pvar);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetObject(IntPtr handle, int size, ref Bitmap target);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint start,
        uint lines,
        IntPtr bits,
        ref BitmapInfo info,
        uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);

    /// <summary>SPI_GETHIGHCONTRAST.</summary>
    internal const uint SpiGetHighContrast = 0x0042;

    /// <summary>HCF_HIGHCONTRASTON.</summary>
    internal const uint HighContrastOn = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct HighContrastInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr DefaultScheme;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint param,
        ref HighContrastInfo data,
        uint winIni);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
}
