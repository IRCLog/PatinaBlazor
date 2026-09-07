using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace PatinaBlazor.Services
{
    // Renders a Razor component (an email template under Components/Emails/) to a raw
    // HTML string, using the same HtmlRenderer mechanism ASP.NET Core itself uses for
    // static prerendering - no separate templating engine needed. A fresh DI scope and
    // HtmlRenderer are created per call (the documented pattern for rendering components
    // outside of a normal HTTP request) so concurrent email sends can never share
    // renderer state; this is not a hot path, so the per-call setup cost doesn't matter.
    public class EmailTemplateRenderer
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public EmailTemplateRenderer(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await using var htmlRenderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

            return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await htmlRenderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
                return output.ToHtmlString();
            });
        }
    }
}
