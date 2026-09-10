---
name: article
description: Write an article for the Tokendial blog. Use when asked to write, draft, review or translate a blog post, or to review an article written elsewhere before it goes in the repository.
---

# Writing an article for the Tokendial blog

The mechanics are a script; the judgement is here. Read this before writing, and run the script instead of
hand-rolling frontmatter.

## What the blog is

Nineteen articles in `site/src/content/blog/<lang>/<slug>.mdx`, the same slug in every language. It exists to
answer what someone hits while paying for a coding agent: what a token is, how a plan meters you, how to
install a tool, which tools cut the bill, and what a design server does. It is not a news feed and not a
changelog.

Sections, and the schema accepts no others: `basics`, `save-tokens`, `tools`, `design`, `install`, `news`.

## The rules a reader can feel

- **A claim carries its evidence.** Every article ends in a `sources` list of what was actually read. A
  review of a live site says the date it was checked. If a number is in the article, the number was
  measured or quoted, never estimated in prose.
- **Say the cost, not only the benefit.** The blog's credibility comes from writing down what a tool does
  badly, what it assumes about your stack, and when it is not worth it.
- **No hedging and no hype.** No "revolutionary", no "game changer", no "in today's fast-paced world".
  Start on the reader's problem in the first sentence; the eyebrow and the title already sold the click.
- **Spanish is tuteo** - `puedes`, `tienes`, `usas` - because all nineteen existing articles are. Voseo is
  not wrong, it is a different voice, and one blog has one voice. The checker enforces this.
- **Both languages or neither.** A slug that exists in one language breaks the language switcher. Write the
  other one, do not translate word for word: the same article, told naturally in that language.
- **The site's typography, always.** The article body is Markdown. It never brings a `<style>` block, a
  stylesheet link or a font from another host: `style-src` and `font-src` are `'self'`, so an external
  font is not a slow load, it is a blocked one, and the article silently loses its face.
- **Figures go through the component.** `import Figure from '../../../components/Figure.astro'` with the
  asset under `site/src/assets/blog/<slug>/`. An SVG diagram beats a screenshot: it survives a theme change
  and stays sharp.

## How to write one

1. `cd site && node scripts/new-post.mjs --slug <slug> --section <section> --title "..." --description "..."`
   It writes both languages, marks them `draft: true`, and refuses to overwrite anything.
2. Write the prose. Structure that has been working: what it is, why it matters to someone paying for an
   agent, how it works, what it costs you, and when it is worth it.
3. Replace the placeholder `sources` with what you read. Verify anything factual against the live thing,
   and say when you checked.
4. Drop `draft: true` from both files.
5. `node scripts/check-posts.mjs` must pass, then `npm run build`. The build validates the frontmatter
   schema; the checker validates the half a schema cannot see.

## Reviewing an article written elsewhere

Judge the prose and the container separately, and say which is which. A good article inside a standalone
HTML page is not publishable here, and saying "it does not serve" about the whole thing would be wrong.

The container checks, in order of what breaks quietly: does it bring its own fonts or styles that the CSP
will refuse; does it carry a second design system; is it one language only; are the sources structured;
does the section exist in the schema. Then run the prose through the rules above.

Extracting prose out of an HTML page is copy and paste into the scaffold. Keep the author's structure and
their headings; change the register only where the checker says the blog disagrees.
