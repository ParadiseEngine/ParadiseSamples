import { test } from 'node:test';
import assert from 'node:assert/strict';
import worker from '../worker/index.mjs';

const env = { PAGES_ORIGIN: 'https://paradise-samples.pages.dev' };
test('unused apex redirects visitors to the gallery', async () => {
  const result = await worker.fetch(new Request('https://paradiseengine.dev/'), env);
  assert.equal(result.status, 302);
  assert.equal(result.headers.get('location'), 'https://paradiseengine.dev/samples/');
});
test('canonical trailing slash preserves scene query', async () => {
  const result = await worker.fetch(new Request('https://paradiseengine.dev/samples?scene=pbr'), env);
  assert.equal(result.status, 308);
  assert.equal(result.headers.get('location'), 'https://paradiseengine.dev/samples/?scene=pbr');
});
test('unrelated site paths are not proxied', async () => {
  assert.equal((await worker.fetch(new Request('https://paradiseengine.dev/samples-other'), env)).status, 404);
});
test('nested assets, query strings and origin redirects retain the public prefix', async (t) => {
  t.mock.method(globalThis, 'fetch', async (request) => {
    assert.equal(request.url, 'https://paradise-samples.pages.dev/rendering/_framework/test.wasm?v=1');
    return new Response(null, { status: 302, headers: { location: '/rendering/next', 'cache-control': 'public, max-age=3600' } });
  });
  const result = await worker.fetch(new Request('https://paradiseengine.dev/samples/rendering/_framework/test.wasm?v=1'), env);
  assert.equal(result.headers.get('location'), 'https://paradiseengine.dev/samples/rendering/next');
  assert.equal(result.headers.get('cache-control'), 'public, max-age=3600');
});
