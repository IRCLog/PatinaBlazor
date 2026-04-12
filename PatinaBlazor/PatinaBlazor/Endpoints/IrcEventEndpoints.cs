using Microsoft.Extensions.Options;
using PatinaBlazor.Data;
using PatinaBlazor.Services;

namespace PatinaBlazor.Endpoints;

public static class IrcEventEndpoints
{
    public static void MapIrcEventEndpoints(this WebApplication app)
    {
        app.MapPost("/api/irc/events", async (
            HttpContext httpContext,
            IrcEventRequest request,
            IIrcEventService ircEventService,
            IOptions<IrcApiSettings> apiSettings) =>
        {
            // Validate API key
            if (!httpContext.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) ||
                !apiSettings.Value.ApiKeys.Any(k => k.Trim() == apiKey.ToString().Trim()))
            {
                return Results.Unauthorized();
            }

            // Parse the action enum
            if (!Enum.TryParse<ChatAction>(request.Action, ignoreCase: true, out var chatAction))
            {
                return Results.BadRequest($"Invalid action: {request.Action}");
            }

            var ircEvent = new IrcEvent
            {
                Timestamp = request.Timestamp ?? DateTime.UtcNow,
                Action = chatAction,
                Message = request.Message,
                Target = request.Target,
                Network = request.Network,
                Channel = request.Channel,
                Sender = request.Sender,
                User = request.User
            };

            var result = await ircEventService.LogEventAsync(ircEvent);

            return Results.Created($"/api/irc/events/{result.Id}", new { id = result.Id });
        });
    }
}
