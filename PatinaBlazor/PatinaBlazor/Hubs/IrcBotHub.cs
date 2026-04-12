using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using PatinaBlazor.Data;
using PatinaBlazor.Services;

namespace PatinaBlazor.Hubs;

public class IrcBotHub : Hub
{
    private readonly IIrcEventService _ircEventService;
    private readonly IrcApiSettings _apiSettings;
    private readonly IrcBotService _botService;

    public IrcBotHub(IIrcEventService ircEventService, IOptions<IrcApiSettings> apiSettings, IrcBotService botService)
    {
        _ircEventService = ircEventService;
        _apiSettings = apiSettings.Value;
        _botService = botService;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();

        string? apiKey = httpContext?.Request.Headers["X-Api-Key"].ToString();

        if (string.IsNullOrEmpty(apiKey))
            apiKey = httpContext?.Request.Query["apiKey"].ToString();

        if (string.IsNullOrEmpty(apiKey) || !_apiSettings.ApiKeys.Any(k => k.Trim() == apiKey.Trim()))
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _botService.Release(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Registers this connection as a handler for a channel. First bot becomes primary;
    /// subsequent bots are queued as standby and promoted automatically if the primary disconnects.
    /// </summary>
    public Task Register(string network, string channel)
    {
        _botService.Register(network, channel, Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Logs an IRC event to the database and notifies the chat UI.
    /// Returns the new event ID.
    /// </summary>
    public async Task<int> LogEvent(IrcEventRequest request)
    {
        if (!Enum.TryParse<ChatAction>(request.Action, ignoreCase: true, out var chatAction))
            throw new HubException($"Invalid action: {request.Action}");

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

        var result = await _ircEventService.LogEventAsync(ircEvent);
        return result.Id;
    }
}
