# Android NativeAOT rendering bootstrap

This is an ARM64 foreground clear-color/touch probe, not the full showcase or a device-qualified mobile port. Both Debug and Release compile C# to **NativeAOT**, with no Mono VM, managed Android Activity, or assembly store. The browser WASM/WebGPU sample remains a separate required target.

## Entry point

`Android/MainActivity.java` extends SDL's Java `SDLActivity` and loads SDL, Dawn, then `libparadise_android.so`. SDL resolves the exported `SDL_main` in `NativeEntryPoint.cs` on its application thread. `SdlAndroidHost` invokes `AndroidSmoke.Run` behind an exception boundary; logcat uses native `liblog.so` rather than Java/.NET bindings.

The project targets ordinary `net10.0` / `linux-bionic-arm64` and publishes a NativeAOT shared library. This is the .NET runtime's experimental NDK/Bionic route, not a glibc Linux executable or a Mono AOT application. `PublishAot`, `NativeLib=Shared`, and the RID are checked; disabling AOT or substituting a Mono/desktop RID is an error. NativeAOT retains runtime/GC support inside the compiled library; it is not a runtime-free conversion.

The native library excludes NuGet's native assets from WebGPUSharp and SDL3-CS. The APK recipe supplies only verified Android binaries. The Java SDL bridge is pinned to the same SDL3-CS release as the native SDL library, not an unrelated upstream jar. The manifest uses `extractNativeLibs=true` because the SDL loader resolves its main library from `nativeLibraryDir`.

## Prerequisites and local feed

Use .NET SDK 10.0.400+, Python 3.10+, JDK 21, Android platform/build-tools 36, and NDK 28.2.13676358. The .NET Android workload and Gradle are not needed. The initial platform floor is API 26 / ARM64, with the Vulkan/WebGPU device matrix still unqualified.

Follow the engine's `docs/development/android.md` for the pinned Dawn build and six-package local feed. Use one distinct version containing the new `Paradise.Hosting.Android` API, for example `0.48.0-android.2`. **Keep engine dependencies as NuGet references**, not local project references. Pass `ParadiseVersion` until the repository's default engine version includes this host.

## Build a complete APK

From the matching engine checkout:

```sh
python3 tools/android/build_nativeaot.py \
  --samples /path/to/ParadiseSamples \
  --version 0.48.0-android.2 --feed ./artifacts/feed \
  --native-dir ./artifacts/native/android-arm64 \
  --sdk "$ANDROID_HOME" --jdk "$JAVA_HOME" \
  --cache ./artifacts/android-cache --out ./artifacts/android \
  --configuration Release
```

Repeat with `--configuration Debug`. Pass `--dotnet /path/to/dotnet` or `--ndk /path/to/ndk` for nondefault tool installations. On PowerShell, use backticks instead of backslashes for continued command lines.

The recipe runs NativeAOT publish, Java compilation, D8/aapt2 packaging, 16 KB checks, alignment and test signing. Outputs:

```text
ParadiseAndroid-NativeAOT-Debug.apk
ParadiseAndroid-NativeAOT-Release.apk
ParadiseAndroid-NativeAOT-<configuration>-validation.json
native/<configuration>/paradise_android.so
native/<configuration>/paradise_android.so.dbg
```

Available native debug symbols are retained outside the APK. Preserve the recipe's private cache/development keystore when updating test installations. Both configurations are test signed, not store signed. The old `dotnet build -t:SignAndroidPackage` and `RunAOTCompilation` instructions no longer apply. Plain `dotnet publish` produces the `.so`, not a complete APK.

`ParadiseSamples.Android.slnx` selects this project without changing the regular desktop/browser solution. Android build/distribution tooling comes from the matching engine checkout; ordinary published desktop/browser samples still do not require one.

## Verify and launch

The builder performs validation automatically. To repeat it from the engine checkout:

```sh
python3 tools/android/verify_android.py \
  --native-dir ./artifacts/native/android-arm64 \
  --apk ./artifacts/android/ParadiseAndroid-NativeAOT-Release.apk
```

The gate verifies every ELF, native dependencies, build checksums and the `SDL_main`/NativeAOT runtime exports. Mono libraries, embedded managed assemblies, unexpected native payloads and unsupported alignment fail the check. Signature/ZIP alignment are also verified by Android's build tools.

On a connected ARM64 test device:

```sh
adb install -r /path/to/ParadiseAndroid-NativeAOT-Release.apk
adb shell am start -n org.paradiseengine.androidsmoke/.MainActivity
adb logcat -s Paradise SDL
```

A previous APK signed by another development key requires uninstall/reinstall, which deletes its app data. Do not automatically uninstall an existing application just to bypass a signing error.

Look for `Android NativeAOT entry point reached; dynamic code is disabled`, then `Android first frame submitted`. Confirm the actual visible animation and touch changes; neither log establishes correct pixels. No device or emulator was connected during the initial NativeAOT build verification.

## Still unqualified

Native startup on real hardware, suspend/resume, drawable replacement, safe areas/orientation, soft keyboard, device loss, PBR/cooked assets/texture transcoding, UI/audio middleware and real-browser scene parity remain in engine #323. Android NativeAOT is experimental upstream. Successful compilation, package inspection and host unit tests must not be presented as completed mobile runtime qualification.

## Activity theme

The launcher explicitly uses `@android:style/Theme.Material.NoActionBar`. This removes Android's Activity title/banner before SDL creates the view. It is separate from immersive system bars, which remain controlled by the engine fullscreen default and `WindowOptions.Fullscreen`. The engine APK recipe rejects missing or title-producing launcher themes.
