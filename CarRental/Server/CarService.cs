using CarRental.IRepository;
using CarRental.Model.Response;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class CarService : ICarRepository
    {
        private readonly SqlConnection conn;

        public CarService(IConfiguration config)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
        }

        public async Task<ServiceResponse<object>> GetCars()
        {
            var response = new ServiceResponse<object>();
            var cars = new List<object>();

            try
            {
                await conn.OpenAsync();

                // UPDATE: New query uses NOT EXISTS to filter out cars currently in the Rentals table
                // with an active status and an overlapping date range.
                string query = @"
                    SELECT c.* FROM Cars c
                    WHERE NOT EXISTS (
                        SELECT 1 
                        FROM Rentals r 
                        WHERE r.CarID = c.CarID 
                        AND r.Status IN ('Pending', 'Rented', 'Active') 
                        AND CAST(GETDATE() AS DATE) BETWEEN r.StartDate AND r.EndDate
                    )";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            cars.Add(new
                            {
                                CarID = reader["CarID"],
                                CarName = reader["CarName"].ToString(),
                                CarInfo = reader["CarInfo"]?.ToString(),
                                Seats = reader["Seats"],
                                PricePerDay = reader["PricePerDay"],
                                CarImage = reader["CarImage"]?.ToString()
                            });
                        }
                    }
                }

                response.StatusCode = 200;
                response.Data = cars;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }

            return response;
        }

    }
}