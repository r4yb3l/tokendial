import { defineConfig } from 'astro/config';
import tailwindcss from '@tailwindcss/vite';
import sitemap from '@astrojs/sitemap';
import mdx from '@astrojs/mdx';

export default defineConfig({
  site: 'https://tokendial.app',
  output: 'static',
  trailingSlash: 'never',
  build: { format: 'file' },
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'en-GB', 'es', 'fr', 'de', 'ar'],
    routing: { prefixDefaultLocale: false }
  },
  integrations: [mdx(), sitemap({ i18n: { defaultLocale: 'en', locales: { en: 'en', 'en-GB': 'en-GB', es: 'es', fr: 'fr', de: 'de', ar: 'ar' } } })],
  vite: { plugins: [tailwindcss()] }
});
