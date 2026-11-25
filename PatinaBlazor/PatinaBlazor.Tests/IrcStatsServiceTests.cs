using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Each test uses its own unique network name and filters the service to it, since the
    // shared "Database" collection is never empty and other tests may add IRC rows too.
    [Collection("Database")]
    public class IrcStatsServiceTests
    {
        private static readonly DateTime Base = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        private readonly DatabaseFixture _fixture;

        public IrcStatsServiceTests(DatabaseFixture fixture) => _fixture = fixture;

        private async Task SeedAsync(params IrcEvent[] events)
        {
            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            context.IrcEvents.AddRange(events);
            await context.SaveChangesAsync();
        }

        private static IrcEvent Evt(string network, ChatAction action, string? channel, string? sender, DateTime ts) =>
            new() { Network = network, Action = action, Channel = channel, Sender = sender, Timestamp = ts };

        private async Task<IrcStatsSummary> StatsAsync(string network, DateTime? from = null, DateTime? to = null, int topN = 25)
        {
            using var scope = _fixture.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IIrcStatsService>();
            return await svc.GetStatsAsync(from ?? Base.AddDays(-1), to ?? Base.AddDays(1), network, topN);
        }

        [Fact]
        public async Task GetStatsAsync_CountsEventsMessagesUsersAndChannels()
        {
            var net = "net-" + Guid.NewGuid();
            await SeedAsync(
                Evt(net, ChatAction.MESSAGE, "#a", "alice", Base),
                Evt(net, ChatAction.ACTION, "#a", "bob", Base.AddMinutes(1)),
                Evt(net, ChatAction.JOIN, "#a", "bob", Base.AddMinutes(2)),
                Evt(net, ChatAction.MESSAGE, "#b", "alice", Base.AddMinutes(3)));

            var s = await StatsAsync(net);

            Assert.Equal(4, s.TotalEvents);
            Assert.Equal(3, s.TotalMessages); // JOIN is not a message
            Assert.Equal(2, s.DistinctUsers);
            Assert.Equal(2, s.DistinctChannels);

            var n = Assert.Single(s.Networks);
            Assert.Equal((4, 3, 2, 2), (n.Events, n.Messages, n.Users, n.Channels));

            var a = Assert.Single(s.Channels, c => c.Channel == "#a");
            Assert.Equal((3, 2, 2), (a.Events, a.Messages, a.Users));

            var alice = Assert.Single(s.Users, u => u.Sender == "alice");
            Assert.Equal((2, 2, 2), (alice.Events, alice.Messages, alice.Channels));
            var bob = Assert.Single(s.Users, u => u.Sender == "bob");
            Assert.Equal((2, 1, 1), (bob.Events, bob.Messages, bob.Channels)); // JOIN counted as activity
        }

        [Fact]
        public async Task GetStatsAsync_DateRange_IsInclusiveFromAndExclusiveTo()
        {
            var net = "net-" + Guid.NewGuid();
            var from = Base;
            var to = Base.AddHours(1);
            await SeedAsync(
                Evt(net, ChatAction.MESSAGE, "#a", "u", from.AddSeconds(-1)), // before
                Evt(net, ChatAction.MESSAGE, "#a", "u", from),                // included
                Evt(net, ChatAction.MESSAGE, "#a", "u", to.AddSeconds(-1)),   // included
                Evt(net, ChatAction.MESSAGE, "#a", "u", to));                 // excluded

            var s = await StatsAsync(net, from, to);

            Assert.Equal(2, s.TotalEvents);
        }

        [Fact]
        public async Task GetStatsAsync_ExcludesNullEmptyAndGuidSendersFromUserStatsOnly()
        {
            var net = "net-" + Guid.NewGuid();
            await SeedAsync(
                Evt(net, ChatAction.MESSAGE, "#a", "alice", Base),
                Evt(net, ChatAction.MESSAGE, "#a", null, Base),
                Evt(net, ChatAction.MESSAGE, "#a", "", Base),
                Evt(net, ChatAction.MESSAGE, "#a", Guid.NewGuid().ToString(), Base));

            var s = await StatsAsync(net);

            Assert.Equal(4, s.TotalEvents); // still counted as events
            Assert.Equal(1, s.DistinctUsers);
            Assert.Equal("alice", Assert.Single(s.Users).Sender);
        }

        [Fact]
        public async Task GetStatsAsync_TopN_OrdersByEventsDescendingAndLimits()
        {
            var net = "net-" + Guid.NewGuid();
            var events = new List<IrcEvent>();
            for (var i = 0; i < 3; i++) events.Add(Evt(net, ChatAction.MESSAGE, "#big", "heavy", Base.AddMinutes(i)));
            events.Add(Evt(net, ChatAction.MESSAGE, "#mid", "medium", Base));
            events.Add(Evt(net, ChatAction.MESSAGE, "#mid", "medium", Base.AddMinutes(1)));
            events.Add(Evt(net, ChatAction.MESSAGE, "#small", "light", Base));
            await SeedAsync(events.ToArray());

            var s = await StatsAsync(net, topN: 2);

            Assert.Equal(new[] { "#big", "#mid" }, s.Channels.Select(c => c.Channel));
            Assert.Equal(new[] { "heavy", "medium" }, s.Users.Select(u => u.Sender));
        }

        [Fact]
        public async Task GetStatsAsync_GroupsEventsByUtcDay()
        {
            var net = "net-" + Guid.NewGuid();
            await SeedAsync(
                Evt(net, ChatAction.MESSAGE, "#a", "u", Base),
                Evt(net, ChatAction.MESSAGE, "#a", "u", Base.AddHours(1)),
                Evt(net, ChatAction.MESSAGE, "#a", "u", Base.AddDays(1) - TimeSpan.FromHours(1) + TimeSpan.FromDays(0.5)));

            var s = await StatsAsync(net, Base.AddDays(-1), Base.AddDays(3));

            Assert.Equal(2, s.Days.Count);
            Assert.Equal(new[] { 2, 1 }, s.Days.Select(d => d.Events));
        }

        [Fact]
        public async Task GetStatsAsync_NetworkFilter_ExcludesOtherNetworks()
        {
            var net = "net-" + Guid.NewGuid();
            var other = "net-" + Guid.NewGuid();
            await SeedAsync(
                Evt(net, ChatAction.MESSAGE, "#a", "u", Base),
                Evt(other, ChatAction.MESSAGE, "#a", "u", Base));

            var s = await StatsAsync(net);

            Assert.Equal(1, s.TotalEvents);
            Assert.All(s.Networks, n => Assert.Equal(net, n.Network));
        }
    }
}
