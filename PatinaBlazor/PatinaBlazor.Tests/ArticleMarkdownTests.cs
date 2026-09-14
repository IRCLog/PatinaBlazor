using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Pure logic, no database - reproduces the sanitizer verification that was previously
    // only done once by hand in a live browser (embedding a raw <script> tag in a real
    // article's Markdown and checking document.body.innerHTML - see CLAUDE.md's 2026-09-06
    // Articles entry). Permanent coverage for the exact class of bug that check was for:
    // article bodies render on genuinely anonymous-reachable pages.
    public class ArticleMarkdownTests
    {
        [Fact]
        public void ScriptTagIsStrippedEntirely()
        {
            var html = ArticleMarkdown.ToSafeHtml("Hello <script>alert('xss')</script> world");

            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alert(", html);
        }

        [Fact]
        public void OnErrorAttributeOnAnImgTagIsStripped()
        {
            var html = ArticleMarkdown.ToSafeHtml("<img src=x onerror=\"alert('xss')\">");

            Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void JavascriptHrefIsStripped()
        {
            var html = ArticleMarkdown.ToSafeHtml("[click me](javascript:alert('xss'))");

            Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OrdinaryMarkdownRendersAsRealHtmlElements()
        {
            var html = ArticleMarkdown.ToSafeHtml("# Heading\n\nSome **bold** text and a [link](https://example.com).");

            Assert.Contains("<h1", html);
            Assert.Contains("<strong>bold</strong>", html);
            Assert.Contains("<a href=\"https://example.com\"", html);
        }

        [Fact]
        public void NullInputDoesNotThrowAndRendersEmpty()
        {
            var html = ArticleMarkdown.ToSafeHtml(null!);

            Assert.Equal(string.Empty, html.Trim());
        }
    }
}
