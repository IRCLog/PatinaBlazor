using PatinaBlazor.Data;

namespace PatinaBlazor.Services;

public class IrcEventService : IIrcEventService
{
    private readonly ApplicationDbContext _context;

    public IrcEventService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IrcEvent> LogEventAsync(IrcEvent ircEvent)
    {
        _context.IrcEvents.Add(ircEvent);
        await _context.SaveChangesAsync();
        return ircEvent;
    }
}
