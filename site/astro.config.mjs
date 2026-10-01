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
  // Only the two index pages redirect. A dynamic source cannot be enumerated in a static build, so a
  // link to /blog/<slug> is a 404; check-posts refuses one in an article.
  redirects: {
    '/blog': '/guides',
    '/es/blog': '/es/guides'
  },
  trailingSlash: 'never',
  build: { format: 'file' },
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'en-GB', 'es', 'fr', 'de', 'ar'],
    routing: { prefixDefaultLocale: false }
  },
  integrations: [mdx(), sitemap({ i18n: { defaultLocale: 'en', locales: { en: 'en', 'en-GB': 'en-GB', es: 'es', fr: 'fr', de: 'de', ar: 'ar' } } })],
  // The content security policy says script-src 'self' and font-src 'self', so nothing may be inlined:
  // Astro inlines a processed script below this limit, and an inline script is refused, not slowed.
  vite: { plugins: [tailwindcss()], build: { assetsInlineLimit: 0 } }
});
