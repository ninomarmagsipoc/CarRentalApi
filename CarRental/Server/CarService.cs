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

        public async Task<ServiceResponse<object>> GetCars(int? userId = null) 
        {
            var response = new ServiceResponse<object>();
            var cars = new List<object>();

            try
            {
                await conn.OpenAsync();

                string query = @"
                SELECT c.*, 
                       CASE WHEN f.FavoriteID IS NOT NULL THEN 1 ELSE 0 END AS IsFavorite,
                       (SELECT TOP 1 Status FROM Rentals r 
                        WHERE r.CarID = c.CarID 
                        AND r.Status NOT IN ('Cancelled', 'Returned', 'Rejected')
                        AND CAST(GETDATE() AS DATE) BETWEEN CAST(r.StartDate AS DATE) AND CAST(r.EndDate AS DATE)
                       ) AS CurrentStatus
                FROM Cars c 
                LEFT JOIN Favorites f ON c.CarID = f.CarID AND f.UserID = @UserID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);

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
                                CarImage = reader["CarImage"]?.ToString(),
                                IsFavorite = Convert.ToBoolean(reader["IsFavorite"]) 
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
            finally { await conn.CloseAsync(); }

            return response;
        }

        public async Task<ServiceResponse<string>> ToggleFavorite(int userId, int carId)
        {
            var response = new ServiceResponse<string>();
            try
            {
                await conn.OpenAsync();

                string checkQuery = "SELECT COUNT(1) FROM Favorites WHERE UserID = @UID AND CarID = @CID";
                int count = 0;

                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@UID", userId);
                    checkCmd.Parameters.AddWithValue("@CID", carId);
                    count = (int)await checkCmd.ExecuteScalarAsync();
                }

                string actionMessage = "";
                string actionQuery = "";

                if (count > 0)
                {
                    actionQuery = "DELETE FROM Favorites WHERE UserID = @UID AND CarID = @CID";
                    actionMessage = "Removed from favorites";
                }
                else
                {
                    actionQuery = "INSERT INTO Favorites (UserID, CarID) VALUES (@UID, @CID)";
                    actionMessage = "Added to favorites";
                }

                using (SqlCommand actionCmd = new SqlCommand(actionQuery, conn))
                {
                    actionCmd.Parameters.AddWithValue("@UID", userId);
                    actionCmd.Parameters.AddWithValue("@CID", carId);
                    await actionCmd.ExecuteNonQueryAsync(); 
                }

                response.StatusCode = 200;
                response.Data = actionMessage;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error: " + ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }
            return response;
        }
    }
}