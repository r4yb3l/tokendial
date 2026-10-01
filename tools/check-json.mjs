#!/usr/bin/env node
// Every shared JSON file parses and no object in it names a key twice.
//
// JSON.parse, ajv and System.Text.Json all accept a duplicate key and keep the last occurrence; Foundation
// keeps the first. A spec once carried "requires" twice, so Windows read one list and macOS another, and
// every validator in the pipeline called the file valid. This walks the text itself so a repeat is an error.
//
// Usage: node tools/check-json.mjs [dir ...]   (defaults to docs and site/src/i18n)
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const roots = process.argv.length > 2 ? process.argv.slice(2) : ['docs', 'site/src/i18n'];

function* files(path) {
  if (statSync(path).isFile()) {
    if (path.endsWith('.json')) yield path;
    return;
  }
  for (const entry of readdirSync(path)) yield* files(join(path, entry));
}

/** Returns the duplicate keys of every object in `text`, with their line numbers. */
function duplicates(text) {
  const found = [];
  const stack = [];
  let line = 1;
  let expectKey = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '\n') line++;
    else if (c === '{') {
      stack.push(new Set());
      expectKey = true;
    } else if (c === '[') {
      stack.push(null);
      expectKey = false;
    } else if (c === '}' || c === ']') {
      stack.pop();
      expectKey = false;
    } else if (c === ',') {
      expectKey = stack.at(-1) instanceof Set;
    } else if (c === '"') {
      let j = i + 1;
      while (text[j] !== '"') j += text[j] === '\\' ? 2 : 1;
      if (expectKey) {
        const key = JSON.parse(text.slice(i, j + 1));
        const keys = stack.at(-1);
        if (keys.has(key)) found.push(`line ${line}: "${key}"`);
        keys.add(key);
        expectKey = false;
      }
      i = j;
    }
  }
  return found;
}

const problems = [];
let count = 0;
for (const root of roots) {
  for (const file of files(root)) {
    count++;
    const text = readFileSync(file, 'utf8');
    const where = relative(process.cwd(), file);
    try {
      JSON.parse(text);
    } catch (error) {
      problems.push(`${where}: ${error.message}`);
      continue;
    }
    for (const repeat of duplicates(text)) problems.push(`${where}: duplicate key at ${repeat}`);
  }
}

if (problems.length > 0) {
  for (const problem of problems) console.error(problem);
  console.error(`\n${problems.length} problem(s) in ${count} JSON files.`);
  process.exit(1);
}
console.log(`${count} JSON files parse, and no object repeats a key.`);
