# Paradise Engine samples

Interactive browser demos: **https://paradiseengine.dev/samples/**.

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
provides lit cube, PBR/shadows and GPU compute scenes; the full native showcase has not been ported.

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
Successful main builds deploy to the `paradise-samples` Cloudflare Pages project, followed by
the `paradise-samples-route` Worker that proxies only `paradiseengine.dev/samples` and `/samples/*`.

One-time setup using Wrangler:

```sh
npx wrangler login
npx wrangler pages project create paradise-samples --production-branch main
```

Configure the GitHub repository variable `CLOUDFLARE_ACCOUNT_ID` and the production environment
secret `CLOUDFLARE_API_TOKEN`. The token needs Account / Cloudflare Pages / Edit,
Account / Workers Scripts / Edit, Zone / Workers Routes / Edit and Zone / Zone / Read,
restricted to the deployment account and `paradiseengine.dev` zone. Wrangler's local OAuth session
is for interactive deployment and is not a durable CI credential.

The hostname must already be proxied through Cloudflare. Pages custom domains bind hostnames,
so the Worker supplies the requested subpath while the existing root site continues to serve
its other routes. `wrangler.jsonc` contains the origin and two route patterns.

To update the engine, check out the intended commit in `engine/`, build and test, then commit
the submodule pointer together with any required sample changes.
