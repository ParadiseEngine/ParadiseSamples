using System.Numerics;
using Microsoft.Extensions.Logging;
using Paradise.Assets.Gltf;
using Paradise.Features;
using Paradise.Rendering.Pbr;

namespace Paradise.Rendering.Sample;

/// <summary>The shared renderer laboratory scene, independent of its desktop or browser host.</summary>
internal sealed class RendererShowcaseScene : IDisposable
{
    private readonly GiDemoScene _room;
    private readonly SkinnedMannequin _rig = new();
    private readonly Matrix4x4[] _palette = new Matrix4x4[SkinnedMannequin.JointCount];
    private int _frame;
    internal GiDemoScene Room => _room;
    internal PbrRenderer Renderer => _room.Renderer;
    internal PbrScene Scene => _room.Scene;

    internal RendererShowcaseScene(IRenderer backend, FeatureSwitches features, uint width, uint height, ILogger logger)
    {
        GiDemoScene.ProbeGi = GiDemoScene.RayTracedAo = true;
        GiDemoScene.Decals = GiDemoScene.Fog = GiDemoScene.Reflections = true;
        GiDemoScene.ExtraLights = 20;
        GiDemoScene.RaysPerProbe = 32;
        GiDemoScene.MaxProbes = 512;
        _room = new GiDemoScene(backend, features, width, height, null, logger);
        _room.Drag(0, 0);
        Scene.Ssao = new PbrSsao { Enabled = true };
        Scene.RayTracedAo = new PbrRayTracedAo { Enabled = true, RaysPerPixel = 4 };
        Scene.ContactShadows = new PbrContactShadows { Enabled = true };
        Scene.Visibility.OcclusionEnabled = true;
        Scene.MotionVectors = new PbrMotionVectors { Enabled = true };
        Scene.Taa = new PbrTaa { Enabled = true };
        Scene.Fxaa = new PbrFxaa { Enabled = true };
        Scene.Exposure = new PbrExposure { Enabled = true, CompensationEv = 0.2f };
        Scene.DepthOfField = new PbrDepthOfField { Enabled = true, FocusDistance = 8, MaxRadiusPixels = 5 };
        Scene.MotionBlur = new PbrMotionBlur { Enabled = true, MaxRadiusPixels = 8 };
        Scene.ColorGrading = new PbrColorGrading { Enabled = true, Saturation = 1.08f, Temperature = 0.1f };
        Scene.LensDistortion = new PbrLensDistortion { Enabled = true, Strength = 0.025f };
        Scene.ChromaticAberration = new PbrChromaticAberration { Enabled = true, IntensityPixels = 0.6f };
        Scene.Vignette = new PbrVignette { Enabled = true, Intensity = 0.15f };
        Scene.FilmGrain = new PbrFilmGrain { Enabled = true, Intensity = 0.008f };
        Scene.Sharpening = new PbrSharpening { Enabled = true, Strength = 0.15f };
        Renderer.Pipeline.Find<ShadowFeature>()!.MapSize = 2048;
        AddGeometry();
    }

    private void AddGeometry()
    {
        var (vertices, indices) = Procedural.UnitCube();
        PbrMesh Cube(Vector4 color, float metallic = 0, float roughness = 0.6f) => new([
            Renderer.UploadPrimitive(vertices, indices, Renderer.Materials.AddDefaultMaterial(color, metallic, roughness))]);
        void Add(PbrMesh mesh, Vector3 scale, Vector3 position, PbrGiMode gi = PbrGiMode.Static) => Scene.Instances.Add(new PbrInstance
        { Mesh = mesh, Model = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position), GiMode = gi });
        // Glossy floor catches SSR and the moving character. Repeated columns share one upload.
        Add(Cube(new Vector4(0.6f, 0.65f, 0.7f, 1), 0.85f, 0.12f), new Vector3(2.4f, 0.04f, 2), new Vector3(1.3f, 0.03f, 1.5f));
        var glassProgram = Renderer.RegisterMaterialProgram(
            ShaderProgramLoader.Load(typeof(RendererShowcaseScene).Assembly, "Shaders.showcaseGlass"));
        var glass = new GltfMaterialData("Glass", new Vector4(0.3f, 0.8f, 0.9f, 0.35f), 0, 0.12f,
            Vector3.Zero, 1, 1, 0, GltfAlphaMode.Blend, 0.5f, true,
            -1, -1, -1, -1, -1, GltfUvTransform.Identity);
        var glassMaterial = Renderer.Materials.AddMaterial(in glass, [], glassProgram, [],
            [new MaterialTarget(7, PbrTargets.SceneColor)]);
        Add(new PbrMesh([Renderer.UploadPrimitive(vertices, indices, glassMaterial)]),
            new Vector3(1.1f, 1.7f, 0.04f), new Vector3(-1.6f, 0.9f, 2.1f));
        var repeated = Cube(new Vector4(0.8f, 0.55f, 0.12f, 1), 0.4f);
        for (var i = 0; i < 30; i++)
            Add(repeated, new Vector3(0.13f, 0.4f + i % 3 * 0.12f, 0.13f), new Vector3(-2.6f + i % 10 * 0.55f, 0.3f, -2.5f + i / 10 * 0.4f));
        // Deliberately hidden and off-camera draws give both culling controls observable work.
        for (var i = 0; i < 12; i++)
            Add(repeated, new Vector3(0.15f), new Vector3(i < 6 ? -1.1f : 30f, 0.4f + i * 0.05f, -1.3f), PbrGiMode.Disabled);
        var skin = Renderer.UploadSkinnedPrimitive(_rig.Vertices, _rig.JointsWeights, _rig.Indices,
            Renderer.Materials.AddDefaultMaterial(new Vector4(0.05f, 0.65f, 0.9f, 1)));
        Scene.Instances.Add(new PbrInstance { Mesh = new PbrMesh([skin]), JointOffset = 0,
            Model = Matrix4x4.CreateScale(0.65f) * Matrix4x4.CreateTranslation(1.9f, 0.1f, 1.8f), GiMode = PbrGiMode.Dynamic });
        Scene.Lights.Add(new PbrLight { Type = PbrLightType.Spot, Position = new Vector3(-2, 3, 2),
            Direction = Vector3.Normalize(new Vector3(1, -2, -2)), Color = new Vector3(1, 0.5f, 0.2f),
            Range = 8, Intensity = 7, CastsShadows = true, SoftShadows = true, SpotOuterDegrees = 38, SpotInnerDegrees = 22 });
    }

    public void RenderFrame(bool paused = false, bool softShadows = true)
    {
        _rig.Pose(_frame / 60f, Matrix4x4.Identity, _palette);
        Renderer.SetJointPalette(0, _palette);
        _room.SoftShadowsOverride = softShadows;
        _room.RenderFrame(!paused);
        if (!paused) _frame++;
    }

    public void Dispose() => _room.Dispose();
}
