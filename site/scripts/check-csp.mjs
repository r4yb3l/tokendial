#!/usr/bin/env node
/**
 * The built site runs under script-src 'self': a script must arrive as a file from this origin. An inline
 * <script> is not slow under that policy, it is refused, and the page silently loses whatever it did -
 * which is how the landing shipped with every dial, toggle and theme switch dead. Astro inlines small
 * processed scripts and `is:inline` writes them as they are, so this reads the build output, not the
 * source. Structured data is not executed and the policy does not apply to it.
 *
 * Run after `npm run build`.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';

const dist = join(dirname(fileURLToPath(import.meta.url)), '..', 'dist');

function* pages(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* pages(path);
    else if (entry.name.endsWith('.html')) yield path;
  }
}

const problems = [];
let count = 0;
for (const page of pages(dist)) {
  count++;
  const html = readFileSync(page, 'utf8');
  for (const [tag] of html.matchAll(/<script\b[^>]*>/gi)) {
    // Both exemptions match on a leading space: \b also matches after a hyphen, which would let
    // data-src and data-type carry an inline script past the check.
    if (/\ssrc\s*=/i.test(tag) || /\stype\s*=\s*["']?application\/ld\+json/i.test(tag)) continue;
    problems.push(`${relative(dist, page)}: inline ${tag}`);
  }
}

if (count === 0) {
  console.error('No pages in dist; run the build first.');
  process.exit(1);
}
if (problems.length > 0) {
  for (const problem of problems) console.error(problem);
  console.error(`\n${problems.length} inline script(s); the content security policy refuses every one.`);
  process.exit(1);
}
console.log(`${count} pages, no inline scripts.`);
