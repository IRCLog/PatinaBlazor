using System.Net;
using System.Text;

namespace PatinaBlazor.Tests.Fakes
{
    // A minimal routing HttpMessageHandler, keyed by request path, for testing PayPalClient
    // fully offline - no real network calls, no Testcontainers, no DI container needed.
    // PayPalClient is constructed with a real HttpClient wrapping this handler, so the whole
    // pipeline (PostAsync's status-code/exception handling, ExtractErrorMessage's branching,
    // each public method's response mapping) runs for real against canned responses, rather
    // than testing any of that logic in isolation.
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<string, HttpResponseMessage>> _routes = new();

        // Every PayPalClient call fetches an OAuth2 access token first (GetAccessTokenAsync) -
        // defaulted to a working response so a test only has to configure the one path it's
        // actually exercising. Override via On("/v1/oauth2/token", ...) for a token-fetch test.
        public FakeHttpMessageHandler()
        {
            _routes["/v1/oauth2/token"] = _ => JsonResponse(HttpStatusCode.OK, """{"access_token":"FAKE-ACCESS-TOKEN","expires_in":32400}""");
        }

        public List<(string Path, string Body)> Requests { get; } = [];

        public void On(string path, Func<string, HttpResponseMessage> respond) => _routes[path] = respond;

        public void On(string path, HttpStatusCode status, string jsonBody) => _routes[path] = _ => JsonResponse(status, jsonBody);

        public void Throw(string path, Exception exception) => _routes[path] = _ => throw exception;

        public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((path, body));

            if (!_routes.TryGetValue(path, out var respond))
            {
                throw new InvalidOperationException($"FakeHttpMessageHandler has no route configured for {path}.");
            }

            return respond(body);
        }
    }
}
