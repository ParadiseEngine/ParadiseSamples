# Renderer laboratory

Run from the repository root (requires .NET 10 and a WebGPU-capable GPU):

```sh
dotnet run --project src/Paradise.Rendering.Sample -- --showcase --config engine.toml
```

This procedural Cornell room needs no external assets. Drag outside the ImGui panel to
orbit, scroll outside it to zoom, and use **Pause animation** for comparisons. Scroll the
feature list independently of the settings below it. Hover a switch for its description.

Every `PbrFeatures.All` entry is listed dynamically and writes to the host's shared
`FeatureSwitches`. The root `engine.toml` supplies the balanced preset when passed with `--config`;
`PARADISE_FEATURES` and `--features` overrides
still determine initial switch values. Scene-level gates are enabled in this showcase;
scene-color capture defaults off and can be enabled to refract through the glass pane.
**Restore initial switches** restores that initial configuration. **All off** leaves the
ImGui overlay usable against a clear background, even with scene/composite/presentation off.

| What to compare | Where to look / controls |
| --- | --- |
| FXAA, TAA, motion vectors and motion blur | Moving cube, skinned cyan character and room edges; pause or orbit the camera. Motion vectors feed TAA and motion blur. |
| CSM/PSSM and local shadow atlas | Directional sun, moving point lamp and orange spotlight; adjust cascade count and split, select Poisson PCSS or hardware PCF. |
| Contact shadows, SSAO, ray-traced AO | Box/floor intersections and the character's feet. SSAO has a separate scene checkbox. |
| Frustum and GPU occlusion | Off-camera copies and objects behind the tall box; orbit and compare the frustum counter. |
| Instancing | Repeated brass columns share mesh/material. Disable GPU occlusion to expose opaque batching and watch saved draws. |
| Decals | Yellow floor and cyan wall arrows. |
| Fog / volumetrics | Local fog near the floor and scattering around the lamps. |
| Probe GI and Forward+ lights | Colored wall bounce and twenty colored local lights. |
| SSR and scene-color capture | Glossy floor beneath the character and tinted glass on the left. |
| Post processing | Emissive ceiling panel for bloom; focus and exposure sliders; switches for DOF, grading, distortion, chromatic aberration, vignette, grain and sharpening. |

Depth/normal prepass dependencies apply normally: switching it off also suppresses effects
that need it. Contact shadows, SSAO and decals shade inline and do not have separate pass
rows. Expand **Last frame passes** to inspect the actual graph. The sample uses a modest
512-probe/32-ray GI budget and four-ray AO, so noise can be visible.

For a reproducible GPU/input smoke check and screenshot:

```sh
dotnet run --project src/Paradise.Rendering.Sample -- --showcase --config engine.toml --headless 8 --sweep --screenshot /tmp/renderer-showcase.png
```

The sweep clicks the real ImGui **All off** and **Restore** buttons through window input,
asserts their switch results, resizes down and back, and disables/re-enables each of the
29 current feature switches across rendered frames. It prints observed passes and peak
culling/batching counters, restores initial switches, and captures the final image. This
checks integration and transitions; visual effect quality remains a manual comparison.
Omit `--sweep` for a fixed number of headless frames.

The profiling build displays milliseconds beside each feature with dedicated GPU passes.
Repeated passes (such as bloom mips) are summed; expand **Last frame passes** for the breakdown.
These timestamps can overlap on the GPU, so their sum is not exclusive feature cost. Decals,
contact shadows and SSAO say `shared: Main`; frustum culling and instancing say `CPU / draws`
because they do not have separate GPU passes. Disabled features say `off`, and graph-culled
features say `no pass`. Measurements refresh each frame without retaining disabled-pass values.

**Render + GPU wait** measures host rendering/submission through GPU timing readback; **CPU
submit** measures host rendering through submission before that wait (including any driver
stalls). Neither includes UI construction. Disable **Measure GPU timings** to avoid the
synchronous readback overhead. A non-profiling build or unsupported GPU shows timings as
unavailable; feature controls still work.

For a sustained run on another device:

```sh
dotnet run --project src/Paradise.Rendering.Sample -- --showcase --config engine.toml --headless 6000 --bench
```

After 60 warm-up frames, `--bench` reports average iteration time, process resident memory,
and managed memory every 300 frames. Add `--no-profile` to compare without synchronous GPU
timing readback; those iteration times measure CPU submission, not completed GPU frames.
Omit `--headless 6000` for an interactive run with the same reporting.

`ProbeGiFeature.Settings` owns GI tuning; `ProbeGiDebugFeature` owns marker size and
the `rendering.debug.giProbes` visibility switch. Expand **DDGI** for live probe visibility, spacing, ray count, update budget, hysteresis,
intensity and lookup biases. Green markers are active; red markers are inactive. Markers
respect scene depth and show relocated positions. Spacing is a minimum: the probe budget
can force a wider grid. An authored volume uses its own spacing and counts.

## Animation mixer

```sh
dotnet run --project src/Paradise.Rendering.Sample -- --animation-mixer
```

Four procedural mannequins, each driven by its own `Paradise.Animation` `AnimationPlayer`, with clips
keyed from formulas at startup so no rigged asset is needed:

1. **Synchronized mix** — idle, walk and run play at once at independent weights in one sync group,
   so their steps stay aligned while the weights move. Untick **Animate weights** and drag the sliders.
2. **Interrupted cross-fades** — a new `Play` every 0.3 s with 0.6 s fades, so fades are always
   interrupted; each continues from the pose it reached and outgoing playbacks are removed within one
   fade. The list shows every contributing playback's weight. The buttons cross-fade by hand.
3. **Upper-body override** — a wave on an override layer masked to the chest and everything below it,
   over the walk. Its layer weight moves between 0 and 1.
4. **Additive lean** — a sideways sway built as an additive clip against the rest pose, applied over
   the run at the strength its layer weight sets.

For a repeatable headless check (fixed 60 Hz steps, a status line every 60 frames, the largest
per-frame joint move per character, and a screenshot):

```sh
dotnet run --project src/Paradise.Rendering.Sample -- --animation-mixer --headless 210 --screenshot /tmp/animation-mixer.png
```

The sample uses the published engine version in `Directory.Build.props`.
