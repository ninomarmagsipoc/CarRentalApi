namespace CarRental.Model
{
    public class AnalyticsModel
    {
        public decimal TotalIncome { get; set; }
        public int ActiveUsers { get; set; }
        public int TotalRentals { get; set; }
        public int CarsRentedToday { get; set; }
        public int TotalCars { get; set; }
        public int ActiveRentals { get; set; }

        public List<ChartData> MonthlyRevenue { get; set; } = new List<ChartData>();
        public List<ChartData> DailyRevenue { get; set; } = new List<ChartData>();
        public List<PieChartData> PopularCars { get; set; } = new List<PieChartData>();
    }

    public class ChartData
    {
        public string Name { get; set; }
        public decimal Revenue { get; set; }
    }

    public class PieChartData
    {
        public string Name { get; set; }
        public int Value { get; set; }
    }
}
