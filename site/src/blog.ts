import { getCollection, type CollectionEntry } from 'astro:content';
import { defaultLocale, type Locale } from './i18n';

export type Post = CollectionEntry<'blog'>;
export type Section = Post['data']['section'];

/** The languages the blog is written in. Other locales read the English edition. */
export const blogLocales: Locale[] = ['en', 'es'];

export const sections: Section[] = ['save-tokens', 'install', 'tools', 'news'];

export function blogLocale(locale: Locale): Locale {
  return blogLocales.includes(locale) ? locale : defaultLocale;
}

export function blogPath(locale: Locale, slug = ''): string {
  const lang = blogLocale(locale);
  const base = lang === defaultLocale ? '/blog' : `/${lang}/blog`;
  return slug ? `${base}/${slug}` : base;
}

export function langOf(post: Post): Locale {
  return post.id.split('/')[0] as Locale;
}

export function slugOf(post: Post): string {
  return post.id.split('/').slice(1).join('/');
}

export async function posts(locale: Locale): Promise<Post[]> {
  const lang = blogLocale(locale);
  const all = await getCollection('blog', (post) => !post.data.draft && langOf(post) === lang);
  return all.sort((a, b) => b.data.date.getTime() - a.data.date.getTime());
}

export function readingMinutes(body: string | undefined): number {
  const words = (body ?? '').split(/\s+/).filter(Boolean).length;
  return Math.max(1, Math.round(words / 200));
}
