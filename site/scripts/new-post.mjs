// Scaffolds one article in every language the site speaks, with frontmatter the collection accepts.
// Run with: node scripts/new-post.mjs --slug my-article --section tools --title "..." --description "..."
//
// The point is that the mechanics are not a judgement call: the slug, the section, the date and the pair
// of files are settled by the script, so what is left to write is the prose.
import { mkdir, writeFile, access } from 'node:fs/promises';
import { dirname, join } from 'node:path';

const SECTIONS = ['basics', 'save-tokens', 'tools', 'design', 'install', 'news'];
const LANGS = ['en', 'es'];
const ROOT = join(dirname(new URL(import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, '$1'), '..');

function args() {
  const out = {};
  for (let i = 2; i < process.argv.length; i += 2) {
    const key = process.argv[i].replace(/^--/, '');
    out[key] = process.argv[i + 1];
  }
  return out;
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function skeleton({ title, description, date, section, lang }) {
  const heading = lang === 'es' ? 'Qué es' : 'What it is';
  const why = lang === 'es' ? 'Por qué importa' : 'Why it matters';
  const verdict = lang === 'es' ? 'Cuándo vale la pena' : 'When it is worth it';
  const checked = lang === 'es'
    ? 'Comprobado el ' + date + ' sobre la versión en vivo.'
    : 'Checked on ' + date + ' against the live version.';
  return `---
title: "${title}"
description: "${description}"
date: ${date}
section: ${section}
sources:
  - title: "REPLACE with what you actually read"
    href: "https://example.com"
draft: true
---

## ${heading}

${checked}

## ${why}

## ${verdict}
`;
}

async function exists(path) {
  try {
    await access(path);
    return true;
  } catch {
    return false;
  }
}

const { slug, section, title, description, date = today() } = args();
const problems = [];
if (!slug || !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(slug)) problems.push('--slug must be lowercase words joined by hyphens');
if (!SECTIONS.includes(section)) problems.push(`--section must be one of ${SECTIONS.join(', ')}`);
if (!title) problems.push('--title is required');
if (!description) problems.push('--description is required');
if (problems.length) {
  console.error('Cannot scaffold:\n  ' + problems.join('\n  '));
  process.exit(1);
}

const written = [];
for (const lang of LANGS) {
  const path = join(ROOT, 'src', 'content', 'blog', lang, `${slug}.mdx`);
  if (await exists(path)) {
    console.error(`${path} already exists; refusing to overwrite`);
    process.exit(1);
  }
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, skeleton({ title, description, date, section, lang }), 'utf8');
  written.push(path);
}

console.log('Scaffolded, both marked draft: true\n  ' + written.join('\n  '));
console.log('\nNext: write the prose, replace the sources, drop draft, then `node scripts/check-posts.mjs`.');
