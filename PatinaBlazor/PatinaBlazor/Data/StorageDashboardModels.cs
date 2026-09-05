namespace PatinaBlazor.Data
{
    public class StorageDashboardSummary
    {
        public int TotalProperties { get; set; }
        public int TotalUnits { get; set; }
        public int OccupiedUnits { get; set; }
        public int AvailableUnits { get; set; }
        public int ReservedUnits { get; set; }
        public int MaintenanceUnits { get; set; }
        public double OccupancyPercent { get; set; }
        public decimal CurrentMrr { get; set; }
        public decimal ProjectedNextMonthRevenue { get; set; }
    }

    public class MonthlyRevenuePoint
    {
        public string MonthLabel { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public bool IsProjected { get; set; }
    }
}
