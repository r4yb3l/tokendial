import { defineCollection, z } from 'astro:content';
import { glob } from 'astro/loaders';

/** Posts live in src/content/blog/<lang>/<slug>.mdx; the same slug in two languages is one article translated. */
const blog = defineCollection({
  loader: glob({ pattern: '**/*.mdx', base: './src/content/blog' }),
  schema: ({ image }) =>
    z.object({
      title: z.string(),
      description: z.string(),
      date: z.coerce.date(),
      updated: z.coerce.date().optional(),
      section: z.enum(['basics', 'save-tokens', 'tools', 'design', 'install', 'news']),
      provider: z.string().optional(),
      /** Shown on the card and used as the social image; the article itself decides where figures go. */
      cover: image().optional(),
      coverAlt: z.string().optional(),
      /** What was read to write the article; rendered as the closing "Sources" list. */
      sources: z.array(z.object({ title: z.string(), href: z.string().url() })).optional(),
      draft: z.boolean().default(false)
    })
});

export const collections = { blog };
