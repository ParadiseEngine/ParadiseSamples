# Paradise Engine samples

Interactive browser demos: **[paradiseengine.dev/samples](https://paradiseengine.dev/samples/)**.

This repository owns the engine's five example applications. All engine dependencies use published NuGet packages at the single
`ParadiseVersion` in `Directory.Build.props`. Runtime packages include their source generators;
the PBR package supplies shader includes and Slang build tooling. No engine checkout is required.

```sh
git clone https://github.com/ParadiseEngine/ParadiseSamples.git
cd ParadiseSamples
dotnet workload install wasm-tools
dotnet build ParadiseSamples.slnx -c Release
dotnet run --project src/Paradise.Rendering.Sample -- --showcase --config engine.toml --bench
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
Version tags deploy to the `paradise-samples` Cloudflare Pages project, followed by the
`paradise-samples-route` Worker on `paradiseengine.dev`. It redirects `/` to `/samples/`,
canonicalizes `/samples`, proxies `/samples/*` to Pages, and returns 404 for other paths.

### Versioned releases

Sample versions match engine versions. To release a new version:

1. Publish the engine's NuGet packages first and wait for them to be available on nuget.org.
2. Set `ParadiseVersion` in `Directory.Build.props` to that version, update sample code as needed,
   and merge the validated changes into `main`.
3. Push the matching tag in this repository, for example:

   ```sh
   git switch main
   git pull --ff-only
   git tag v0.46.0
   git push origin v0.46.0
   ```

[Build, deploy and release samples](.github/workflows/samples.yml) verifies that the tag matches
`ParadiseVersion`, restores NuGet dependencies, builds and tests, then deploys to Cloudflare.
After public-gallery verification succeeds, it creates a GitHub release under the same tag
and attaches `sample-site.tar.gz` containing the deployed gallery and WebAssembly assets.
Extract and serve the archive over HTTP with WebAssembly MIME support in a WebGPU-capable browser.
Native executables are not included.

Pushes to `main` and pull requests build and test without releasing. A manual workflow run on
`main` also validates only; use a version tag to publish. To retry a failed tagged release,
use **Re-run failed jobs** on that run. Reruns reuse the tag and replace the archive if needed.
Publishing an engine tag does not automatically tag this repository.

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

Update `ParadiseVersion` and any required sample code together. Every Paradise package uses
that central version; local engine sources are never substituted for NuGet packages.
