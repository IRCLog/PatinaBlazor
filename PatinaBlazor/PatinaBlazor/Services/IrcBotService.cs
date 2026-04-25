using Microsoft.AspNetCore.SignalR;
using PatinaBlazor.Data;
using PatinaBlazor.Hubs;

namespace PatinaBlazor.Services;

/// <summary>
/// Manages IRC bot connections and routes commands to the correct bot.
/// Tracks primary/standby ownership per channel and promotes standbys automatically
/// when a primary disconnects.
///
/// Inject this into any page that needs to send commands to bots.
/// The hub injects it for registration and release only.
/// </summary>
public class IrcBotService(IHubContext<IrcBotHub> hubContext)
{
    private readonly Dictionary<string, string> _channelOwners = new();
    private readonly Dictionary<string, List<string>> _standbys = new();
    private readonly Dictionary<string, DateTime> _lastPong = new();
    private readonly object _lock = new();

    /// <summary>
    /// Registers a connection as a handler for a channel. First connection becomes
    /// primary; subsequent connections are queued as standby and promoted automatically
    /// when the primary disconnects.
    /// </summary>
    public void Register(string network, string channel, string connectionId)
    {
        var key = Key(network, channel);

        lock (_lock)
        {
            _lastPong.TryAdd(connectionId, DateTime.UtcNow);

            if (_channelOwners.TryGetValue(key, out var existing) && existing == connectionId)
                return;

            if (!_channelOwners.ContainsKey(key))
            {
                _channelOwners[key] = connectionId;
                return;
            }

            if (!_standbys.TryGetValue(key, out var list))
                _standbys[key] = list = new List<string>();

            if (!list.Contains(connectionId))
                list.Add(connectionId);
        }
    }

    /// <summary>
    /// Releases all channels owned or queued by the given connection.
    /// The first standby per channel is silently promoted to primary.
    /// </summary>
    public void Release(string connectionId)
    {
        lock (_lock)
        {
            _lastPong.Remove(connectionId);

            foreach (var list in _standbys.Values)
                list.Remove(connectionId);

            var owned = _channelOwners
                .Where(kv => kv.Value == connectionId)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in owned)
            {
                _channelOwners.Remove(key);

                if (_standbys.TryGetValue(key, out var list) && list.Count > 0)
                {
                    _channelOwners[key] = list[0];
                    list.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>
    /// Records a pong response from a bot, resetting its staleness timer.
    /// </summary>
    public void RecordPong(string connectionId)
    {
        lock (_lock)
        {
            if (_lastPong.ContainsKey(connectionId))
                _lastPong[connectionId] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns all currently registered connection IDs (primary and standby).
    /// </summary>
    public IReadOnlyList<string> GetAllConnectionIds()
    {
        lock (_lock)
        {
            return _lastPong.Keys.ToList();
        }
    }

    /// <summary>
    /// Returns connection IDs that have not ponged within the given time window.
    /// </summary>
    public IReadOnlyList<string> GetStaleConnectionIds(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        lock (_lock)
        {
            return _lastPong.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        }
    }

    /// <summary>
    /// Sends a command to the primary bot for a channel.
    /// Returns false if no bot is registered for that channel.
    /// </summary>
    public async Task<bool> SendCommandAsync(string network, string channel, BotCommand command)
    {
        string? connectionId;

        lock (_lock)
        {
            _channelOwners.TryGetValue(Key(network, channel), out connectionId);
        }

        if (connectionId is null)
            return false;

        await hubContext.Clients.Client(connectionId).SendAsync("ReceiveCommand", command);
        return true;
    }

    private static string Key(string network, string channel) => $"{network}\x00{channel}";
}
