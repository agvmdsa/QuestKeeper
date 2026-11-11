import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import type { Plugin } from 'vite';
import { defineConfig } from 'vitest/config';
import { mockApi } from './dev/mock-api';

/** Writes dist/sw.js from the template: the cache name and the shell list change whenever a hashed file name changes. */
function serviceWorker(): Plugin {
  return {
    name: 'qkp-service-worker',
    apply: 'build',
    enforce: 'post',
    generateBundle(_options, bundle) {
      const files = ['/', '/manifest.webmanifest', '/icons/icon-192.png', ...Object.keys(bundle).filter((name) => name !== 'index.html').map((name) => `/${name}`)];
      const version = createHash('sha256').update(JSON.stringify(files)).digest('hex').slice(0, 10);
      const template = readFileSync(new URL('./src/sw.template.js', import.meta.url), 'utf8');
      this.emitFile({
        type: 'asset',
        fileName: 'sw.js',
        source: template.replace("'__CACHE__'", JSON.stringify(`qkp-shell-${version}`)).replace('__SHELL__', JSON.stringify(files)),
      });
    },
  };
}

export default defineConfig({
  plugins: [serviceWorker(), mockApi()],
  build: {
    target: 'es2022',
    outDir: 'dist',
    emptyOutDir: true,
    cssCodeSplit: false,
    assetsInlineLimit: 0,
    modulePreload: { polyfill: false },
  },
  test: { include: ['test/**/*.test.ts'] },
});
