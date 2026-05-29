import { defineConfig } from 'astro/config';

export default defineConfig({
  site: 'https://difuks.github.io',
  base: '/ark-manager',
  trailingSlash: 'ignore',
  build: {
    assets: 'assets',
  },
});
