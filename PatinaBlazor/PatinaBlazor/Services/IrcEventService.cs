using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services;

// Uses IDbContextFactory rather than an injected scoped ApplicationDbContext: Blazor
// Server keeps one DI scope (and one scoped DbContext) alive for a circuit's entire
// lifetime, not per page, so a query from the page a user just left can still be
// in-flight when the next page's query starts - and EF Core's DbContext isn't safe
// for concurrent use. Every method here gets its own short-lived context instead.
public class IrcEventService : IIrcEventService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IrcChatNotifier _notifier;

    public IrcEventService(IDbContextFactory<ApplicationDbContext> contextFactory, IrcChatNotifier notifier)
    {
        _contextFactory = contextFactory;
        _notifier = notifier;
    }

    public async Task<IrcEvent> LogEventAsync(IrcEvent ircEvent)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.IrcEvents.Add(ircEvent);
        await context.SaveChangesAsync();
        _notifier.Notify(ircEvent);
        return ircEvent;
    }

    public async Task<List<IrcEvent>> GetRecentEventsAsync(int count, string? network, string? channel)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.IrcEvents.AsQueryable();

        if (!string.IsNullOrEmpty(network))
            query = query.Where(e => e.Network == network);

        if (!string.IsNullOrEmpty(channel))
            query = query.Where(e => e.Channel == channel);

        var events = await query
            .OrderByDescending(e => e.CreatedDate)
            .Take(count)
            .ToListAsync();

        events.Reverse();
        return events;
    }

    public async Task<List<string>> GetNetworksAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.IrcEvents
            .Select(e => e.Network)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }

    public async Task<List<string>> GetChannelsAsync(string network)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.IrcEvents
            .Where(e => e.Network == network && e.Channel != null)
            .Select(e => e.Channel!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }
}
