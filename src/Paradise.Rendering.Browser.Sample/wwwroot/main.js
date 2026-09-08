// Bootstrap: start the .NET runtime, hand the managed side the scene name and canvas size, then
// pump requestAnimationFrame. The managed entry point is never run - the app is driven purely
// through its [JSExport] surface (see Program.cs).
import { dotnet } from './_framework/dotnet.js';

const status = document.getElementById('status');
const canvas = document.getElementById('gpu-canvas');

try {
    if (!navigator.gpu) throw new Error('this browser exposes no navigator.gpu (WebGPU required)');

    const { getAssemblyExports, getConfig } = await dotnet.create();
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    const program = exports.Paradise.Rendering.Browser.Sample.Program;

    const query = new URLSearchParams(location.search);
    const scene = query.get('scene') || 'showcase';
    // Canvas size and instance count are query-driven so the same page serves as the perf harness.
    canvas.width = Number(query.get('w')) || canvas.width;
    canvas.height = Number(query.get('h')) || canvas.height;
    document.title = `Paradise browser renderer - ${scene}`;
    const hostModuleUrl = new URL('paradise-sample-host.js', document.baseURI).href;
    // ?log=debug turns the engine's own diagnostics up — the PBR cluster dump among them. Passed
    // as an argument because wasm has no environment to read one from.
    await program.InitAsync(
        scene, hostModuleUrl, canvas.width, canvas.height,
        Number(query.get('boxes')) || 0, query.get('log') || '');

    if (scene === 'showcase') {
        document.getElementById('controls').hidden = false;
        document.getElementById('passes-panel').hidden = false;
        const features = [];
        const updateFeatures = () => {
            for (const { input, index } of features) input.checked = program.FeatureEnabled(index);
            const visible = features.filter(f => ['rendering.scene', 'rendering.composite', 'rendering.presentation'].includes(f.name))
                .every(f => f.input.checked);
            canvas.style.visibility = visible ? 'visible' : 'hidden';
        };
        for (let index = 0; index < program.FeatureCount(); index++) {
            const name = program.FeatureName(index);
            const label = document.createElement('label');
            label.title = program.FeatureSummary(index);
            const input = document.createElement('input');
            input.type = 'checkbox';
            input.addEventListener('change', () => { program.SetFeature(index, input.checked); updateFeatures(); });
            label.append(input, document.createTextNode(name.replace('rendering.', '')));
            document.getElementById('features').append(label);
            features.push({ input, index, name });
        }
        updateFeatures();
        document.getElementById('restore').onclick = () => { program.RestoreFeatures(false); updateFeatures(); };
        document.getElementById('all-off').onclick = () => { program.RestoreFeatures(true); updateFeatures(); };
        for (const input of document.querySelectorAll('[data-option]')) {
            input.addEventListener('input', () => {
                const value = input.type === 'checkbox' ? Number(input.checked) : Number(input.value);
                program.SetShowcaseOption(input.dataset.option, value);
                if (input.nextElementSibling?.tagName === 'OUTPUT') input.nextElementSibling.value = input.value;
            });
        }
        let pointer;
        canvas.addEventListener('pointerdown', e => { if (e.button !== 0) return; pointer = [e.clientX, e.clientY]; canvas.setPointerCapture(e.pointerId); });
        canvas.addEventListener('pointermove', e => {
            if (!pointer) return;
            program.Orbit(e.clientX - pointer[0], e.clientY - pointer[1]);
            pointer = [e.clientX, e.clientY];
        });
        canvas.addEventListener('pointerup', () => { pointer = null; });
        canvas.addEventListener('pointercancel', () => { pointer = null; });
        canvas.addEventListener('wheel', e => { e.preventDefault(); program.Zoom(-e.deltaY / 100); }, { passive: false });
        if (!query.has('w') && !query.has('h')) {
            new ResizeObserver(() => {
                const width = Math.max(320, Math.round(canvas.clientWidth));
                program.Resize(width, Math.round(width * 800 / 1280));
            }).observe(canvas);
        }
        setInterval(() => {
            document.getElementById('draws').textContent = program.ShowcaseStats();
            if (document.getElementById('passes-panel').open)
                document.getElementById('passes').textContent = program.ShowcasePasses();
        }, 1000);
    }

    const frame = () => {
        program.OnAnimationFrame();
        requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
} catch (error) {
    // The managed side writes its own SAMPLE-FAIL when it can; this covers everything before the
    // runtime is up (no WebGPU, a failed download, a broken import).
    status.textContent = `SAMPLE-FAIL: ${error && error.message ? error.message : error}`;
    console.error(error);
}
