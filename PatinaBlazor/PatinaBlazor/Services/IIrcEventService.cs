using PatinaBlazor.Data;

namespace PatinaBlazor.Services;

public interface IIrcEventService
{
    Task<IrcEvent> LogEventAsync(IrcEvent ircEvent);
    Task<List<IrcEvent>> GetRecentEventsAsync(int count, string? network, string? channel);
    Task<List<string>> GetNetworksAsync();
    Task<List<string>> GetChannelsAsync(string network);
}
