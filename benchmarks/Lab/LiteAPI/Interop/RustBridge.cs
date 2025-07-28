using System.Runtime.InteropServices;

namespace LiteAPI.Interop;

internal static class RustBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr HandleRequestDelegate(IntPtr method, IntPtr path, IntPtr body);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FreeStringDelegate(IntPtr ptr);

    [DllImport("liteapi_rust", CallingConvention = CallingConvention.Cdecl)]
    private static extern int start_listener(IntPtr handleCb, IntPtr freeCb);

    public static void StartRustListener(Router router)
    {
        HandleRequestDelegate handleCb = (methodPtr, pathPtr, bodyPtr) =>
        {
            var method = Marshal.PtrToStringAnsi(methodPtr) ?? "GET";
            var path = Marshal.PtrToStringAnsi(pathPtr) ?? "/";
            var body = Marshal.PtrToStringAnsi(bodyPtr) ?? "";

            var response = router.HandleRawRequest(method, path, body);
            var str = response.GetBodyAsString();
            return Marshal.StringToHGlobalAnsi(str);
        };

        FreeStringDelegate freeCb = (ptr) =>
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);
        };

        var handlePtr = Marshal.GetFunctionPointerForDelegate(handleCb);
        var freePtr = Marshal.GetFunctionPointerForDelegate(freeCb);

        start_listener(handlePtr, freePtr);
    }
}