import { defineConfig } from 'astro/config';
import tailwindcss from '@tailwindcss/vite';
import sitemap from '@astrojs/sitemap';
import mdx from '@astrojs/mdx';

/// No domain is bought yet, so the canonical host is whatever Vercel serves the project from. Pointing
/// canonicals and the sitemap at a domain that does not resolve would tell search engines the pages live
/// somewhere they cannot be fetched. VERCEL_PROJECT_PRODUCTION_URL is the project's stable production
/// host, unlike VERCEL_URL, which changes with every deployment.
const production = process.env.VERCEL_PROJECT_PRODUCTION_URL;
const site = production ? `https://${production}` : 'https://tokendial.app';

export default defineConfig({
  site,
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
