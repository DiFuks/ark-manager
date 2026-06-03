import { defineConfig } from 'astro/config';

import sitemap from '@astrojs/sitemap';

export default defineConfig({
  site: 'https://arkmanager.org',
  trailingSlash: 'ignore',

  // English at the root, Russian under /ru/. The default locale is not
  // prefixed (prefixDefaultLocale: false), so `/` stays English.
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'ru'],
    routing: { prefixDefaultLocale: false },
  },

  build: {
    assets: 'assets',
  },

  // Sitemap emits reciprocal <xhtml:link rel="alternate" hreflang> entries for
  // each localized page so Google/Yandex see them as one document, two languages.
  integrations: [
    sitemap({
      i18n: {
        defaultLocale: 'en',
        locales: { en: 'en', ru: 'ru' },
      },
    }),
  ],
});
