using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // IDbContextFactory per call, same reasoning as AdminDashboardService/IrcEventService.
    public class IrcStatsService : IIrcStatsService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public IrcStatsService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<IrcStatsSummary> GetStatsAsync(DateTime fromUtc, DateTime toUtc, string? network = null, int topN = 25)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var events = context.IrcEvents
                .AsNoTracking()
                .Where(e => e.Timestamp >= fromUtc && e.Timestamp < toUtc);
            if (!string.IsNullOrEmpty(network))
            {
                events = events.Where(e => e.Network == network);
            }

            // "Real" IRC senders only: the Chat page's own outbound messages store the web-app
            // user's GUID in Sender (see Chat.razor), which is not an IRC nick. A 36-char value
            // containing '-' is the GUID shape. Null/empty senders (e.g. some bot events) are
            // also excluded from user stats - they still count toward event/channel totals.
            var userEvents = events.Where(e =>
                e.Sender != null && e.Sender != "" &&
                !(e.Sender.Length == 36 && e.Sender.Contains("-")));

            var summary = new IrcStatsSummary
            {
                TotalEvents = await events.CountAsync(),
                TotalMessages = await events.CountAsync(e => e.Action == ChatAction.MESSAGE || e.Action == ChatAction.ACTION),
                DistinctUsers = await userEvents.Select(e => e.Sender).Distinct().CountAsync(),
                DistinctChannels = await events.Where(e => e.Channel != null && e.Channel != "")
                    .Select(e => e.Network + "|" + e.Channel).Distinct().CountAsync()
            };

            summary.Networks = await events
                .GroupBy(e => e.Network)
                .Select(g => new IrcNetworkStat
                {
                    Network = g.Key,
                    Events = g.Count(),
                    Messages = g.Count(e => e.Action == ChatAction.MESSAGE || e.Action == ChatAction.ACTION),
                    Users = g.Where(e => e.Sender != null && e.Sender != "" &&
                                         !(e.Sender.Length == 36 && e.Sender.Contains("-")))
                             .Select(e => e.Sender).Distinct().Count(),
                    Channels = g.Where(e => e.Channel != null && e.Channel != "")
                                .Select(e => e.Channel).Distinct().Count()
                })
                .OrderByDescending(n => n.Events)
                .ToListAsync();

            summary.Channels = await events
                .Where(e => e.Channel != null && e.Channel != "")
                .GroupBy(e => new { e.Network, e.Channel })
                .Select(g => new IrcChannelStat
                {
                    Network = g.Key.Network,
                    Channel = g.Key.Channel!,
                    Events = g.Count(),
                    Messages = g.Count(e => e.Action == ChatAction.MESSAGE || e.Action == ChatAction.ACTION),
                    Users = g.Where(e => e.Sender != null && e.Sender != "" &&
                                         !(e.Sender.Length == 36 && e.Sender.Contains("-")))
                             .Select(e => e.Sender).Distinct().Count(),
                    LastActivity = g.Max(e => e.Timestamp)
                })
                .OrderByDescending(c => c.Events)
                .Take(topN)
                .ToListAsync();

            summary.Users = await userEvents
                .GroupBy(e => e.Sender!)
                .Select(g => new IrcUserStat
                {
                    Sender = g.Key,
                    Events = g.Count(),
                    Messages = g.Count(e => e.Action == ChatAction.MESSAGE || e.Action == ChatAction.ACTION),
                    Channels = g.Where(e => e.Channel != null && e.Channel != "")
                                .Select(e => e.Network + "|" + e.Channel).Distinct().Count(),
                    LastSeen = g.Max(e => e.Timestamp)
                })
                .OrderByDescending(u => u.Events)
                .Take(topN)
                .ToListAsync();

            summary.Days = await events
                .GroupBy(e => e.Timestamp.Date)
                .Select(g => new IrcDayStat { Day = g.Key, Events = g.Count() })
                .OrderBy(d => d.Day)
                .ToListAsync();

            return summary;
        }
    }
}
