using CarRental.IRepository;
using CarRental.Model.Response;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class RentalService : IRentalRepository
    {
        private readonly SqlConnection conn;

        public RentalService(IConfiguration config)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
        }

        public async Task<ServiceResponse<Rental>> CreateRental(RentalRequest request)
        {
            var response = new ServiceResponse<Rental>();

            try 
            {
                if(request.EndDate < request.StartDate)
                {
                    response.StatusCode = 400;
                    response.Message = "End Date cannot be earlier than Start Date";
                    return response;
                }

                await conn.OpenAsync();

                string checkQuery = @"SELECT COUNT(*) FROM Rentals WHERE CarID = @CarID AND Status != 'Cancelled'
                                    AND NOT (@StartDate > @EndDate OR @EndDate < @StartDate)";

                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@CarID", request.CarID);
                    checkCmd.Parameters.AddWithValue("@StartDate", request.StartDate);
                    checkCmd.Parameters.AddWithValue("@EndDate", request.EndDate);

                    int count = (int)await checkCmd.ExecuteScalarAsync();

                    if(count > 0)
                    {
                        response.StatusCode = 400;
                        response.Message = "Car is Already rented for selected dates.";
                        return response;
                    }
                }

                decimal pricepPerDay = 0;
                string priceQuery = "SELECT PricePerDay FROM Cars WHERE CarID = @CarID";

                using (SqlCommand cmd = new SqlCommand(priceQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@CarID", request.CarID);
                    var result = await cmd.ExecuteScalarAsync();

                    if(result == null)
                    {
                        response.StatusCode = 404;
                        response.Message = "Car Not Found";
                        return response;
                    }

                    pricepPerDay = Convert.ToDecimal(result);
                }

                int totalDays = (request.EndDate - request.StartDate).Days + 1;
                decimal totalPrice = totalDays * pricepPerDay;

                string insertQuery = @"INSERT INTO Rentals (UserID, CarID, StartDate, EndDate, TotalPrice)
                                     OUTPUT INSERTED.*
                                     VALUES (@UserID, @CarID, @StartDate, @EndDate, @TotalPrice)";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", request.UserID);
                    cmd.Parameters.AddWithValue("@CarID", request.CarID);
                    cmd.Parameters.AddWithValue("@StartDate", request.StartDate);
                    cmd.Parameters.AddWithValue("@EndDate", request.EndDate);
                    cmd.Parameters.AddWithValue("@TotalPrice", totalPrice);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if(await reader.ReadAsync())
                        {
                            response.Data = new Rental
                            {
                                RentalID = Convert.ToInt32(reader["RentalID"]),
                                UserID = Convert.ToInt32(reader["UserID"]),
                                CarID = Convert.ToInt32(reader["CarID"]),
                                StartDate = Convert.ToDateTime(reader["StartDate"]),
                                EndDate = Convert.ToDateTime(reader["EndDate"]),
                                TotalDays = Convert.ToInt32(reader["TotalDays"]),
                                TotalPrice = Convert.ToDecimal(reader["TotalPrice"]),
                                Status = reader["Status"].ToString(),
                                CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                                UpdatedAt = Convert.ToDateTime(reader["UpdatedAt"])
                            };

                            response.Message = "Rental Created SuccessFully";
                        }
                    }
                }

            }
            catch(Exception ex) 
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }

            return response;
        }

        public async Task<ServiceResponse<List<Rental>>> GetRentals()
        {
            var response = new ServiceResponse<List<Rental>>();
            var list = new List<Rental>();

            await conn.OpenAsync();

            string query = @"
    SELECT r.*, 
           CONCAT(u.FirstName, ' ', u.LastName) AS UserName 
    FROM Rentals r
    LEFT JOIN Users u ON r.UserID = u.Id
    ORDER BY r.CreatedAt DESC";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    list.Add(new Rental
                    {
                        RentalID = Convert.ToInt32(reader["RentalID"]),
                        UserID = Convert.ToInt32(reader["UserID"]),

                        // 2. This stays exactly the same as before! It just grabs the combined name from SQL.
                        UserName = reader["UserName"] != DBNull.Value ? reader["UserName"].ToString() : "Unknown User",

                        CarID = Convert.ToInt32(reader["CarID"]),
                        StartDate = Convert.ToDateTime(reader["StartDate"]),
                        EndDate = Convert.ToDateTime(reader["EndDate"]),
                        TotalDays = Convert.ToInt32(reader["TotalDays"]),
                        TotalPrice = Convert.ToDecimal(reader["TotalPrice"]),
                        Status = reader["Status"].ToString(),
                        CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                        UpdatedAt = reader["UpdatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["UpdatedAt"]) : DateTime.MinValue
                    });
                }
            }

            response.Data = list;
            return response;
        }
        public async Task<ServiceResponse<Rental>> GetRentalById(int id)
        {
            var response = new ServiceResponse<Rental>();

            await conn.OpenAsync();

            string query = "SELECT * FROM Rentals WHERE RentalID = @Id";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);

                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    if(await reader.ReadAsync())
                    {
                        response.StatusCode = 200;
                        response.Data = new Rental 
                        {
                            RentalID = Convert.ToInt32(reader["RentalID"]),
                            UserID = Convert.ToInt32(reader["UserID"]),
                            CarID = Convert.ToInt32(reader["CarID"]),
                            StartDate = Convert.ToDateTime(reader["StartDate"]),
                            EndDate = Convert.ToDateTime(reader["EndDate"]),
                            TotalDays = Convert.ToInt32(reader["TotalDays"]),
                            TotalPrice = Convert.ToDecimal(reader["TotalPrice"]),
                            Status = reader["Status"].ToString(),
                            CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                            UpdatedAt = Convert.ToDateTime(reader["UpdatedAt"])
                        };
                    }

                    else
                    {
                        response.StatusCode = 404;
                        response.Message = "Rental not found";
                    }
                }
            }
            return response;
        }

        public async Task<ServiceResponse<bool>> ReviewBooking(int rentalId, string newStatus)
        {
            var response = new ServiceResponse<bool>();

            // Only allow specific statuses to prevent database errors
            if (newStatus != "Approved" && newStatus != "Rejected")
            {
                response.StatusCode = 400;
                response.Message = "Invalid status. Must be 'Approved' or 'Rejected'.";
                return response;
            }

            try
            {
                await conn.OpenAsync();

                // Update the Rental status and the UpdatedAt timestamp
                string query = @"
            UPDATE Rentals 
            SET Status = @Status, UpdatedAt = GETDATE()
            WHERE RentalID = @RentalID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", newStatus);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);

                    int rowsAffected = await cmd.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        response.StatusCode = 200;
                        response.Data = true;
                        response.Message = $"Booking successfully marked as {newStatus}.";
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Data = false;
                        response.Message = "Rental ID not found.";
                    }
                }
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
