using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;
using Paradise.Animation;
using Paradise.Animation.Offline;
using Paradise.BLOB;
using Paradise.Features;
using Paradise.Rendering.Graph;
using Paradise.Rendering.Pbr;
using Paradise.Rendering.WebGPU;
using Paradise.Ui.ImGui;
using Paradise.Windowing;
using Paradise.Windowing.Sdl;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Paradise.Rendering.Sample;

/// <summary>Four skinned mannequins, each showing one part of <see cref="AnimationPlayer"/>: idle, walk and run at
/// independent weights in one sync group; cross-fades started faster than they finish; a wave overriding the walk's
/// upper body; an additive lean over the run. Every clip is keyed from a formula at startup, so the demo needs no
/// asset.</summary>
internal sealed class AnimationMixerDemo : IDisposable
{
    private const float KeysPerSecond = 30f;
    private const int Hips = 0, Chest = 1, Head = 2, LeftUpperArm = 3, LeftForearm = 4, RightUpperArm = 5, RightForearm = 6;
    private const int LeftThigh = 7, LeftShin = 8, RightThigh = 9, RightShin = 10;

    /// <summary>Fills one rotation per joint for a phase in 0..2π and returns the hips' height above rest.</summary>
    private delegate float Posing(float phase, Span<Quaternion> rotations);

    private sealed class MixerOverlay(WebGpuHostPass callback) : IRenderFeature
    {
        public FeatureDefinition Definition { get; } = new("animationMixer.overlay", true, "Animation mixer controls.");
        public FrameRequirements Requires => FrameRequirements.None;
        public void Resize(uint width, uint height) { }
        public void Dispose() { }
        public void Setup(in FrameContext frame) => frame.Graph.AddHostPass("ImGui", RenderPassEvent.Overlay, callback, FrameGraph.Backbuffer);
    }

    private sealed class Character(string title, AnimationPlayer player, int offset)
    {
        public string Title { get; } = title;
        public AnimationPlayer Player { get; } = player;
        public int Offset { get; } = offset;
        public Vector3[] Previous { get; } = new Vector3[SkinnedMannequin.JointCount];
        public bool HasPrevious { get; set; }

        /// <summary>The farthest any joint moved between two frames, in metres: a pop shows here as an outlier.</summary>
        public float MaxStep { get; set; }
    }

    private readonly PbrRenderer _pbr;
    private readonly PbrScene _scene = new();
    private readonly NativeBlobAssetReference<SkeletonBlob> _skeleton;
    private readonly NativeBlobAssetReference<AnimationBlob> _idle;
    private readonly NativeBlobAssetReference<AnimationBlob> _walk;
    private readonly NativeBlobAssetReference<AnimationBlob> _run;
    private readonly NativeBlobAssetReference<AnimationBlob> _wave;
    private readonly NativeBlobAssetReference<AdditiveAnimationBlob> _lean;
    private readonly int[] _paletteJoints = new int[SkinnedMannequin.JointCount];
    private readonly Matrix4x4[] _inverseBinds = new Matrix4x4[SkinnedMannequin.JointCount];
    private readonly Matrix4x4[] _palette = new Matrix4x4[SkinnedMannequin.JointCount];
    private readonly Character[] _characters = new Character[4];

    private readonly PlaybackHandle[] _gaits = new PlaybackHandle[3];
    private readonly float[] _gaitWeights = [1f, 0f, 0f];
    private SyncGroupHandle _locomotion;
    private bool _autoMix = true;

    private readonly (PlaybackHandle Playback, string Name)[] _recent = new (PlaybackHandle, string)[8];
    private int _recentCount;
    private bool _autoSwitch = true;
    private float _switchInterval = 0.3f;
    private float _fadeSeconds = 0.6f;
    private float _untilSwitch = 0.3f;
    private int _switches;

    private LayerHandle _upper;
    private bool _autoOverride = true;
    private float _overrideWeight;

    private LayerHandle _additive;
    private bool _autoAdditive = true;
    private float _additiveWeight;

    private float _time;
    private bool _paused;
    private float _yaw = 0.12f;
    private float _pitch = 0.25f;
    private float _distance = 8.5f;
    private uint _width;
    private uint _height;

    private AnimationMixerDemo(WebGpuRenderer backend, uint width, uint height, ILogger logger)
    {
        _width = width;
        _height = height;
        _pbr = new PbrRenderer(backend, Program.Features, width, height, logger: logger);
        _skeleton = Skeleton();
        _idle = AnimationBuilder.Build(Keys("idle", 2.4f, Idle));
        _walk = AnimationBuilder.Build(Keys("walk", MathF.Tau / 5.5f, Walk));
        _run = AnimationBuilder.Build(Keys("run", 0.72f, Run));
        _wave = AnimationBuilder.Build(Keys("wave", 1.2f, Wave));
        // The lean is taken against the rest pose, so it tilts whatever pose it is added to.
        _lean = AdditiveAnimationBuilder.Build(Keys("lean", 3f, Lean), _skeleton.Value.RestPoses.ToArray());

        var bind = SkinnedMannequin.BindPositions;
        for (var joint = 0; joint < _paletteJoints.Length; joint++)
        {
            _paletteJoints[joint] = joint;
            _inverseBinds[joint] = Matrix4x4.CreateTranslation(-bind[joint]);
        }

        BuildScene();

        // 1 · Every gait plays at once at its own weight; the sync group keeps their steps aligned while the weights move.
        var mix = _characters[0].Player;
        _locomotion = mix.AddSyncGroup();
        _gaits[0] = mix.Add(_idle, 1f);
        _gaits[1] = mix.Add(_walk, 0f);
        _gaits[2] = mix.Add(_run, 0f);
        foreach (var gait in _gaits) mix.Synchronize(gait, _locomotion);

        // 2 · Each Play fades from wherever the last one had got to.
        Remember(_characters[1].Player.Play(_idle), "idle");

        // 3 · The wave replaces only the joints under the chest; the walk keeps the legs.
        var waving = _characters[2].Player;
        waving.Play(_walk);
        _upper = waving.AddLayer(AnimationLayerMode.Override, 0f, JointMask.Branch(ref _skeleton.Value, "chest"));
        waving.Play(_wave, _upper);

        // 4 · The lean is added on top of the run, at whatever strength its layer has.
        var leaning = _characters[3].Player;
        leaning.Play(_run);
        _additive = leaning.AddLayer(AnimationLayerMode.Additive, 0f);
        leaning.Play(_lean, _additive);
    }

    public PbrRenderer Renderer => _pbr;

    private void BuildScene()
    {
        var (cube, cubeIndices) = Procedural.UnitCube();
        var floor = _pbr.Materials.AddDefaultMaterial(new Vector4(0.42f, 0.44f, 0.47f, 1f), metallic: 0f, roughness: 0.85f);
        _scene.Instances.Add(new PbrInstance
        {
            Mesh = new PbrMesh([_pbr.UploadPrimitive(cube, cubeIndices, floor)]),
            Model = Matrix4x4.CreateScale(16f, 0.2f, 10f) * Matrix4x4.CreateTranslation(0f, -0.1f, 0f),
        });

        var mannequin = new SkinnedMannequin();
        Vector4[] colors =
        [
            new(0.15f, 0.4f, 0.95f, 1f), new(0.95f, 0.5f, 0.12f, 1f), new(0.2f, 0.75f, 0.3f, 1f), new(0.62f, 0.3f, 0.85f, 1f),
        ];
        string[] titles = ["1 - Synchronized mix", "2 - Interrupted cross-fades", "3 - Upper-body override", "4 - Additive lean"];
        for (var i = 0; i < _characters.Length; i++)
        {
            var material = _pbr.Materials.AddDefaultMaterial(colors[i], metallic: 0f, roughness: 0.55f);
            var offset = i * SkinnedMannequin.JointCount;
            _scene.Instances.Add(new PbrInstance
            {
                Mesh = new PbrMesh([_pbr.UploadSkinnedPrimitive(mannequin.Vertices, mannequin.JointsWeights, mannequin.Indices, material)]),
                Model = Matrix4x4.CreateTranslation(-3.3f + 2.2f * i, 0f, 0f),
                JointOffset = offset,
                GiMode = PbrGiMode.Dynamic,
            });
            _characters[i] = new Character(titles[i], new AnimationPlayer(_skeleton, playbackCapacity: 8), offset);
        }

        _scene.HasSkyBackground = true;
        _scene.SkyTopColor = new Vector3(0.25f, 0.45f, 0.85f);
        _scene.SkyHorizonColor = new Vector3(0.7f, 0.75f, 0.85f);
        _scene.SkyGroundHorizon = new Vector3(0.35f, 0.33f, 0.3f);
        _scene.SkyGroundBottom = new Vector3(0.12f, 0.11f, 0.1f);
        _scene.Ambient = new PbrAmbient
        {
            Sky = new Vector3(0.35f, 0.4f, 0.5f),
            Equator = new Vector3(0.22f, 0.22f, 0.24f),
            Ground = new Vector3(0.08f, 0.07f, 0.06f),
        };
        _scene.Tonemap = new PbrTonemap { Mode = PbrTonemapMode.Filmic, Exposure = 0.9f, White = 4f };
        _scene.Lights.Add(new PbrLight
        {
            Type = PbrLightType.Directional,
            Direction = Vector3.Normalize(new Vector3(0.35f, 0.9f, 0.6f)),
            Color = new Vector3(1f, 0.96f, 0.9f),
            Intensity = 1.3f,
            CastsShadows = true,
            SoftShadows = true,
            Size = 1f,
        });
    }

    /// <summary>The mannequin's joints with their bind offsets as rest poses, in its own depth-first order.</summary>
    private static NativeBlobAssetReference<SkeletonBlob> Skeleton()
    {
        var bind = SkinnedMannequin.BindPositions;
        var parents = SkinnedMannequin.Parents;
        var names = SkinnedMannequin.JointNames;
        var raw = new RawSkeleton();
        var joints = new RawJoint[bind.Length];
        for (var joint = 0; joint < bind.Length; joint++)
        {
            var parent = parents[joint];
            joints[joint] = new RawJoint(names[joint])
            {
                Transform = new JointPose(parent < 0 ? bind[joint] : bind[joint] - bind[parent], Quaternion.Identity, Vector3.One),
            };
            if (parent < 0) raw.Roots.Add(joints[joint]);
            else joints[parent].Children.Add(joints[joint]);
        }

        var skeleton = SkeletonBuilder.Build(raw);
        // The mesh's skin indexes joints in the mannequin's order; the builder's depth-first order must be the same.
        for (var joint = 0; joint < names.Length; joint++)
        {
            if (skeleton.Value.Names[joint].ToString() != names[joint]) throw new InvalidOperationException($"Joint {joint} built as '{skeleton.Value.Names[joint].ToString()}', skinned as '{names[joint]}'.");
        }

        return skeleton;
    }

    /// <summary>Keys every joint at 30 Hz over one cycle: rotations from <paramref name="posing"/>, the hips' bob, and each joint's rest offset, which the builder would otherwise leave at zero.</summary>
    private static RawAnimation Keys(string name, float duration, Posing posing)
    {
        var bind = SkinnedMannequin.BindPositions;
        var parents = SkinnedMannequin.Parents;
        var raw = new RawAnimation { Name = name, Duration = duration };
        for (var joint = 0; joint < bind.Length; joint++)
        {
            var track = new RawTrack();
            if (joint != Hips) track.Translations.Add(new TranslationKey(0f, bind[joint] - bind[parents[joint]]));
            raw.Tracks.Add(track);
        }

        Span<Quaternion> rotations = stackalloc Quaternion[bind.Length];
        var frames = (int)MathF.Ceiling(duration * KeysPerSecond);
        for (var frame = 0; frame <= frames; frame++)
        {
            var time = MathF.Min(frame / KeysPerSecond, duration);
            rotations.Fill(Quaternion.Identity);
            var bob = posing(MathF.Tau * time / duration, rotations);
            raw.Tracks[Hips].Translations.Add(new TranslationKey(time, bind[Hips] + new Vector3(0f, bob, 0f)));
            for (var joint = 0; joint < bind.Length; joint++) raw.Tracks[joint].Rotations.Add(new RotationKey(time, rotations[joint]));
        }

        return raw;
    }

    private static Quaternion AboutX(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle);

    private static Quaternion AboutZ(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);

    private static float Idle(float phase, Span<Quaternion> r)
    {
        var breath = MathF.Sin(phase);
        r[Chest] = Quaternion.CreateFromYawPitchRoll(0f, 0.04f + 0.03f * breath, 0f);
        r[Head] = Quaternion.CreateFromYawPitchRoll(0.12f * MathF.Sin(phase), 0.03f * breath, 0f);
        r[LeftUpperArm] = AboutZ(0.12f + 0.03f * breath);
        r[RightUpperArm] = AboutZ(-0.12f - 0.03f * breath);
        r[LeftForearm] = r[RightForearm] = AboutX(-0.15f);
        return 0.012f * breath;
    }

    /// <summary>The walk <see cref="SkinnedMannequin.Pose"/> draws, keyed as a clip.</summary>
    private static float Walk(float phase, Span<Quaternion> r)
    {
        var swing = MathF.Sin(phase);
        r[Hips] = Quaternion.CreateFromYawPitchRoll(0.08f * swing, 0f, 0.04f * swing);
        r[Chest] = Quaternion.CreateFromYawPitchRoll(-0.12f * swing, 0.03f, 0f);
        r[Head] = Quaternion.CreateFromYawPitchRoll(0.06f * swing, 0.05f * MathF.Sin(phase * 2f), 0f);
        r[LeftUpperArm] = AboutX(-0.45f * swing);
        r[LeftForearm] = AboutX(-0.3f - MathF.Max(0f, -0.5f * swing));
        r[RightUpperArm] = AboutX(0.45f * swing);
        r[RightForearm] = AboutX(-0.3f - MathF.Max(0f, 0.5f * swing));
        r[LeftThigh] = AboutX(0.55f * swing);
        r[LeftShin] = AboutX(MathF.Max(0f, -0.9f * MathF.Sin(phase - 0.6f)));
        r[RightThigh] = AboutX(-0.55f * swing);
        r[RightShin] = AboutX(MathF.Max(0f, 0.9f * MathF.Sin(phase - 0.6f)));
        return 0.04f * MathF.Abs(MathF.Sin(phase));
    }

    /// <summary>The walk's cycle with a longer stride, bent elbows and a forward lean; its footfalls sit at the same phases, so a synchronized blend keeps the feet planted.</summary>
    private static float Run(float phase, Span<Quaternion> r)
    {
        var swing = MathF.Sin(phase);
        r[Hips] = Quaternion.CreateFromYawPitchRoll(0.12f * swing, 0.12f, 0.05f * swing);
        r[Chest] = Quaternion.CreateFromYawPitchRoll(-0.22f * swing, 0.18f, 0f);
        r[Head] = Quaternion.CreateFromYawPitchRoll(0.08f * swing, -0.2f + 0.06f * MathF.Sin(phase * 2f), 0f);
        r[LeftUpperArm] = AboutX(-0.9f * swing);
        r[LeftForearm] = AboutX(-1.3f);
        r[RightUpperArm] = AboutX(0.9f * swing);
        r[RightForearm] = AboutX(-1.3f);
        r[LeftThigh] = AboutX(0.95f * swing);
        r[LeftShin] = AboutX(0.2f + MathF.Max(0f, -1.7f * MathF.Sin(phase - 0.5f)));
        r[RightThigh] = AboutX(-0.95f * swing);
        r[RightShin] = AboutX(0.2f + MathF.Max(0f, 1.7f * MathF.Sin(phase - 0.5f)));
        return 0.1f * MathF.Abs(MathF.Sin(phase));
    }

    /// <summary>The right arm raised and waving, the head turned toward it; below the chest it holds rest, which the mask hides.</summary>
    private static float Wave(float phase, Span<Quaternion> r)
    {
        var wave = MathF.Sin(phase);
        r[Chest] = Quaternion.CreateFromYawPitchRoll(0.15f, 0f, -0.06f);
        r[Head] = Quaternion.CreateFromYawPitchRoll(-0.35f, 0.05f, 0f);
        r[RightUpperArm] = AboutZ(-2.45f + 0.18f * wave);
        r[RightForearm] = AboutZ(-0.5f * wave);
        r[LeftUpperArm] = AboutZ(0.1f);
        r[LeftForearm] = AboutX(-0.2f);
        return 0f;
    }

    /// <summary>A slow sideways sway of the chest and head; relative to rest it becomes a delta that tilts any pose.</summary>
    private static float Lean(float phase, Span<Quaternion> r)
    {
        var sway = MathF.Sin(phase);
        r[Chest] = Quaternion.CreateFromYawPitchRoll(0f, 0f, 0.3f * sway);
        r[Head] = Quaternion.CreateFromYawPitchRoll(0.25f * sway, 0f, -0.2f * sway);
        return 0f;
    }

    private void Remember(PlaybackHandle playback, string name)
    {
        Array.Copy(_recent, 0, _recent, 1, _recent.Length - 1);
        _recent[0] = (playback, name);
        _recentCount = Math.Min(_recentCount + 1, _recent.Length);
    }

    private void CrossFadeTo(int gait)
    {
        var (clip, name) = gait switch { 0 => (_idle, "idle"), 1 => (_walk, "walk"), _ => (_run, "run") };
        Remember(_characters[1].Player.Play(clip, _fadeSeconds), name);
        _switches++;
    }

    /// <summary>Drives each character's controls, advances and evaluates its player, and uploads its palette.</summary>
    private void Update(float seconds)
    {
        if (_paused) seconds = 0f;
        _time += seconds;

        if (_autoMix)
        {
            // Three phase-shifted waves: the weights never all reach zero, and at most times all three are nonzero.
            for (var gait = 0; gait < _gaitWeights.Length; gait++) _gaitWeights[gait] = 0.5f + 0.5f * MathF.Sin(_time * 0.5f + gait * MathF.Tau / 3f);
        }

        var mix = _characters[0].Player;
        for (var gait = 0; gait < _gaits.Length; gait++) mix.SetWeight(_gaits[gait], _gaitWeights[gait]);

        if (_autoSwitch && seconds > 0f)
        {
            _untilSwitch -= seconds;
            if (_untilSwitch <= 0f)
            {
                _untilSwitch += _switchInterval;
                CrossFadeTo(_switches % 3 == 2 ? 0 : _switches % 3 + 1);
            }
        }

        if (_autoOverride) _overrideWeight = 0.5f - 0.5f * MathF.Cos(_time * 0.9f);
        _characters[2].Player.SetWeight(_upper, _overrideWeight);
        if (_autoAdditive) _additiveWeight = 0.5f - 0.5f * MathF.Cos(_time * 0.7f);
        _characters[3].Player.SetWeight(_additive, _additiveWeight);

        foreach (var character in _characters)
        {
            var player = character.Player;
            player.Advance(seconds);
            player.Evaluate();
            var models = player.ModelMatrices;
            SkinningPalette.Compute(models, _paletteJoints, _inverseBinds, -1, _palette);
            _pbr.SetJointPalette(character.Offset, _palette);
            if (seconds <= 0f) continue;
            for (var joint = 0; joint < models.Length; joint++)
            {
                var position = models[joint].Translation;
                if (character.HasPrevious) character.MaxStep = MathF.Max(character.MaxStep, Vector3.Distance(position, character.Previous[joint]));
                character.Previous[joint] = position;
            }

            character.HasPrevious = true;
        }

        _scene.ElapsedSeconds = _time;
    }

    private void Render(float seconds)
    {
        Update(seconds);
        // Aimed left of the row's centre so the characters clear the control panel on the left.
        var target = new Vector3(-2f, 1.1f, 0f);
        var eye = target + new Vector3(
            _distance * MathF.Cos(_pitch) * MathF.Sin(_yaw),
            _distance * MathF.Sin(_pitch),
            _distance * MathF.Cos(_pitch) * MathF.Cos(_yaw));
        _scene.Camera = new PbrCamera
        {
            View = PbrMath.LookAt(eye, target, Vector3.UnitY),
            Projection = PbrMath.Perspective(MathF.PI / 3.4f, _width / (float)_height, 0.05f, 100f),
            Position = eye,
        };
        _pbr.RenderFrame(_scene);
    }

    private void Resize(uint width, uint height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _pbr.Resize(_width, _height);
    }

    private void DrawPanel()
    {
        ImGuiApi.SetNextWindowPos(new Vector2(12, 12), ImGuiCond.FirstUseEver);
        ImGuiApi.SetNextWindowSize(new Vector2(390, 0), ImGuiCond.FirstUseEver);
        ImGuiApi.Begin("Animation mixer");
        ImGuiText.Show($"{ImGuiApi.GetIO().Framerate:F0} FPS | drag outside the panel to orbit");
        ImGuiApi.Checkbox("Pause", ref _paused);

        ImGuiApi.Separator();
        ImGuiText.Show(_characters[0].Title);
        ImGuiApi.Checkbox("Animate weights", ref _autoMix);
        string[] gaits = ["idle", "walk", "run"];
        for (var gait = 0; gait < gaits.Length; gait++)
        {
            if (ImGuiApi.SliderFloat(gaits[gait], ref _gaitWeights[gait], 0f, 1f)) _autoMix = false;
        }

        ImGuiText.Show($"sync phase {_characters[0].Player.GetState(_locomotion).Phase:F2}");

        ImGuiApi.Separator();
        ImGuiText.Show(_characters[1].Title);
        ImGuiApi.Checkbox("Switch every", ref _autoSwitch);
        ImGuiApi.SameLine();
        ImGuiApi.SliderFloat("s##interval", ref _switchInterval, 0.05f, 2f);
        ImGuiApi.SliderFloat("fade (s)", ref _fadeSeconds, 0f, 2f);
        if (ImGuiApi.Button("Idle")) CrossFadeTo(0);
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Walk")) CrossFadeTo(1);
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Run")) CrossFadeTo(2);
        var player = _characters[1].Player;
        for (var i = 0; i < _recentCount; i++)
        {
            var (playback, name) = _recent[i];
            if (!player.Contains(playback)) continue;
            var state = player.GetState(playback);
            ImGuiText.Show($"  {name,-4}  weight {state.Weight:F2}  time {state.NormalizedTime:F2}{(state.IsFadingOut ? "  fading out" : "")}");
        }

        ImGuiApi.Separator();
        ImGuiText.Show(_characters[2].Title);
        if (ImGuiApi.SliderFloat("override weight", ref _overrideWeight, 0f, 1f)) _autoOverride = false;
        ImGuiApi.Checkbox("Animate override", ref _autoOverride);

        ImGuiApi.Separator();
        ImGuiText.Show(_characters[3].Title);
        if (ImGuiApi.SliderFloat("lean strength", ref _additiveWeight, 0f, 1f)) _autoAdditive = false;
        ImGuiApi.Checkbox("Animate lean", ref _autoAdditive);
        ImGuiApi.End();
    }

    private void Report(int frame)
    {
        var gaitState = _characters[0].Player;
        Console.WriteLine(
            $"[mixer] frame {frame}: mix idle/walk/run {_gaitWeights[0]:F2}/{_gaitWeights[1]:F2}/{_gaitWeights[2]:F2} phase {gaitState.GetState(_locomotion).Phase:F2}; " +
            $"cross-fades {_switches} started, {_characters[1].Player.Playbacks.Length} playing; override {_overrideWeight:F2}; lean {_additiveWeight:F2}");
    }

    public static int Run(string[] args, ILoggerFactory logs)
    {
        string? Value(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var headless = args.Contains("--headless");
        var frames = 0;
        if (headless && (!int.TryParse(Value("--headless"), out frames) || frames < 1)) throw new ArgumentException("--headless needs a positive frame count.");
        uint width = 1280, height = 720;
        using var platform = new SdlWindowPlatform();
        using var window = headless ? null : platform.CreateWindow(new WindowOptions("Paradise animation mixer", width, height));
        width = window?.Width ?? width;
        height = window?.Height ?? height;
        using var backend = headless ? WebGpuRenderer.CreateHeadless(width, height, logs.CreateLogger("WebGPU"))
            : new WebGpuRenderer(window!.CreateSurface(), logger: logs.CreateLogger("WebGPU"));
        using var demo = new AnimationMixerDemo(backend, width, height, logs.CreateLogger("Pbr"));
        using var ui = new ImGuiUiCore(width, height);
        ui.DisableIniFile();
        using var overlay = new ImGuiWebGpuRenderer(backend.NativeDevice, backend.NativeColorFormat);
        ui.AddDraw(demo.DrawPanel);
        var pending = new List<ImGuiTextureOp>();
        ImGuiDrawSnapshot? snapshot = null;
        demo.Renderer.Pipeline.Add(new MixerOverlay(new WebGpuHostPass((encoder, view) =>
        {
            overlay.ApplyTextureOps(pending);
            if (snapshot is not null) overlay.Render(encoder, view, window?.Width ?? width, window?.Height ?? height, snapshot);
        })));
        if (window is not null)
        {
            window.Resized += (w, h) =>
            {
                backend.Resize(w, h);
                demo.Resize(w, h);
            };
        }

        var clock = Stopwatch.StartNew();
        var last = 0.0;
        var pointer = Vector2.Zero;
        var dragging = false;
        var count = headless ? frames : int.MaxValue;
        for (var frame = 0; frame < count && window?.CloseRequested != true; frame++)
        {
            platform.Pump();
            if (window is not null)
            {
                while (window.TryReadEvent(out var input))
                {
                    var e = input.Event;
                    var consumed = ui.Input.Handle(e);
                    if (e.Kind == WindowEventKind.Button && e.Source == EventSource.Mouse && e.Code == (byte)PointerButton.Left)
                    {
                        dragging = e.Pressed && !consumed;
                        if (dragging) pointer = new Vector2(e.X, e.Y);
                    }

                    if (e.Kind == WindowEventKind.PointerMove)
                    {
                        var next = new Vector2(e.X, e.Y);
                        if (dragging && !consumed)
                        {
                            demo._yaw -= (next.X - pointer.X) * 0.01f;
                            demo._pitch = Math.Clamp(demo._pitch + (next.Y - pointer.Y) * 0.01f, 0.02f, 1.2f);
                        }

                        pointer = next;
                    }

                    if (e.Kind == WindowEventKind.Scroll && !consumed) demo._distance = Math.Clamp(demo._distance * (1f - e.Y * 0.1f), 3f, 30f);
                }
            }

            // Headless frames are a fixed 60 Hz so a run is repeatable; a window follows the wall clock.
            var now = headless ? frame / 60.0 : clock.Elapsed.TotalSeconds;
            var seconds = (float)Math.Min(now - last, 0.1);
            last = now;
            ui.Input.Tick(now);
            snapshot = ui.AcquireSnapshotForRender(pending, out _);
            demo.Render(seconds);
            if (headless && (frame + 1) % 60 == 0) demo.Report(frame + 1);
        }

        foreach (var character in demo._characters)
        {
            Console.WriteLine($"[mixer] {character.Title}: largest joint step {character.MaxStep * 100f:F1} cm/frame, {character.Player.Playbacks.Length} playbacks, {character.Player.Layers.Length} layers.");
        }

        if (Value("--screenshot") is { } path)
        {
            var pixels = backend.ReadbackColor(out var w, out var h);
            using var file = File.Create(path);
            PngWriter.Write(file, new ColorReadback(pixels, w, h), backend.ColorFormat);
            Console.WriteLine($"Screenshot written to {path}.");
        }

        Console.WriteLine("Animation mixer completed.");
        return 0;
    }

    public void Dispose()
    {
        foreach (var character in _characters) character?.Player.Dispose();
        _lean.Dispose();
        _wave.Dispose();
        _run.Dispose();
        _walk.Dispose();
        _idle.Dispose();
        _skeleton.Dispose();
        _pbr.Dispose();
    }
}
