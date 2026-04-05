using PatinaBlazor.Data;

namespace PatinaBlazor.Services;

public interface IIrcEventService
{
    Task<IrcEvent> LogEventAsync(IrcEvent ircEvent);
}
