using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class RentalService : IRentalRepository
    {
        private readonly SqlConnection conn;
        private readonly INotificationRepository _notificationRepo;
        private readonly IEmailService _emailService;
        private readonly IPaymentRepository _paymentRepo;

        public RentalService(IConfiguration config, INotificationRepository notificationRepo, IPaymentRepository paymentRepo, IEmailService emailService)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
            _notificationRepo = notificationRepo;
            _emailService = emailService;
            _paymentRepo = paymentRepo;
        }

        public async Task<ServiceResponse<Rental>> CreateRental(RentalRequest request)
        {
            var response = new ServiceResponse<Rental>();

            try
            {
                DateTime today = DateTime.Today;

                if (request.StartDate.Date < today)
                {
                    response.StatusCode = 400;
                    response.Message = "Start Date cannot be in the past.";
                    return response;
                }

                if (request.EndDate.Date < request.StartDate.Date)
                {
                    response.StatusCode = 400;
                    response.Message = "End Date cannot be earlier than Start Date";
                    return response;
                }

                await conn.OpenAsync();

                string checkQuery = @"
                    SELECT COUNT(*) FROM Rentals 
                    WHERE CarID = @CarID 
                    AND Status NOT IN ('Cancelled', 'Returned', 'Rejected') 
                    AND (
                        -- Kini mag-check kung ang bag-ong booking (request) nasulod ba sa 
                        -- Expanded Range (DB Date +/- 3 days buffer)
                        DATEADD(day, -3, StartDate) <= @EndDate 
                        AND 
                        DATEADD(day, 3, EndDate) >= @StartDate
                    )";

                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@CarID", request.CarID);
                    checkCmd.Parameters.AddWithValue("@StartDate", request.StartDate.Date);
                    checkCmd.Parameters.AddWithValue("@EndDate", request.EndDate.Date);

                    int count = (int)await checkCmd.ExecuteScalarAsync();

                    if (count > 0)
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

                int totalDays = (request.EndDate - request.StartDate).Days;

                if (totalDays <= 0)
                {
                    totalDays = 1;
                }

                decimal totalPrice = totalDays * pricepPerDay;

                string insertQuery = @"INSERT INTO Rentals (UserID, CarID, StartDate, EndDate, TotalPrice, FullName, ContactNumber, PickupLocation)
                                     OUTPUT INSERTED.*
                                     VALUES (@UserID, @CarID, @StartDate, @EndDate, @TotalPrice, @FullName, @ContactNumber, @PickupLocation)";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", request.UserID);
                    cmd.Parameters.AddWithValue("@CarID", request.CarID);
                    cmd.Parameters.AddWithValue("@StartDate", request.StartDate);
                    cmd.Parameters.AddWithValue("@EndDate", request.EndDate);
                    cmd.Parameters.AddWithValue("@TotalPrice", totalPrice);

                    cmd.Parameters.AddWithValue("@FullName", (object)request.FullName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ContactNumber", (object)request.ContactNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@PickupLocation", (object)request.PickupLocation ?? DBNull.Value);

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
                                FullName = reader["FullName"]?.ToString(),
                                ContactNumber = reader["ContactNumber"]?.ToString(),
                                PickupLocation = reader["PickupLocation"]?.ToString(),
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
                   CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
                   (SELECT TOP 1 PaymentID FROM Payment p WHERE p.RentalID = r.RentalID ORDER BY p.CreatedAt DESC) AS PaymentID
            FROM Rentals r
            LEFT JOIN Users u ON r.UserID = u.Id
            WHERE r.Status != 'Pending'
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
                        UserName = reader["UserName"] != DBNull.Value ? reader["UserName"].ToString() : "Unknown User",
                        CarID = Convert.ToInt32(reader["CarID"]),
                        StartDate = Convert.ToDateTime(reader["StartDate"]),
                        EndDate = Convert.ToDateTime(reader["EndDate"]),
                        TotalDays = Convert.ToInt32(reader["TotalDays"]),
                        TotalPrice = Convert.ToDecimal(reader["TotalPrice"]),
                        Status = reader["Status"].ToString(),
                        FullName = reader["FullName"] != DBNull.Value ? reader["FullName"].ToString() : null,
                        ContactNumber = reader["ContactNumber"] != DBNull.Value ? reader["ContactNumber"].ToString() : null,
                        PickupLocation = reader["PickupLocation"] != DBNull.Value ? reader["PickupLocation"].ToString() : null,
                        CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                        UpdatedAt = reader["UpdatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["UpdatedAt"]) : DateTime.MinValue,

                        PaymentID = reader["PaymentID"] != DBNull.Value ? Convert.ToInt32(reader["PaymentID"]) : (int?)null,
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
                            FullName = reader["FullName"] != DBNull.Value ? reader["FullName"].ToString() : null,
                            ContactNumber = reader["ContactNumber"] != DBNull.Value ? reader["ContactNumber"].ToString() : null,
                            PickupLocation = reader["PickupLocation"] != DBNull.Value ? reader["PickupLocation"].ToString() : null,
                            CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                            UpdatedAt = Convert.ToDateTime(reader["UpdatedAt"]),
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

        public async Task<ServiceResponse<bool>> ReviewBooking(int rentalId, string newStatus, string reason = null)
        {
            var response = new ServiceResponse<bool>();

            try
            {
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                string query = @"    
            UPDATE Rentals 
            SET Status = @Status, UpdatedAt = GETDATE()    
            OUTPUT INSERTED.UserID    
            WHERE RentalID = @RentalID";

                int? userId = null;
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", newStatus);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);

                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null) userId = Convert.ToInt32(result);
                }

                if (userId.HasValue)
                {
                    string userEmail = "";
                    string emailQuery = "SELECT Email FROM Users WHERE Id = @UserID";
                    using (SqlCommand emailCmd = new SqlCommand(emailQuery, conn))
                    {
                        emailCmd.Parameters.AddWithValue("@UserID", userId.Value);
                        var emailResult = await emailCmd.ExecuteScalarAsync();
                        if (emailResult != null && emailResult != DBNull.Value)
                        {
                            userEmail = emailResult.ToString();
                        }
                    }

                    string notificationMessage = string.Empty;
                    string emailSubject = "Rental Update";

                    if (newStatus == "Approved")
                    {
                        decimal remainingBalance = 0;

                        string balanceQuery = @"SELECT TOP 1 RemainingBalance 
                                        FROM Payment 
                                        WHERE RentalID = @RentalID 
                                        ORDER BY CreatedAt DESC";

                        using (SqlCommand balanceCmd = new SqlCommand(balanceQuery, conn))
                        {
                            balanceCmd.Parameters.AddWithValue("@RentalID", rentalId);
                            var balanceResult = await balanceCmd.ExecuteScalarAsync();

                            if (balanceResult != null && balanceResult != DBNull.Value)
                            {
                                remainingBalance = Convert.ToDecimal(balanceResult);
                            }
                        }

                        notificationMessage = $"Your rental has been approved. Please pay the remaining balance of PHP {remainingBalance:N2}. Please Go to our Website to Pay it";

                        emailSubject = "Rental Approved - Action Required"; 
                    }
                    else if (newStatus == "Refund Required")
                    {
                        
                        notificationMessage = "Your rental request has been rejected and your down payment will be refunded.";
                        emailSubject = "Rental Update - Refund Processing"; 

                        
                        if (!string.IsNullOrEmpty(reason))
                        {
                            notificationMessage += $" Reason: {reason}";
                        }
                    }

                    if (!string.IsNullOrEmpty(notificationMessage))
                    {
                       
                        await _notificationRepo.CreateNotification(userId.Value, rentalId, notificationMessage);

                        
                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await _emailService.SendEmailAsync(userEmail, emailSubject, notificationMessage);
                        }
                    }

                    response.StatusCode = 200;
                    response.Data = true;
                    response.Message = $"Booking successfully marked as {newStatus}.";
                }
                else
                {
                    response.StatusCode = 404;
                    response.Message = "Rental not found";
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

        public async Task<ServiceResponse<bool>> CheckAndMarkOverdueRentals()
        {
            var response = new ServiceResponse<bool>();
            try
            {
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                // Find Confirmed rentals that are past EndDate
                const string findQuery = @"
                    SELECT r.RentalID, r.UserID, u.Email 
                    FROM Rentals r 
                    INNER JOIN Users u ON r.UserID = u.Id 
                    WHERE r.Status = 'Delivered' AND r.EndDate < GETDATE()";

                var overdueRentals = new List<(int RentalId, int UserId, string Email)>();

                using (var cmd = new SqlCommand(findQuery, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        overdueRentals.Add((
                            Convert.ToInt32(reader["RentalID"]),
                            Convert.ToInt32(reader["UserID"]),
                            reader["Email"].ToString()
                        ));
                    }
                }

                if (overdueRentals.Count > 0)
                {
                    foreach (var rental in overdueRentals)
                    {
                        // Update Status to Overdue
                        const string updateQuery = "UPDATE Rentals SET Status = 'Overdue', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                        using (var cmd = new SqlCommand(updateQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@RentalID", rental.RentalId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Send Notifications
                        string message = "Your rental is overdue. Please return the car immediately to avoid penalties.";
                        await _notificationRepo.CreateNotification(rental.UserId, rental.RentalId, message);
                        await _emailService.SendEmailAsync(rental.Email, "Rental Overdue Notice", message);
                    }
                }

                response.StatusCode = 200;
                response.Data = true;
                response.Message = $"{overdueRentals.Count} rentals marked as overdue.";
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

        public async Task<ServiceResponse<object>> ReturnCar(int rentalId)
        {
            var response = new ServiceResponse<object>();
            try
            {
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                DateTime endDate = DateTime.MinValue;
                string status = "";
                int userId = 0;
                string userEmail = "";
                decimal pricePerDay = 0;

                // 1. Get Rental details
                const string rentalQuery = @"
                    SELECT r.EndDate, r.Status, r.UserID, u.Email, c.PricePerDay
                    FROM Rentals r 
                    INNER JOIN Users u ON r.UserID = u.Id 
                    INNER JOIN Cars c ON r.CarID = c.CarID
                    WHERE r.RentalID = @RentalID";

                using (var cmd = new SqlCommand(rentalQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        endDate = Convert.ToDateTime(reader["EndDate"]);
                        status = reader["Status"].ToString();
                        userId = Convert.ToInt32(reader["UserID"]);
                        userEmail = reader["Email"].ToString();
                        pricePerDay = Convert.ToDecimal(reader["PricePerDay"]);
                    }
                    else
                    {
                        response.StatusCode = 404; response.Message = "Rental not found."; return response;
                    }
                }

                if (status == "Returned")
                {
                    response.StatusCode = 400; response.Message = "Car is already returned."; return response;
                }

                //Calculate Penalty
                int overdueDays = (DateTime.Now.Date - endDate.Date).Days;
                decimal penaltyFee = overdueDays > 0 ? overdueDays * pricePerDay : 0;
                string checkoutUrl = null;

                //Update DB
                const string updateQuery = "UPDATE Rentals SET Status = 'Returned', PenaltyFee = @PenaltyFee, UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                using (var cmd = new SqlCommand(updateQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@PenaltyFee", penaltyFee);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                    await cmd.ExecuteNonQueryAsync();
                }

                //Handle Notifications and Payments
                string subject, body;
                if (penaltyFee > 0)
                {
                    // Create Penalty Payment in PayMongo
                    var paymentRes = await _paymentRepo.CreatePenaltyPayment(rentalId, penaltyFee);
                    if (paymentRes.StatusCode == 200) checkoutUrl = paymentRes.Data.CheckoutUrl;

                    subject = "Late Return Penalty";
                    body = $"You returned the car late. Please pay the penalty fee of PHP {penaltyFee:N2}. <br/> Pay here: <a href='{checkoutUrl}'>Checkout Link</a>";

                    await _notificationRepo.CreateNotification(userId, rentalId, $"You returned the car late. Please pay the penalty fee of PHP {penaltyFee:N2}.");
                }
                else
                {
                    subject = "Rental Completed";
                    body = "Thank you for returning the car on time.";
                    await _notificationRepo.CreateNotification(userId, rentalId, "Thank you for returning the car on time.");
                }

                await _emailService.SendEmailAsync(userEmail, subject, body);

                response.StatusCode = 200;
                response.Data = new { PenaltyFee = penaltyFee, CheckoutUrl = checkoutUrl };
                response.Message = "Car returned successfully.";
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

        public async Task<ServiceResponse<bool>> RequestCancellation(int rentalId, int userId)
        {
            var response = new ServiceResponse<bool>();
            try
            {

                
                    await conn.OpenAsync();

                    // Check if rental is eligible for cancellation
                    string checkQuery = "SELECT Status FROM Rentals WHERE RentalID = @RentalID AND UserID = @UserID";
                    using var checkCmd = new SqlCommand(checkQuery, conn);
                    checkCmd.Parameters.AddWithValue("@RentalID", rentalId);
                    checkCmd.Parameters.AddWithValue("@UserID", userId);

                    var statusResult = await checkCmd.ExecuteScalarAsync();
                    if (statusResult == null)
                    {
                        response.StatusCode = 404;
                        response.Message = "Rental not found.";
                        return response;
                    }

                    string currentStatus = statusResult.ToString();
                    if (currentStatus == "Completed" || currentStatus == "Returned" || currentStatus == "Cancelled")
                    {
                        response.StatusCode = 400;
                        response.Message = $"Cannot cancel a rental that is already {currentStatus}.";
                        return response;
                    }

                    //Update Status to "Cancellation Requested"
                    string updateQuery = "UPDATE Rentals SET Status = 'Cancellation Requested', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                    using var updateCmd = new SqlCommand(updateQuery, conn);
                    updateCmd.Parameters.AddWithValue("@RentalID", rentalId);
                    await updateCmd.ExecuteNonQueryAsync();

                    await _notificationRepo.CreateNotification(1, rentalId, $"User requested cancellation for Rental #{rentalId}. Review required.");

                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Cancellation request submitted successfully.";
                
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Error: {ex.Message}";
            }
            finally{
                await conn.CloseAsync();
            }
            return response;
        }

        public async Task<ServiceResponse<bool>> ReviewCancellation(int rentalId, string action)
        {
            var response = new ServiceResponse<bool>();

            if (action == "Approved")
            {
                // Offload to PaymentService to handle the complex 90% refund iteration
                return await _paymentRepo.ProcessCancellationRefunds(rentalId);
            }
            else if (action == "Rejected")
            {
                    await conn.OpenAsync();
                    // Revert back to Confirmed
                    string updateQuery = "UPDATE Rentals SET Status = 'Confirmed', UpdatedAt = GETDATE() OUTPUT INSERTED.UserID WHERE RentalID = @RentalID";
                    using var cmd = new SqlCommand(updateQuery, conn);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                    var result = await cmd.ExecuteScalarAsync();

                    if (result != null)
                    {
                        int userId = Convert.ToInt32(result);

                    string emailQuery = "SELECT Email FROM Users WHERE Id = @UserID";
                    using var emailCmd = new SqlCommand(emailQuery, conn);
                    emailCmd.Parameters.AddWithValue("@UserID", userId);
                    var emailResult = await emailCmd.ExecuteScalarAsync();

                    string userEmail = emailResult?.ToString();

                    string rejectMsg = "Your cancellation request has been rejected. Your booking remains confirmed.";
                        await _notificationRepo.CreateNotification(userId, rentalId, rejectMsg);

                    if (!string.IsNullOrEmpty(userEmail))
                    {
                         await _emailService.SendEmailAsync(userEmail, "Cancellation Request Rejected", rejectMsg);
                    }
                    }

                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Cancellation request rejected. Rental reverted to Confirmed.";
                    return response;
                }
            

            response.StatusCode = 400;
            response.Message = "Invalid action.";
            return response;
        }

        public async Task<ServiceResponse<List<Rental>>> GetRentalsByUserId(int userId)
        {
            var response = new ServiceResponse<List<Rental>>();
            var list = new List<Rental>();

            try
            {
                // Check if connection is already open, if not, open it
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                string query = @"
                    SELECT r.*, 
                           CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
                           c.CarName 
                    FROM Rentals r
                    LEFT JOIN Users u ON r.UserID = u.Id
                    LEFT JOIN Cars c ON r.CarID = c.CarID
                    WHERE r.UserID = @UserID
                    ORDER BY r.CreatedAt DESC";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new Rental
                            {
                                RentalID = Convert.ToInt32(reader["RentalID"]),
                                UserID = Convert.ToInt32(reader["UserID"]),
                                UserName = reader["UserName"] != DBNull.Value ? reader["UserName"].ToString() : "Unknown User",
                                CarID = Convert.ToInt32(reader["CarID"]),
                                StartDate = Convert.ToDateTime(reader["StartDate"]),
                                EndDate = Convert.ToDateTime(reader["EndDate"]),
                                TotalDays = Convert.ToInt32(reader["TotalDays"]),
                                TotalPrice = Convert.ToDecimal(reader["TotalPrice"]),
                                Status = reader["Status"].ToString(),
                                FullName = reader["FullName"] != DBNull.Value ? reader["FullName"].ToString() : null,
                                ContactNumber = reader["ContactNumber"] != DBNull.Value ? reader["ContactNumber"].ToString() : null,
                                PickupLocation = reader["PickupLocation"] != DBNull.Value ? reader["PickupLocation"].ToString() : null,
                                CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                                UpdatedAt = reader["UpdatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["UpdatedAt"]) : DateTime.MinValue,
                                CarName = reader["CarName"] != DBNull.Value ? reader["CarName"].ToString() : "Unknown Car"
                            });
                        }
                    }
                }

                response.Data = list;
                response.StatusCode = 200;
                response.Message = "User rentals retrieved successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Error: {ex.Message}";
            }
            finally
            {
                if (conn.State == System.Data.ConnectionState.Open)
                {
                    await conn.CloseAsync();
                }
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> UpdateRentalStatus(int rentalId, string newStatus)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                // Update the rental status and get the UserID for notifications
                string query = @"
                    UPDATE Rentals 
                    SET Status = @Status, UpdatedAt = GETDATE() 
                    OUTPUT INSERTED.UserID 
                    WHERE RentalID = @RentalID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", newStatus);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);

                    var result = await cmd.ExecuteScalarAsync();

                    if (result != null)
                    {
                        int userId = Convert.ToInt32(result);

                        string userEmail = "";
                        string emailQuery = "SELECT Email FROM Users WHERE Id = @UserID";
                        using (SqlCommand emailCmd = new SqlCommand(emailQuery, conn))
                        {
                            emailCmd.Parameters.AddWithValue("@UserID", userId);
                            var emailResult = await emailCmd.ExecuteScalarAsync();
                            if (emailResult != null && emailResult != DBNull.Value)
                            {
                                userEmail = emailResult.ToString();
                            }
                        }

                        // Craft notification message based on new status
                        string message = newStatus == "On the Way"
                            ? "🚗 Good news! Your rented car is now on the way to your location."
                            : "✅ Your rented car has been delivered. Please note that cancellation is no longer allowed.";
                        string subject = $"Car Rental Update: {newStatus}";

                        // Send In-App Notification
                        await _notificationRepo.CreateNotification(userId, rentalId, message);

                        // Send Email Notification
                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await _emailService.SendEmailAsync(userEmail, subject, message);
                        }

                        response.Data = true;
                        response.StatusCode = 200;
                        response.Message = $"Status successfully updated to {newStatus}";
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Message = "Rental not found";
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
                if (conn.State == System.Data.ConnectionState.Open)
                {
                    await conn.CloseAsync();
                }
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> RequestReturn(int rentalId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                await conn.OpenAsync();
                string query = "UPDATE Rentals SET Status = 'Return Requested', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@RentalID", rentalId);
                await cmd.ExecuteNonQueryAsync();

                // Optional: Mo notify sa Admin
                await _notificationRepo.CreateNotification(1, rentalId, $"User requested to return Rental #{rentalId}. Review needed.");

                response.Data = true;
                response.StatusCode = 200;
                response.Message = "Return request submitted.";
            }
            catch (Exception ex) { response.StatusCode = 500; response.Message = ex.Message; }
            finally { await conn.CloseAsync(); }

            return response;
        }

        public async Task<ServiceResponse<object>> ReviewReturnRequest(int rentalId, string action, string reason = null)
        {
            if (action == "Approved")
            {
                return await ReturnCar(rentalId);   
            }
            else if (action == "Rejected")
            {
                var response = new ServiceResponse<object>();
                try
                {
                    await conn.OpenAsync();
                    string query = "UPDATE Rentals SET Status = 'Delivered', UpdatedAt = GETDATE() OUTPUT INSERTED.UserID WHERE RentalID = @RentalID";
                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                    var result = await cmd.ExecuteScalarAsync();

                    if (result != null)
                    {
                        int userId = Convert.ToInt32(result);

                        string emailQuery = "SELECT Email FROM Users WHERE Id = @UserID";
                        using var emailCmd = new SqlCommand(emailQuery, conn);
                        emailCmd.Parameters.AddWithValue("@UserID", userId);
                        string userEmail = (await emailCmd.ExecuteScalarAsync())?.ToString();

                        string msg = $"Your return request was rejected. Reason: {reason}";
                        await _notificationRepo.CreateNotification(userId, rentalId, msg);

                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await _emailService.SendEmailAsync(userEmail, "Return Request Rejected", msg);
                        }
                    }
                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Return rejected. User notified.";
                }
                catch (Exception ex) { response.StatusCode = 500; response.Message = ex.Message; }
                finally { await conn.CloseAsync(); }
                return response;
            }
            return new ServiceResponse<object> { StatusCode = 400, Message = "Invalid action" };
        }
    }
}
