import { defineCollection, z } from 'astro:content';
import { glob } from 'astro/loaders';

/** Posts live in src/content/blog/<lang>/<slug>.mdx; the same slug in two languages is one article translated. */
const blog = defineCollection({
  loader: glob({ pattern: '**/*.mdx', base: './src/content/blog' }),
  schema: z.object({
    title: z.string(),
    description: z.string(),
    date: z.coerce.date(),
    updated: z.coerce.date().optional(),
    section: z.enum(['save-tokens', 'tools', 'install', 'news']),
    provider: z.string().optional(),
    draft: z.boolean().default(false)
  })
});

export const collections = { blog };
