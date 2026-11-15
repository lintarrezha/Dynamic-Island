using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace iLand.Native;

/// <summary>
/// Jembatan ke Windows API untuk perekaman layar:
/// 1. Membuat GraphicsCaptureItem untuk sebuah monitor (lewat IGraphicsCaptureItemInterop).
/// 2. Membuat perangkat Direct3D 11 yang dibutuhkan penangkap layar dan encoder video.
/// Semua API di sini resmi dan terdokumentasi oleh Microsoft.
/// </summary>
internal static class GraphicsCaptureInterop
{
    // IID dari GraphicsCaptureItem dan IDXGIDevice (nilai tetap dari Windows SDK).
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceGuid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D_DRIVER_TYPE_WARP = 5;          // perender software, cadangan jika GPU tidak tersedia
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;

    /// <summary>Antarmuka COM milik Windows untuk membuat GraphicsCaptureItem dari handle monitor atau window.</summary>
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGraphicsCaptureItemInterop
    {
        // Urutan dua method ini TIDAK boleh ditukar: COM memanggilnya berdasarkan urutan.
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter, uint driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out uint featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>Membuat objek penangkap untuk satu monitor penuh.</summary>
    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitorHandle)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItemGuid;

        IntPtr itemPointer = interop.CreateForMonitor(monitorHandle, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            // FromAbi menambah hitungan referensinya sendiri, jadi referensi dari kita dilepas.
            Marshal.Release(itemPointer);
        }
    }

    /// <summary>Membuat perangkat Direct3D 11 (GPU, atau WARP jika GPU tidak tersedia).</summary>
    public static IDirect3DDevice CreateDevice()
    {
        int result = D3D11CreateDevice(
            IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero, 0, D3D11_SDK_VERSION,
            out IntPtr d3dDevice, out _, out IntPtr context);

        if (result < 0)
        {
            result = D3D11CreateDevice(
                IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0, D3D11_SDK_VERSION,
                out d3dDevice, out _, out context);
        }

        Marshal.ThrowExceptionForHR(result);

        IntPtr dxgiDevice = IntPtr.Zero;
        IntPtr graphicsDevice = IntPtr.Zero;
        try
        {
            Guid dxgiIid = DxgiDeviceGuid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in dxgiIid, out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out graphicsDevice));

            return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
        }
        finally
        {
            // Semua pointer COM yang kita pegang dilepas. Objek IDirect3DDevice hasilnya
            // memegang referensinya sendiri, jadi perangkat tetap hidup.
            if (graphicsDevice != IntPtr.Zero)
            {
                Marshal.Release(graphicsDevice);
            }

            if (dxgiDevice != IntPtr.Zero)
            {
                Marshal.Release(dxgiDevice);
            }

            if (context != IntPtr.Zero)
            {
                Marshal.Release(context);
            }

            Marshal.Release(d3dDevice);
        }
    }
}
