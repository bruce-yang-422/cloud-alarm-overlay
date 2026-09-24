// Run npm ci then node build.cjs in this directory to refresh the committed assets.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const out = path.join(root, 'task-builder-assets');
fs.mkdirSync(out, { recursive: true });
for (const [source, target] of [
  ['lucide/dist/umd/lucide.min.js', 'lucide.min.js'],
  ['lucide/LICENSE', 'lucide-LICENSE.txt'],
  ['xlsx/dist/xlsx.full.min.js', 'xlsx.full.min.js'],
  ['xlsx/LICENSE', 'xlsx-LICENSE.txt'],
  ['tailwindcss/LICENSE', 'tailwind-LICENSE.txt'],
]) fs.copyFileSync(path.join(__dirname, 'node_modules', source), path.join(out, target));
for (const font of ['noto-sans-tc', 'plus-jakarta-sans', 'jetbrains-mono']) {
  const source = path.join(__dirname, 'node_modules/@fontsource-variable', font);
  const destination = path.join(out, font);
  fs.mkdirSync(path.join(destination, 'files'), { recursive: true });
  const css = fs.readFileSync(path.join(source, 'index.css'), 'utf8');
  fs.writeFileSync(path.join(destination, 'index.css'), css);
  fs.copyFileSync(path.join(source, 'LICENSE'), path.join(destination, 'LICENSE.txt'));
  for (const match of css.matchAll(/url\(\.\/files\/([^)]*)\)/g)) {
    fs.copyFileSync(path.join(source, 'files', match[1]), path.join(destination, 'files', match[1]));
  }
}
execFileSync(process.execPath, [path.join(__dirname, 'node_modules/tailwindcss/lib/cli.js'),
  '-c', path.join(__dirname, 'tailwind.config.cjs'), '-i', path.join(__dirname, 'input.css'),
  '-o', path.join(out, 'tailwind.css'), '--minify'], { cwd: root, stdio: 'inherit' });
