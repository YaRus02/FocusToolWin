using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using WinRT.Interop;

namespace FocusTool.Win.Capture;

/// <summary>
/// Win32/WinRT/Direct3D bridging needed for Windows Graphics Capture:
/// creating a capture item for a window, turning a Direct3D11 device into the
/// WinRT <see cref="IDirect3DDevice"/> the frame pool expects, and pulling the
/// native texture out of a captured frame surface.
/// </summary>
internal static class CaptureInterop
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = ActivationFactory
            .Get("Windows.Graphics.Capture.GraphicsCaptureItem")
            .AsInterface<IGraphicsCaptureItemInterop>();

        var iid = GraphicsCaptureItemIid;
        var rawItem = interop.CreateForWindow(hwnd, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(rawItem);
        }
        finally
        {
            Marshal.Release(rawItem);
        }
    }

    public static async Task<GraphicsCaptureItem?> PickItemAsync(IntPtr ownerWindow)
    {
        var picker = new GraphicsCapturePicker();
        InitializeWithWindow.Initialize(picker, ownerWindow);
        return await picker.PickSingleItemAsync();
    }

    public static IDirect3DDevice CreateDirect3DDevice(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = Texture2DIid;
        var texturePointer = access.GetInterface(ref iid);
        return new ID3D11Texture2D(texturePointer);
    }
}
