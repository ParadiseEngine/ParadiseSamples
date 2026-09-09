# Paradise Engine samples

Interactive browser demos: **[paradiseengine.dev/samples](https://paradiseengine.dev/samples/)**.

This repository owns the engine's five example applications. A pinned engine submodule keeps
sample code, shaders and source generators compatible without requiring an unpublished NuGet release.

```sh
git clone --recurse-submodules https://github.com/ParadiseEngine/ParadiseSamples.git
cd ParadiseSamples
dotnet workload install wasm-tools
dotnet build ParadiseSamples.slnx -c Release
dotnet run --project src/Paradise.Rendering.Sample -p:ParadiseProfiling=true -- --showcase --config engine.toml --bench
```

Use .NET SDK 10.0.400 or later (see `global.json`). The native renderer and ImGui samples need a
desktop graphics environment. The BT and ECS samples are console applications. The browser sample
provides the full renderer showcase plus lit cube, PBR/shadows and GPU compute scenes.
The showcase shares its scene, animation and glass shader with the native sample through `src/Shared`.
Browser controls replace the native ImGui overlay: orbit/zoom, feature switches, pause, shadow
settings, focus and exposure. The embedded `engine.toml` supplies the same starting preset.
Browser statistics report CPU submission time and pass names; native GPU timing readback is not
exposed by the browser backend.

## Build the website

```sh
npm ci
dotnet publish src/Paradise.Rendering.Browser.Sample -c Release -o artifacts/browser
npm run build:site
npx wrangler pages dev dist
```

The static gallery lives in `site/`; the published WebAssembly application lives under `rendering/`.
Links and runtime assets are relative so they work on Pages and beneath `/samples/`.

## Deployment

GitHub Actions builds all five samples, runs both console applications and a BT NativeAOT smoke
test, then publishes the browser application. Pull requests build without deployment credentials.
Successful `main` builds deploy to the `paradise-samples` Cloudflare Pages project, followed by
the `paradise-samples-route` Worker on `paradiseengine.dev`. It redirects `/` to `/samples/`,
canonicalizes `/samples`, proxies `/samples/*` to Pages, and returns 404 for other paths.

### Automatic releases

Every push to `main` (including a merged sample PR or engine submodule update) runs
[Build, deploy and release samples](.github/workflows/samples.yml):

1. Build and validate the samples, then assemble the browser gallery.
2. Deploy that build to Cloudflare and verify the public gallery.
3. Publish a [GitHub release](https://github.com/ParadiseEngine/ParadiseSamples/releases)
   tagged `samples-<run number>` at the source commit, with `sample-site.tar.gz` containing
   the deployed gallery and WebAssembly assets.

Releases are created only after deployment succeeds. Pull requests only build and test.
Extract the archive and serve it over HTTP with WebAssembly MIME support to run it locally;
a WebGPU-capable browser is required. Native executables are not included in this archive.

To release manually, open **Actions → Build, deploy and release samples → Run workflow**
and select `main`. To retry a failed deployment or release, use **Re-run failed jobs** on
that run. Reruns reuse its release tag and replace the archive if the release already exists.
An engine release alone does not update samples: commit the tested `engine/` pointer to
`main` to publish a compatible sample build.

### Cloudflare configuration

The repository variable `CLOUDFLARE_ACCOUNT_ID` and the `production` environment secret
`CLOUDFLARE_API_TOKEN` are configured for this repository. The workflow uses GitHub's
built-in token for releases; only the release job receives `contents: write`.

For a fresh deployment, create the Pages project once using Wrangler:

```sh
npx wrangler login
npx wrangler pages project create paradise-samples --production-branch main
```

When setting up another repository, configure the GitHub repository variable `CLOUDFLARE_ACCOUNT_ID` and the production environment
secret `CLOUDFLARE_API_TOKEN`. The token needs Account / Cloudflare Pages / Edit,
Account / Workers Scripts / Edit, Zone / Workers Routes / Edit and Zone / Zone / Read,
restricted to the deployment account and `paradiseengine.dev` zone. Wrangler's local OAuth session
is for interactive deployment and is not a durable CI credential.

Wrangler's Worker custom domain provisions DNS and TLS for the previously unused apex hostname.
Pages custom domains bind hostnames, so the Worker supplies the requested subpath.
`wrangler.jsonc` contains the Pages origin and custom domain. If a main website is added later,
move the Worker to `/samples` and `/samples/*` routes after configuring that site's proxied DNS.

To update the engine, check out the intended commit in `engine/`, build and test, then commit
the submodule pointer together with any required sample changes.
