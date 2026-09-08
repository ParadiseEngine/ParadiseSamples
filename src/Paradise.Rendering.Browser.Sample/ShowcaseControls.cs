using System.Runtime.InteropServices.JavaScript;
using Paradise.Rendering.Pbr;

namespace Paradise.Rendering.Browser.Sample;

public static partial class Program
{
    [JSExport]
    internal static int FeatureCount() => s_definitions.Length;

    [JSExport]
    internal static string FeatureName(int index) => s_definitions[index].Name;

    [JSExport]
    internal static string FeatureSummary(int index) => s_definitions[index].Summary;

    [JSExport]
    internal static bool FeatureEnabled(int index) => s_features.IsEnabled(s_definitions[index].Id);

    [JSExport]
    internal static void SetFeature(int index, bool enabled) => s_features.Set(s_definitions[index].Id, enabled);

    [JSExport]
    internal static void RestoreFeatures(bool allOff)
    {
        for (var i = 0; i < s_definitions.Length; i++)
            s_features.Set(s_definitions[i].Id, !allOff && s_initial[i]);
        if (s_showcaseScene is { } showcase) showcase.Scene.TemporalHistoryVersion++;
    }

    [JSExport]
    internal static void Orbit(double x, double y) => s_showcaseScene?.Room.Drag((float)x, (float)y);

    [JSExport]
    internal static void Zoom(double amount) => s_showcaseScene?.Room.Zoom((float)Math.Clamp(amount, -2, 2));

    [JSExport]
    internal static void Resize(int width, int height)
    {
        if (s_showcaseScene is null || s_renderer is null) return;
        var w = (uint)Math.Clamp(width, 320, 2560);
        var h = (uint)Math.Clamp(height, 240, 1600);
        s_renderer.Resize(w, h);
        s_showcaseScene.Room.Resize(w, h);
    }

    [JSExport]
    internal static void SetShowcaseOption(string name, double value)
    {
        if (s_showcaseScene is not { } showcase) return;
        var scene = showcase.Scene;
        switch (name)
        {
            case "pause": s_paused = value != 0; break;
            case "soft": s_softShadows = value != 0; break;
            case "ssao": scene.Ssao = scene.Ssao with { Enabled = value != 0 }; break;
            case "cascades": showcase.Renderer.Pipeline.Find<ShadowFeature>()!.CascadeCount = Math.Clamp((int)value, 1, 4); break;
            case "split": showcase.Renderer.Pipeline.Find<ShadowFeature>()!.CascadeSplitLambda = (float)Math.Clamp(value, 0, 1); break;
            case "focus": scene.DepthOfField = scene.DepthOfField with { FocusDistance = (float)Math.Clamp(value, 2, 15) }; break;
            case "exposure": scene.Exposure = scene.Exposure with { CompensationEv = (float)Math.Clamp(value, -3, 3) }; break;
        }
    }

    [JSExport]
    internal static string ShowcaseStats()
    {
        if (s_showcaseScene is not { } showcase) return "";
        var batching = showcase.Renderer.Pipeline.Find<InstancingFeature>()!;
        var culling = showcase.Renderer.Pipeline.Find<FrustumCullingFeature>()!;
        return $"{showcase.Scene.Instances.Count} objects · {batching.DrawCalls} draws · {batching.SavedDrawCalls} saved · {culling.CulledDrawCount} culled";
    }

    [JSExport]
    internal static string ShowcasePasses() => s_showcaseScene is { } showcase
        ? string.Join("\n", showcase.Renderer.LastPassNames) : "";
}
