using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services;

public class IrcEventService : IIrcEventService
{
    private readonly ApplicationDbContext _context;
    private readonly IrcChatNotifier _notifier;

    public IrcEventService(ApplicationDbContext context, IrcChatNotifier notifier)
    {
        _context = context;
        _notifier = notifier;
    }

    public async Task<IrcEvent> LogEventAsync(IrcEvent ircEvent)
    {
        _context.IrcEvents.Add(ircEvent);
        await _context.SaveChangesAsync();
        _notifier.Notify(ircEvent);
        return ircEvent;
    }

    public async Task<List<IrcEvent>> GetRecentEventsAsync(int count, string? network, string? channel)
    {
        var query = _context.IrcEvents.AsQueryable();

        if (!string.IsNullOrEmpty(network))
            query = query.Where(e => e.Network == network);

        if (!string.IsNullOrEmpty(channel))
            query = query.Where(e => e.Channel == channel);

        var events = await query
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync();

        events.Reverse();
        return events;
    }

    public async Task<List<string>> GetNetworksAsync()
    {
        return await _context.IrcEvents
            .Select(e => e.Network)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }

    public async Task<List<string>> GetChannelsAsync(string network)
    {
        return await _context.IrcEvents
            .Where(e => e.Network == network && e.Channel != null)
            .Select(e => e.Channel!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }
}
