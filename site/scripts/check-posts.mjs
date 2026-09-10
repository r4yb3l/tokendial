// Checks every article against the house rules, so a post written by anyone - a person, an agent, a
// future model - fails here rather than in production. Run with: node scripts/check-posts.mjs
//
// Astro's own build already validates the frontmatter schema. What it cannot see is the editorial half:
// whether the pair of languages exists, whether sources were left as placeholders, whether the article
// smuggles in fonts the content security policy will refuse, or whether the Spanish drifts into another
// register than the rest of the blog.
import { readdir, readFile } from 'node:fs/promises';
import { join, dirname } from 'node:path';

const ROOT = join(dirname(new URL(import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, '$1'), '..');
const BLOG = join(ROOT, 'src', 'content', 'blog');
const LANGS = ['en', 'es'];
const SECTIONS = ['basics', 'save-tokens', 'tools', 'design', 'install', 'news'];

/** The site serves its own fonts and styles: style-src and font-src are 'self'. Anything fetched from
 *  another host is not a slow load, it is a blocked one, and the article silently loses its typography. */
const FORBIDDEN = [
  [/https?:\/\/fonts\.googleapis\.com/, 'links a Google Fonts stylesheet; the CSP blocks style-src from other hosts'],
  [/https?:\/\/fonts\.gstatic\.com/, 'links Google font files; the CSP blocks font-src from other hosts'],
  [/<link\b[^>]*rel=["']stylesheet/i, 'brings its own stylesheet; the article should use the site typography'],
  [/<style[\s>]/i, 'carries a <style> block; the article should use the site typography'],
  [/<script[\s>]/i, 'carries a <script> block'],
  [/<!doctype/i, 'is a whole HTML document rather than an article body']
];

/** The blog is written in tuteo. Voseo is not wrong, it is just a different voice, and one blog should
 *  have one. The accent carries the whole distinction - "usas" is tuteo, "usás" is not - so these
 *  patterns must never make the vowel optional, and the match must be case sensitive for the same reason. */
const VOSEO = /\b(venís|podés|apretás|decís|tenés|sabés|querés|usás|mirá|escribís|leés|hacés|sos)\b/g;

function frontmatter(text) {
  const match = text.match(/^---\n([\s\S]*?)\n---/);
  if (!match) return null;
  const out = {};
  for (const line of match[1].split('\n')) {
    const pair = line.match(/^([a-zA-Z]+):\s*(.*)$/);
    if (pair) out[pair[1]] = pair[2].trim();
  }
  out.__raw = match[1];
  return out;
}

const problems = [];
const bySlug = new Map();

for (const lang of LANGS) {
  let files = [];
  try {
    files = (await readdir(join(BLOG, lang))).filter((f) => f.endsWith('.mdx'));
  } catch {
    problems.push(`${lang}: no such language directory`);
    continue;
  }
  for (const file of files) {
    const slug = file.replace(/\.mdx$/, '');
    const where = `${lang}/${slug}`;
    const text = await readFile(join(BLOG, lang, file), 'utf8');
    const front = frontmatter(text);
    bySlug.set(slug, (bySlug.get(slug) ?? new Set()).add(lang));

    if (!front) {
      problems.push(`${where}: no frontmatter`);
      continue;
    }
    if (!front.title || !front.description) problems.push(`${where}: title and description are required`);
    if (!SECTIONS.includes(front.section)) problems.push(`${where}: section "${front.section}" is not one of ${SECTIONS.join(', ')}`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(front.date ?? '')) problems.push(`${where}: date must be YYYY-MM-DD`);
    if (!/sources:/.test(front.__raw)) problems.push(`${where}: no sources; say what was read`);
    if (/example\.com|REPLACE/.test(front.__raw)) problems.push(`${where}: sources still hold the scaffold placeholder`);
    if (front.draft === 'true') problems.push(`${where}: still a draft`);

    const body = text.slice(text.indexOf('\n---', 3) + 4);
    for (const [pattern, why] of FORBIDDEN) {
      if (pattern.test(body)) problems.push(`${where}: ${why}`);
    }
    // Install guides are reference pages and are meant to be short; an essay that short is unfinished.
    const floor = front.section === 'install' ? 250 : 400;
    const words = body.replace(/```[\s\S]*?```/g, ' ').split(/\s+/).filter(Boolean).length;
    if (words < floor) problems.push(`${where}: ${words} words is thin for a ${front.section} article`);
    if (lang === 'es') {
      const voseo = [...new Set((body.match(VOSEO) ?? []).map((w) => w.toLowerCase()))];
      if (voseo.length) problems.push(`${where}: voseo (${voseo.slice(0, 4).join(', ')}); the blog is written in tuteo`);
    }
  }
}

for (const [slug, langs] of bySlug) {
  const missing = LANGS.filter((l) => !langs.has(l));
  if (missing.length) problems.push(`${slug}: exists only in ${[...langs].join(', ')}; missing ${missing.join(', ')}`);
}

if (problems.length) {
  console.error(`${problems.length} problem(s):`);
  for (const p of problems) console.error('  ' + p);
  process.exit(1);
}
console.log(`${bySlug.size} articles, ${LANGS.length} languages each, all clean.`);
