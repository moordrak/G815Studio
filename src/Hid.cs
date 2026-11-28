using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Svitac;

/// <summary>Opis jednog HID sučelja (Windows svaku "top-level collection" prikazuje kao zaseban uređaj).</summary>
public sealed class HidDeviceInfo
{
    public string Path { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InputReportLength { get; init; }
    public int OutputReportLength { get; init; }

    public override string ToString() =>
        $"{VendorId:X4}:{ProductId:X4} page=0x{UsagePage:X4} usage=0x{Usage:X4} in={InputReportLength} out={OutputReportLength}";
}

internal static class HidNative
{
    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS caps);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll")]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}

public static class HidEnumerator
{
    public static List<HidDeviceInfo> Enumerate(ushort vendorId)
    {
        var result = new List<HidDeviceInfo>();
        HidNative.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

        try
        {
            for (int i = 0; ; i++)
            {
                var ifData = new HidNative.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>()
                };
                if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifData))
                    break;

                HidNative.SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(size);
                try
                {
                    // cbSize strukture SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 na x64, 6 na x86
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref ifData, detail, size, out _, IntPtr.Zero))
                        continue;
                    string path = Marshal.PtrToStringUni(detail + 4);
                    if (string.IsNullOrEmpty(path)) continue;
                    var info = Query(path, vendorId);
                    if (info != null) result.Add(info);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    private static HidDeviceInfo Query(string path, ushort vendorId)
    {
        // Otvaranje bez prava čitanja/pisanja radi i kad uređaj drži netko drugi.
        using var h = HidNative.CreateFile(path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;

        var attr = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
        if (!HidNative.HidD_GetAttributes(h, ref attr) || attr.VendorID != vendorId) return null;
        if (!HidNative.HidD_GetPreparsedData(h, out IntPtr pp)) return null;
        try
        {
            if (HidNative.HidP_GetCaps(pp, out var caps) != HidNative.HIDP_STATUS_SUCCESS) return null;
            return new HidDeviceInfo
            {
                Path = path,
                VendorId = attr.VendorID,
                ProductId = attr.ProductID,
                UsagePage = caps.UsagePage,
                Usage = caps.Usage,
                InputReportLength = caps.InputReportByteLength,
                OutputReportLength = caps.OutputReportByteLength
            };
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(pp);
        }
    }
}

/// <summary>Otvoreno HID sučelje za čitanje i pisanje izvještaja (overlapped I/O preko FileStreama).</summary>
public sealed class HidStream : IDisposable
{
    private readonly FileStream _fs;
    private readonly object _writeLock = new();

    public int InputReportLength { get; }
    public int OutputReportLength { get; }

    private HidStream(FileStream fs, int inLen, int outLen)
    {
        _fs = fs;
        InputReportLength = inLen;
        OutputReportLength = outLen;
    }

    public static HidStream Open(HidDeviceInfo info)
    {
        var h = HidNative.CreateFile(info.Path, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING,
            HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            throw new Win32Exception(err);
        }
        var fs = new FileStream(h, FileAccess.ReadWrite, 0, true);
        return new HidStream(fs, info.InputReportLength, info.OutputReportLength);
    }

    public void Write(byte[] report)
    {
        var buf = new byte[OutputReportLength];
        Array.Copy(report, buf, Math.Min(report.Length, buf.Length));
        lock (_writeLock)
        {
            _fs.Write(buf, 0, buf.Length);
        }
    }

    public async Task<byte[]> ReadAsync(CancellationToken ct)
    {
        var buf = new byte[InputReportLength];
        int n = await _fs.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
        if (n <= 0) throw new IOException(L.T("Uređaj je odspojen."));
        if (n < buf.Length) Array.Resize(ref buf, n);
        return buf;
    }

    public void Dispose()
    {
        try { _fs.Dispose(); } catch { /* ignoriraj */ }
    }
}
