using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    // Read-only mapping onto the "Logs" table Serilog's MSSqlServer sink creates and writes
    // to directly (see Program.cs's UseSerilog configuration) - EF never inserts or updates
    // rows here, only queries them. Mapped as a keyless entity type excluded from migrations
    // in ApplicationDbContext, since Serilog (not EF) owns this table's schema end to end.
    public class AppLogEntry
    {
        public int Id { get; set; }

        // Stored UTC (Program.cs sets columnOptions.TimeStamp.ConvertToUtc = true), matching
        // every other timestamp column in the app. Use TimeStampCentral for display.
        public DateTime TimeStamp { get; set; }

        public string? Level { get; set; }
        public string? Message { get; set; }
        public string? MessageTemplate { get; set; }
        public string? Exception { get; set; }
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public string? UserId { get; set; }
        public string? Properties { get; set; }

        [NotMapped]
        public DateTime TimeStampCentral => TimeZoneInfo.ConvertTimeFromUtc(TimeStamp, CentralZone);

        // "Central Standard Time" is the Windows time zone id; .NET's ICU-based TimeZoneInfo
        // (used on Linux) has resolved Windows ids cross-platform since .NET 6, confirmed
        // directly against this app's target framework rather than assumed - so this same id
        // works unchanged on both the Linux dev machine and the Windows production host.
        // Public so callers (e.g. AdminLogs.razor's date-range filter, which is Central-facing
        // but must query the underlying UTC column) can convert using this exact same zone.
        public static readonly TimeZoneInfo CentralZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    }
}
