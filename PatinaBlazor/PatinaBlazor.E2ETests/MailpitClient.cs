using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PatinaBlazor.E2ETests
{
    // Fetches captured emails back out of Mailpit's REST API and extracts the link a real
    // user would click - the piece Playwright itself can't do (it can submit the form that
    // triggers an email, but it has no way to "receive" one). Mirrors, in code, the exact
    // manual curl-and-regex technique already used by hand multiple times in this project's
    // history to verify the real Register/ForgotPassword/ConfirmEmail flows (see CLAUDE.md's
    // Stage 2 Identity-logging verification notes).
    public class MailpitClient
    {
        private readonly HttpClient _http;

        public MailpitClient(string baseUrl)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        }

        // Polls (Mailpit delivery + the app's own SMTP send are both effectively instant
        // locally, but not synchronous with the form submit that triggered them) for the
        // most recently received message to toEmail, then extracts the first href in its
        // HTML body containing linkUrlContains (e.g. "ConfirmEmail" or "ResetPassword").
        //
        // HtmlDecode is required, not optional: Mailpit stores the raw HTML source as the
        // app actually sent it, where an href attribute's "&" between query parameters is
        // correctly encoded as "&amp;" - a real browser decodes that once when parsing the
        // attribute, but a plain regex over the raw source sees the encoded text literally.
        // This is the exact double-encoding class of bug this app hit once in production
        // (see CLAUDE.md) - decoding here reproduces what a real browser does automatically.
        public async Task<string> GetLatestEmailLinkAsync(string toEmail, string linkUrlContains, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(15));
            Exception? lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var latestId = await FindLatestMessageIdAsync(toEmail);
                    if (latestId != null)
                    {
                        var html = await GetMessageHtmlAsync(latestId);
                        var match = Regex.Match(html, "href=\"([^\"]*" + Regex.Escape(linkUrlContains) + "[^\"]*)\"");
                        if (match.Success)
                        {
                            return WebUtility.HtmlDecode(match.Groups[1].Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(500);
            }

            throw new TimeoutException(
                $"No email to '{toEmail}' with a link containing '{linkUrlContains}' arrived within the timeout.", lastError);
        }

        private async Task<string?> FindLatestMessageIdAsync(string toEmail)
        {
            var listJson = await _http.GetStringAsync("/api/v1/messages?limit=50");
            using var listDoc = JsonDocument.Parse(listJson);

            string? latestId = null;
            var latestCreated = DateTimeOffset.MinValue;

            foreach (var message in listDoc.RootElement.GetProperty("messages").EnumerateArray())
            {
                var isToRecipient = message.GetProperty("To").EnumerateArray()
                    .Any(to => string.Equals(to.GetProperty("Address").GetString(), toEmail, StringComparison.OrdinalIgnoreCase));
                if (!isToRecipient)
                {
                    continue;
                }

                var created = message.GetProperty("Created").GetDateTimeOffset();
                if (created > latestCreated)
                {
                    latestCreated = created;
                    latestId = message.GetProperty("ID").GetString();
                }
            }

            return latestId;
        }

        private async Task<string> GetMessageHtmlAsync(string messageId)
        {
            var messageJson = await _http.GetStringAsync($"/api/v1/message/{messageId}");
            using var messageDoc = JsonDocument.Parse(messageJson);
            return messageDoc.RootElement.GetProperty("HTML").GetString() ?? "";
        }
    }
}
