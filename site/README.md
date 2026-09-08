# tokendial.app

The marketing site: one landing page in six languages, built with Astro and Tailwind, deployed as static files.

```
npm install
npm run dev       # http://localhost:4321
npm run build     # dist/
npm run preview
```

## Structure

- `src/i18n/*.json` — the copy, one file per language; `en-GB.json` only overrides what differs from `en.json`.
- `src/components/` — `Landing.astro` assembles the sections; `DockDemo.astro` draws the dock from the app's own
  tokens and marks (`src/data/marks.json`, copied from `docs/design/marks/normalized`).
- `src/pages/index.astro` is English at `/`; `src/pages/[lang]/index.astro` renders `/es`, `/fr`, `/de`, `/ar`, `/en-GB`.
  Arabic renders right-to-left; code and the dock stay left-to-right through the `.ltr` utility.
- `public/_headers` — security headers and immutable caching for Cloudflare Pages.

## Deploying to Cloudflare Pages

Create a Pages project connected to the GitHub repository with these settings:

| Setting | Value |
|---|---|
| Production branch | `main` |
| Root directory | `site` |
| Build command | `npm run build` |
| Build output directory | `dist` |
| Environment variable | `NODE_VERSION` = `24` |

Then add `tokendial.app` (and `www.tokendial.app` as a redirect) under Custom domains; Cloudflare issues the
certificate. Every push to `main` that touches `site/` rebuilds the site.

## Keeping the marks in sync

```
cp ../docs/design/marks/normalized/*.svg ../docs/design/marks/normalized/opencode.png src/assets/marks/
cp ../docs/design/marks/normalized/marks.json src/data/marks.json
```
