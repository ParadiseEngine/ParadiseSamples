import { cp, mkdir, readdir, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

const publish = resolve(process.argv[2] || 'artifacts/browser/wwwroot');
await stat(`${publish}/index.html`);
await mkdir('dist/rendering', { recursive: true });
await cp('site', 'dist', { recursive: true });
await cp(publish, 'dist/rendering', { recursive: true });
// Pages rejects individual files over 25 MiB; fail before deployment.
async function validate(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = `${directory}/${entry.name}`;
    if (entry.isDirectory()) await validate(path);
    else if ((await stat(path)).size > 25 * 1024 * 1024) throw new Error(`Pages file too large: ${path}`);
  }
}
await validate('dist');
console.log('Built dist: sample gallery and WebAssembly renderer.');
