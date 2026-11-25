namespace PatinaBlazor.Data
{
    public class IrcStatsSummary
    {
        public int TotalEvents { get; set; }
        /// <summary>MESSAGE + ACTION events (actual chat, not presence noise).</summary>
        public int TotalMessages { get; set; }
        public int DistinctUsers { get; set; }
        public int DistinctChannels { get; set; }

        public List<IrcNetworkStat> Networks { get; set; } = new();
        public List<IrcChannelStat> Channels { get; set; } = new();
        public List<IrcUserStat> Users { get; set; } = new();
        /// <summary>Events per UTC calendar day, ascending, only days that had activity.</summary>
        public List<IrcDayStat> Days { get; set; } = new();
    }

    public class IrcNetworkStat
    {
        public string Network { get; set; } = string.Empty;
        public int Events { get; set; }
        public int Messages { get; set; }
        public int Users { get; set; }
        public int Channels { get; set; }
    }

    public class IrcChannelStat
    {
        public string Network { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public int Events { get; set; }
        public int Messages { get; set; }
        public int Users { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class IrcUserStat
    {
        public string Sender { get; set; } = string.Empty;
        public int Events { get; set; }
        public int Messages { get; set; }
        public int Channels { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class IrcDayStat
    {
        public DateTime Day { get; set; }
        public int Events { get; set; }
    }
}
