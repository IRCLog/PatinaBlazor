namespace PatinaBlazor.Services;

public class IrcApiSettings
{
    public List<string> ApiKeys { get; set; } = [];

    /// <summary>How often (seconds) the heartbeat service pings registered bots. Default: 120.</summary>
    public int PingIntervalSeconds { get; set; } = 120;

    /// <summary>How long (seconds) without a pong before a connection is considered stale. Default: 300.</summary>
    public int StaleThresholdSeconds { get; set; } = 300;
}
