using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public interface IIrcStatsService
    {
        /// <summary>
        /// Aggregates IRC activity with Timestamp in [fromUtc, toUtc). When <paramref name="network"/>
        /// is given, everything is restricted to that network.
        /// </summary>
        Task<IrcStatsSummary> GetStatsAsync(DateTime fromUtc, DateTime toUtc, string? network = null, int topN = 25);
    }
}
