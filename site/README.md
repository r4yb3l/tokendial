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
- `vercel.json` — what production serves: security headers, immutable caching for `/_astro/`, and the
  `cleanUrls`/`trailingSlash` pair that makes `/es` answer for `es.html`. `public/_headers` carries the same headers
  for any other static host and must be kept identical.

## Deploying

The site is a Vercel project with `site` as its root directory, served at https://tokendial.vercel.app; every push
to `main` that touches `site/` rebuilds it. No domain is bought yet, so `astro.config.mjs` takes the canonical
host from `VERCEL_PROJECT_PRODUCTION_URL` rather than naming `tokendial.app`.

The content security policy says `script-src 'self'`: a script must be a file. Write processed `<script>` tags, not
`is:inline` ones, and run `npm run check-csp` after a build — an inline script works on the dev server and is
refused in production.

## Keeping the marks in sync

```
cp ../docs/design/marks/normalized/*.svg ../docs/design/marks/normalized/opencode.png src/assets/marks/
cp ../docs/design/marks/normalized/marks.json src/data/marks.json
```
