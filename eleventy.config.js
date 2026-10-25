/**
 * @fileoverview 11ty v3 ESM configuration for the FluxEncoder documentation site.
 * Enforces Nunjucks (.njk) as the single template engine with a component-first
 * macro architecture in `site/_includes/components/`.
 */

function slugifyText(value) {
  return String(value ?? '')
    .toLowerCase()
    .replace(/<[^>]+>/g, '')
    .replace(/&[a-z0-9#]+;/gi, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

export default function (eleventyConfig) {
  // Static asset and schema passthrough
  eleventyConfig.addPassthroughCopy({ 'site/assets': 'assets' });
  eleventyConfig.addPassthroughCopy({ 'site/schema': 'schema' });

  // Watch modular CSS partials
  eleventyConfig.addWatchTarget('./site/styles/');

  // Filter: deterministic slugify for heading anchors and IDs
  eleventyConfig.addFilter('slugify', (str) => slugifyText(str));

  // Filter: inject deterministic `id` attributes and anchor links into <h2> and <h3> tags
  eleventyConfig.addFilter('withHeadingIds', (htmlContent) => {
    if (!htmlContent || typeof htmlContent !== 'string') {
      return htmlContent;
    }
    return htmlContent.replace(/<h([23])(?![^>]*\bid=)([^>]*)>([\s\S]*?)<\/h\1>/gi, (_match, level, attrs, inner) => {
      const id = slugifyText(inner);
      if (!id) {
        return `<h${level}${attrs}>${inner}</h${level}>`;
      }
      return `<h${level} id="${id}"${attrs}>${inner}<a class="flux-heading-anchor" href="#${id}" aria-label="Direct link to section">#</a></h${level}>`;
    });
  });

  // Filter: extract <h2> and <h3> headings from rendered content for automatic On-This-Page TOC
  eleventyConfig.addFilter('extractHeadings', (htmlContent) => {
    if (!htmlContent || typeof htmlContent !== 'string') {
      return [];
    }
    const headings = [];
    const regex = /<h([23])(?:[^>]*\bid="([^"]+)")?[^>]*>([\s\S]*?)<\/h\1>/gi;
    let match;
    while ((match = regex.exec(htmlContent)) !== null) {
      const level = Number(match[1]);
      const rawText = match[3]
        .replace(/<a[^>]*class="flux-heading-anchor"[^>]*>[\s\S]*?<\/a>/gi, '')
        .replace(/<[^>]+>/g, '')
        .trim();
      const id = match[2] || slugifyText(rawText);
      if (id && rawText) {
        headings.push({ level, id, text: rawText });
      }
    }
    return headings;
  });

  // Filter: JSON formatting for debug/inspection
  eleventyConfig.addFilter('prettyJson', (value) => JSON.stringify(value, null, 2));

  return {
    dir: {
      input: 'site',
      includes: '_includes',
      data: '_data',
      output: '_site',
    },
    templateFormats: ['njk', 'md'],
    markdownTemplateEngine: 'njk',
    htmlTemplateEngine: 'njk',
  };
}
