import rss from '@astrojs/rss';
import type { APIContext } from 'astro';
import { blogPath, posts, slugOf } from '../../blog';
import { useTranslations } from '../../i18n';

export async function GET(context: APIContext) {
  const t = useTranslations('en');
  return rss({
    title: `Tokendial — ${t('blog.title')}`,
    description: t('blog.lead'),
    site: context.site ?? 'https://tokendial.app',
    items: (await posts('en')).map((post) => ({
      title: post.data.title,
      description: post.data.description,
      pubDate: post.data.date,
      link: blogPath('en', slugOf(post))
    }))
  });
}
