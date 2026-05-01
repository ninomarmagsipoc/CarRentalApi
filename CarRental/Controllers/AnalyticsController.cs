using CarRental.Model;
using CarRental.Model.Response;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace CarRental.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly string _connectionString;

        public AnalyticsController(IConfiguration config)
        {
            _connectionString = config["ConnectionStrings:CarRental"];
        }

        [HttpGet]
        public async Task<ActionResult<ServiceResponse<object>>> GetDashboardAnalytics()
        {
            var response = new ServiceResponse<object>();
            var analytics = new AnalyticsModel();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1. TOP STATS (Gi-apil na ang TotalCars dinhi)
                    string statsQuery = @"
                        SELECT 
                            (SELECT ISNULL(SUM(
                                CASE 
                                    WHEN PaymentStatus IN ('Paid', 'Completed', 'Success') THEN Amount
                                    WHEN PaymentStatus = 'Refunded' AND (RefundReason LIKE '%cancel%' OR RefundReason LIKE '%75%%') THEN Amount * 0.25
                                    ELSE 0 
                                END
                            ), 0) FROM Payment) AS TotalIncome,
                            (SELECT COUNT(*) FROM Users WHERE Role = 'User') AS ActiveUsers,
                            (SELECT COUNT(*) FROM Rentals) AS TotalRentals,
                            (SELECT COUNT(*) FROM Rentals WHERE CAST(GETDATE() AS DATE) BETWEEN CAST(StartDate AS DATE) AND CAST(EndDate AS DATE) AND Status = 'Rented') AS CarsRentedToday,
                            (SELECT COUNT(RentalID) FROM Rentals WHERE Status IN ('Approved', 'Confirmed', 'Rented')) AS ActiveRental,
                            (SELECT COUNT(*) FROM Cars) AS TotalCars;"; // 🟢 ADDED TOTAL CARS

                    using (var cmd = new SqlCommand(statsQuery, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            analytics.TotalIncome = reader["TotalIncome"] != DBNull.Value ? Convert.ToDecimal(reader["TotalIncome"]) : 0;
                            analytics.ActiveUsers = reader["ActiveUsers"] != DBNull.Value ? Convert.ToInt32(reader["ActiveUsers"]) : 0;
                            analytics.TotalRentals = reader["TotalRentals"] != DBNull.Value ? Convert.ToInt32(reader["TotalRentals"]) : 0;
                            analytics.CarsRentedToday = reader["CarsRentedToday"] != DBNull.Value ? Convert.ToInt32(reader["CarsRentedToday"]) : 0;
                            analytics.ActiveRentals = reader["ActiveRental"] != DBNull.Value ? Convert.ToInt32(reader["ActiveRental"]) : 0;
                            analytics.TotalCars = reader["TotalCars"] != DBNull.Value ? Convert.ToInt32(reader["TotalCars"]) : 0; // 🟢 ADDED MAPPING
                        }
                    }

                    // 2. MONTHLY REVENUE 
                    string monthlyQuery = @"
                        SELECT DATENAME(month, CreatedAt) AS MonthName, 
                               ISNULL(SUM(
                                   CASE 
                                       WHEN PaymentStatus IN ('Paid', 'Completed', 'Success') THEN Amount
                                       WHEN PaymentStatus = 'Refunded' AND (RefundReason LIKE '%cancel%' OR RefundReason LIKE '%75%%') THEN Amount * 0.25
                                       ELSE 0 
                                   END
                               ), 0) AS Revenue
                        FROM Payment
                        WHERE YEAR(CreatedAt) = YEAR(GETDATE())
                          AND (PaymentStatus IN ('Paid', 'Completed', 'Success') 
                               OR (PaymentStatus = 'Refunded' AND (RefundReason LIKE '%cancel%' OR RefundReason LIKE '%75%%')))
                        GROUP BY DATENAME(month, CreatedAt), MONTH(CreatedAt)
                        ORDER BY MONTH(CreatedAt);";

                    using (var cmd = new SqlCommand(monthlyQuery, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            analytics.MonthlyRevenue.Add(new ChartData
                            {
                                Name = reader["MonthName"].ToString()?.Substring(0, 3),
                                Revenue = Convert.ToDecimal(reader["Revenue"])
                            });
                        }
                    }

                    // 3. DAILY REVENUE 
                    string dailyQuery = @"
                        SELECT DATENAME(weekday, CreatedAt) AS DayName, 
                               ISNULL(SUM(
                                   CASE 
                                       WHEN PaymentStatus IN ('Paid', 'Completed', 'Success') THEN Amount
                                       WHEN PaymentStatus = 'Refunded' AND (RefundReason LIKE '%cancel%' OR RefundReason LIKE '%75%%') THEN Amount * 0.25
                                       ELSE 0 
                                   END
                               ), 0) AS Revenue
                        FROM Payment
                        WHERE CreatedAt >= DATEADD(day, -7, GETDATE())
                          AND (PaymentStatus IN ('Paid', 'Completed', 'Success') 
                               OR (PaymentStatus = 'Refunded' AND (RefundReason LIKE '%cancel%' OR RefundReason LIKE '%75%%')))
                        GROUP BY DATENAME(weekday, CreatedAt), CAST(CreatedAt AS DATE)
                        ORDER BY CAST(CreatedAt AS DATE);";

                    using (var cmd = new SqlCommand(dailyQuery, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            analytics.DailyRevenue.Add(new ChartData
                            {
                                Name = reader["DayName"].ToString()?.Substring(0, 3),
                                Revenue = Convert.ToDecimal(reader["Revenue"])
                            });
                        }
                    }

                    // 4. MOST POPULAR CARS
                    string popularQuery = @"
                        SELECT TOP 5 c.CarName, COUNT(r.RentalID) AS RentCount
                        FROM Rentals r
                        JOIN Cars c ON r.CarID = c.CarID
                        GROUP BY c.CarName
                        ORDER BY RentCount DESC;";

                    using (var cmd = new SqlCommand(popularQuery, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            analytics.PopularCars.Add(new PieChartData
                            {
                                Name = reader["CarName"].ToString(),
                                Value = Convert.ToInt32(reader["RentCount"])
                            });
                        }
                    }
                }

                response.StatusCode = 200;
                response.Message = "Analytics fetched successfully";
                response.Data = analytics;
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
                return StatusCode(500, response);
            }
        }
    }
}