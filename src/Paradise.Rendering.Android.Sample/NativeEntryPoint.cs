using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Paradise.Hosting.Android;

namespace Paradise.Rendering.Android.Sample;

internal static class NativeEntryPoint
{
    // SDL's Java launcher resolves this C ABI symbol after initializing its JNI bridge.
    [UnmanagedCallersOnly(EntryPoint = "SDL_main", CallConvs = [typeof(CallConvCdecl)])]
    public static int SdlMain(int argc, nint argv)
    {
        try
        {
            return SdlAndroidHost.Run(AndroidSmoke.Run);
        }
        catch (Exception)
        {
            // Even startup/allocation/logging failures must not cross an unmanaged boundary.
            return 1;
        }
    }
}
