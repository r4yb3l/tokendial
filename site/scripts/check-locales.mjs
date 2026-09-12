#!/usr/bin/env node
/**
 * Every locale says everything English says, in the same placeholders.
 *
 * The app side has enforced this since the beginning
 * (I18nTests.CatalogueCoversEveryEnglishKeyWithTheSamePlaceholders); the site did not, and a change that
 * edits five locale files at once is exactly when a key goes missing from one of them. A missing key is
 * silent: index.ts spreads English under en-GB and `t` falls back, so the page renders in the wrong
 * language rather than breaking.
 *
 * en-GB is an overlay by design - two keys where British English differs - so it is held to the opposite
 * rule: it may say less, but nothing it says may be absent from English.
 */
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'i18n');
const complete = ['es', 'fr', 'de', 'ar'];
const overlays = ['en-GB'];

const read = (code) => JSON.parse(readFileSync(join(root, `${code}.json`), 'utf8'));
const placeholders = (text) => [...String(text).matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

const english = read('en');
const problems = [];

for (const code of complete) {
  const dictionary = read(code);
  for (const [key, value] of Object.entries(english)) {
    if (!(key in dictionary)) {
      problems.push(`${code}: missing ${key}`);
      continue;
    }
    const want = placeholders(value);
    const got = placeholders(dictionary[key]);
    if (want.join() !== got.join()) {
      problems.push(`${code}: ${key} has {${got.join('} {')}} where English has {${want.join('} {')}}`);
    }
  }
  for (const key of Object.keys(dictionary)) {
    if (!(key in english)) problems.push(`${code}: ${key} is not in English`);
  }
}

for (const code of overlays) {
  for (const key of Object.keys(read(code))) {
    if (!(key in english)) problems.push(`${code}: ${key} is not in English`);
  }
}

if (problems.length > 0) {
  for (const problem of problems) console.error(problem);
  console.error(`\n${problems.length} problem(s) across ${complete.length + overlays.length} locales.`);
  process.exit(1);
}

console.log(`${Object.keys(english).length} keys, ${complete.length} full translations and ${overlays.length} overlay: all present, placeholders match.`);
