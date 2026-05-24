using CarRental.Model;
using CarRental.Model.Response;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using System.Data;
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

                    using (var multi = await conn.QueryMultipleAsync("dash_GetDashboardAnalytics", commandType: CommandType.StoredProcedure))
                    {
                        var stats = await multi.ReadSingleOrDefaultAsync<AnalyticsModel>();
                        if (stats != null)
                        {
                            analytics.TotalIncome = stats.TotalIncome;
                            analytics.ActiveUsers = stats.ActiveUsers;
                            analytics.TotalRentals = stats.TotalRentals;
                            analytics.CarsRentedToday = stats.CarsRentedToday;
                            analytics.ActiveRentals = stats.ActiveRentals;
                            analytics.TotalCars = stats.TotalCars;
                        }

                        analytics.MonthlyRevenue = (await multi.ReadAsync<ChartData>()).ToList();

                        analytics.DailyRevenue = (await multi.ReadAsync<ChartData>()).ToList();

                        analytics.PopularCars = (await multi.ReadAsync<PieChartData>()).ToList();
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