import { defineConfig } from 'astro/config';

import sitemap from '@astrojs/sitemap';

export default defineConfig({
  site: 'https://arkmanager.org',
  trailingSlash: 'ignore',

  build: {
    assets: 'assets',
  },

  integrations: [sitemap()],
});