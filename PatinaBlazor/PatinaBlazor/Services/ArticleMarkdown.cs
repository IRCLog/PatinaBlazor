using Ganss.Xss;
using Markdig;

namespace PatinaBlazor.Services
{
    // Renders an article's Markdown source to display-ready HTML. Sanitized at render
    // time (not at save time) so a future sanitizer rule update applies retroactively
    // to existing content without needing to re-edit anything. Stateless, so this is a
    // plain static helper rather than a DI-registered service.
    public static class ArticleMarkdown
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        private static readonly HtmlSanitizer Sanitizer = new();

        public static string ToSafeHtml(string markdown)
        {
            var html = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
            return Sanitizer.Sanitize(html);
        }
    }
}
