using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Paradise.Rendering.WebGPU;
using Paradise.Windowing;
using Paradise.Windowing.Sdl;

namespace Paradise.Rendering.Android.Sample;

/// <summary>Exercises the native Activity, window, Dawn surface and presentation path without middleware.</summary>
internal static partial class AndroidSmoke
{
    public static void Run(ILogger logger)
    {
        if (RuntimeFeature.IsDynamicCodeSupported || RuntimeFeature.IsDynamicCodeCompiled)
        {
            throw new PlatformNotSupportedException("The Android launcher requires NativeAOT, without JIT or interpreter fallback.");
        }
        LogNativeAot(logger);
        using var platform = new SdlWindowPlatform(logger);
        using var window = platform.CreateWindow(new WindowOptions("Paradise Android", 1280, 720));
        var surface = window.CreateSurface();
        using var renderer = new WebGpuRenderer(in surface, logger: logger);
        LogReady(logger, surface.Width, surface.Height, renderer.ColorFormat);
        var clock = Stopwatch.StartNew();
        uint width = window.Width;
        uint height = window.Height;
        var red = 0.15f;
        var blue = 0.55f;
        var frames = 0;
        while (!window.CloseRequested)
        {
            // SDLActivity owns Android's UI loop; this pump runs on the SDL application thread.
            // Full session/surface replacement is a later roadmap milestone, not inferred here.
            platform.Pump();
            if (window.CloseRequested) break;
            while (window.TryReadEvent(out var timed))
            {
                var input = timed.Event;
                if (input.Source == EventSource.Touch && input.Kind == WindowEventKind.Button && input.Pressed)
                {
                    red = Math.Clamp(input.X / Math.Max(1u, window.Width), 0f, 1f);
                    blue = Math.Clamp(input.Y / Math.Max(1u, window.Height), 0f, 1f);
                    LogTouch(logger, input.Slot);
                }
            }
            if (width != window.Width || height != window.Height)
            {
                width = window.Width;
                height = window.Height;
                renderer.Resize(width, height);
            }
            var green = 0.2f + 0.1f * MathF.Sin((float)clock.Elapsed.TotalSeconds);
            renderer.RenderClearFrame(new ColorRgba(red, green, blue, 1f));
            if (++frames == 1) LogFirstFrame(logger);
        }
        LogStopped(logger, frames);
    }

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Android NativeAOT entry point reached; dynamic code is disabled")]
    private static partial void LogNativeAot(ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Android Dawn surface ready: {Width}x{Height}, format {Format}")]
    private static partial void LogReady(ILogger logger, uint width, uint height, TextureFormat format);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Android first frame submitted")]
    private static partial void LogFirstFrame(ILogger logger);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Android touch slot {Slot}")]
    private static partial void LogTouch(ILogger logger, byte slot);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Android smoke stopped after {Frames} frames")]
    private static partial void LogStopped(ILogger logger, int frames);
}
