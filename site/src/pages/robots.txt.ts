import type { APIRoute } from 'astro';

/// The sitemap has to be named by the host the site is actually served from. With no domain bought yet
/// that is a *.vercel.app URL, so the line is built from Astro.site rather than written down.
export const GET: APIRoute = ({ site }) => {
  const base = site ?? new URL('https://tokendial.app');
  const body = `User-agent: *\nAllow: /\nSitemap: ${new URL('sitemap-index.xml', base).href}\n`;
  return new Response(body, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
};
