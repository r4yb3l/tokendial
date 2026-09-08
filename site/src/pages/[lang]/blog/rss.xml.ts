import rss from '@astrojs/rss';
import type { APIContext } from 'astro';
import { blogLocales, blogPath, posts, slugOf } from '../../../blog';
import { defaultLocale, isLocale, useTranslations } from '../../../i18n';

export function getStaticPaths() {
  return blogLocales.filter((code) => code !== defaultLocale).map((code) => ({ params: { lang: code } }));
}

export async function GET(context: APIContext) {
  const lang = context.params.lang;
  if (!isLocale(lang)) return new Response(null, { status: 404 });
  const t = useTranslations(lang);
  return rss({
    title: `Tokendial — ${t('blog.title')}`,
    description: t('blog.lead'),
    site: context.site ?? 'https://tokendial.app',
    items: (await posts(lang)).map((post) => ({
      title: post.data.title,
      description: post.data.description,
      pubDate: post.data.date,
      link: blogPath(lang, slugOf(post))
    }))
  });
}
