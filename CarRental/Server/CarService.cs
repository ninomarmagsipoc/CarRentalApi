using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class CarService : ICarRepository
    {
        private readonly SqlConnection conn;
        private readonly IWebHostEnvironment _env;

        public CarService(IConfiguration config, IWebHostEnvironment env)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
            _env = env;
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
                LEFT JOIN Favorites f ON c.CarID = f.CarID AND f.UserID = @UserID WHERE IsHidden = 0";

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

        public async Task<ServiceResponse<List<CarBookingDTO>>> GetCarBookings(int carId)
        {
            var response = new ServiceResponse<List<CarBookingDTO>>();
            var bookings = new List<CarBookingDTO>();

            try
            {
                await conn.OpenAsync();

                string query = @"SELECT StartDate, EndDate, Status 
                         FROM Rentals 
                         WHERE CarID = @CarID 
                         AND Status IN ('Approved', 'Rented')";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@CarID", carId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            bookings.Add(new CarBookingDTO
                            {
                                StartDate = Convert.ToDateTime(reader["StartDate"]),
                                EndDate = Convert.ToDateTime(reader["EndDate"]),
                                Status = reader["Status"].ToString()
                            });
                        }
                    }
                }
                response.Data = bookings;
                response.StatusCode = 200;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error fetching bookings: " + ex.Message;
            }
            finally { await conn.CloseAsync(); }

            return response;
        }

        public async Task<ServiceResponse<string>> DeleteCar(int carId)
        {
            var response = new ServiceResponse<string>();
            try
            {
                await conn.OpenAsync();

                // 1. I-CHECK KUNG NAA BAY ACTIVE O APPROVED NGA RENTAL KINI NGA SAKYANAN
                string checkQuery = @"
                    SELECT COUNT(1) 
                    FROM Rentals 
                    WHERE CarID = @CarID AND Status IN ('Approved', 'Rented')";

                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@CarID", carId);
                    int activeRentals = (int)await checkCmd.ExecuteScalarAsync();

                    if (activeRentals > 0)
                    {
                        // Kung naay nag-rent, dili nato ipadayon ang pag-hide
                        response.StatusCode = 400;
                        response.Message = "Dili pwede i-hide kay currently 'Approved' o 'Rented' pa ang sakyanan.";
                        return response;
                    }
                }

                // 2. I-HIDE ANG SAKYANAN IMBES NGA I-DELETE
                // Nag-assume ta nga naa kay column nga 'IsHidden' (o 'IsDeleted') sa imong Cars table
                string updateQuery = "UPDATE Cars SET IsHidden = 1 WHERE CarID = @CarID";

                using (SqlCommand cmd = new SqlCommand(updateQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@CarID", carId);
                    await cmd.ExecuteNonQueryAsync();
                }

                response.Data = "Car successfully hidden.";
                response.StatusCode = 200;
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

        public async Task<ServiceResponse<string>> AddCar(CarRequest request)
        {
            var response = new ServiceResponse<string>();
            try
            {
                string imagePath = "";

                if (request.ImageFile != null && request.ImageFile.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "images");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + request.ImageFile.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await request.ImageFile.CopyToAsync(fileStream);
                    }

                    imagePath = "/images/" + uniqueFileName;
                }

                await conn.OpenAsync();
                string query = @"INSERT INTO Cars (CarName, CarInfo, Seats, PricePerDay, CarImage) 
                         VALUES (@CarName, @CarInfo, @Seats, @PricePerDay, @CarImage)";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@CarName", request.CarName);
                    cmd.Parameters.AddWithValue("@CarInfo", request.CarInfo);
                    cmd.Parameters.AddWithValue("@Seats", request.Seats);
                    cmd.Parameters.AddWithValue("@PricePerDay", request.PricePerDay);
                    cmd.Parameters.AddWithValue("@CarImage", string.IsNullOrEmpty(imagePath) ? DBNull.Value : imagePath);

                    await cmd.ExecuteNonQueryAsync();
                }

                response.Data = "Car added successfully!";
                response.StatusCode = 200;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error adding car: " + ex.Message;
            }
            finally { await conn.CloseAsync(); }
            return response;
        }

        public async Task<ServiceResponse<string>> EditCar(int carId, CarRequest request)
        {
            var response = new ServiceResponse<string>();
            try
            {
                string imagePath = null;

                // Kung naay gi-upload nga bag-ong picture, i-save
                if (request.ImageFile != null && request.ImageFile.Length > 0)
                {
                    string uploadFolder = Path.Combine(_env.WebRootPath, "images");
                    if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + request.ImageFile.FileName;
                    string filePath = Path.Combine(uploadFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await request.ImageFile.CopyToAsync(fileStream);
                    }
                    imagePath = "/images/" + uniqueFileName;
                }

                await conn.OpenAsync();

                // Dynamic SQL: Kung walay bag-ong image, dili i-update ang CarImage column
                string query = @"UPDATE Cars 
                         SET CarName = @CarName, CarInfo = @CarInfo, Seats = @Seats, 
                             PricePerDay = @PricePerDay, MaintenanceMonth = @MaintenanceMonth " +
                                 (imagePath != null ? ", CarImage = @CarImage " : " ") +
                                 "WHERE CarID = @CarID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@CarID", carId);
                    cmd.Parameters.AddWithValue("@CarName", request.CarName);
                    cmd.Parameters.AddWithValue("@CarInfo", request.CarInfo);
                    cmd.Parameters.AddWithValue("@Seats", request.Seats);
                    cmd.Parameters.AddWithValue("@PricePerDay", request.PricePerDay);
                    cmd.Parameters.AddWithValue("@MaintenanceMonth", string.IsNullOrEmpty(request.MaintenanceMonth) ? (object)DBNull.Value : request.MaintenanceMonth);

                    if (imagePath != null)
                        cmd.Parameters.AddWithValue("@CarImage", imagePath);

                    await cmd.ExecuteNonQueryAsync();
                }

                response.Data = "Car updated successfully!";
                response.StatusCode = 200;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error updating car: " + ex.Message;
            }
            finally { await conn.CloseAsync(); }
            return response;
        }

        public async Task<ServiceResponse<string>> RestoreCar(int carId)
        {
            var response = new ServiceResponse<string>();
            try
            {
                await conn.OpenAsync();
                // I-set ang IsHidden balik sa 0 aron mo-gawas pag-usab
                string query = "UPDATE Cars SET IsHidden = 0 WHERE CarID = @CarID";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@CarID", carId);
                    await cmd.ExecuteNonQueryAsync();
                }
                response.Data = "Car restored successfully";
                response.StatusCode = 200;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally { await conn.CloseAsync(); }
            return response;
        }
        public async Task<ServiceResponse<object>> GetArchivedCars()
        {
            var response = new ServiceResponse<object>();
            var cars = new List<object>();

            try
            {
                await conn.OpenAsync();
                // Mokuha lang sa mga sakyanan nga gitago (IsHidden = 1)
                string query = "SELECT * FROM Cars WHERE IsHidden = 1";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        cars.Add(new
                        {
                            CarID = reader["CarID"],
                            CarName = reader["CarName"].ToString(),
                            CarInfo = reader["CarInfo"].ToString(),
                            Seats = Convert.ToInt32(reader["Seats"]),
                            PricePerDay = Convert.ToDecimal(reader["PricePerDay"]),
                            CarImage = reader["CarImage"] == DBNull.Value ? null : reader["CarImage"].ToString(),
                            MaintenanceMonth = reader["MaintenanceMonth"] == DBNull.Value ? null : reader["MaintenanceMonth"].ToString(),
                            IsHidden = true // Hardcoded to true kay archived list man ni
                        });
                    }
                }

                response.Data = cars;
                response.StatusCode = 200;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error fetching archived cars: " + ex.Message;
            }
            finally { await conn.CloseAsync(); }

            return response;
        }
    }
}