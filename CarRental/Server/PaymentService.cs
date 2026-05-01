using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CarRental.Server
{
    public class PaymentService : IPaymentRepository
    {
        private readonly string _connectionString;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _paymongoKey;
        private readonly INotificationRepository _notificationRepo;
        private readonly IEmailService _emailService;
        public PaymentService(IConfiguration config, IHttpClientFactory httpClientFactory, INotificationRepository notificationRepo, IEmailService emailService)
        {
            _connectionString = config.GetConnectionString("CarRental");
            _paymongoKey = config["PayMongo:SecretKey"];
            _httpClientFactory = httpClientFactory;
            _notificationRepo = notificationRepo;
            _emailService = emailService;
        }

        public async Task<ServiceResponse<PaymentResponse>> CreatePayment(PaymentRequest request)
        {
            var response = new ServiceResponse<PaymentResponse>();

            try
            {
                // Fetch rental total price
                decimal totalPrice = 0;
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string query = "SELECT TotalPrice FROM Rentals WHERE RentalID = @RentalID";
                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@RentalID", request.RentalID);

                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null) return ErrorResponse(response, 404, "Rental not found.");

                    totalPrice = Convert.ToDecimal(result);
                }

                // Calculations
                decimal downPayment = totalPrice * 0.5m;
                decimal remainingAmount = totalPrice - downPayment;

                // Create PayMongo Link
                string description = $"Downpayment for Rental #{request.RentalID}";
                var (success, checkoutUrl, reference, error) = await CreatePayMongoLink(downPayment, request.RentalID, description, request.SuccessUrl, request.CancelUrl);

                if (!success) return ErrorResponse(response, 500, error);

                // Save to DB 
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string insertQuery = @"
                        INSERT INTO Payment (RentalID, UserID, Amount, RemainingBalance, PaymentMethod, PaymentType, PaymentStatus, PayMongoRef)
                        VALUES (@RentalID, @UserID, @Amount, @Remaining, @Method, 'Partial', 'Pending', @Ref)";

                    using var cmd = new SqlCommand(insertQuery, conn);
                    cmd.Parameters.AddWithValue("@RentalID", request.RentalID);
                    cmd.Parameters.AddWithValue("@UserID", request.UserID);
                    cmd.Parameters.AddWithValue("@Amount", downPayment);
                    cmd.Parameters.AddWithValue("@Remaining", remainingAmount);
                    cmd.Parameters.AddWithValue("@Method", request.PaymentMethod);
                    cmd.Parameters.AddWithValue("@Ref", reference);

                    await cmd.ExecuteNonQueryAsync();
                }

                response.Data = new PaymentResponse { CheckoutUrl = checkoutUrl, Reference = reference, Amount = downPayment };
                response.StatusCode = 200;
                response.Message = "Payment link generated.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Critical Error: {ex.Message}";
            }

            return response;
        }

        private async Task<(bool Success, string CheckoutUrl, string Reference, string ErrorMessage)> CreatePayMongoLink(
                   decimal amount,
                   int rentalId,
                   string description,
                   string successUrl, 
                   string cancelUrl) 
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_paymongoKey}:"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                var payload = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            // Explicitly set testable payment methods
                            payment_method_types = new[] { "gcash", "paymaya", "card" },

                            // Checkout API uses line_items instead of a single amount
                            line_items = new[]
                            {
                        new
                        {
                            currency = "PHP",
                            amount = (int)(amount * 100), // Convert to cents
                            name = description,
                            quantity = 1
                        }
                    },

                            success_url = successUrl,
                            cancel_url = cancelUrl,

                            reference_number = rentalId.ToString()
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                // Hit the Checkout Sessions endpoint instead of Links
                var res = await client.PostAsync("https://api.paymongo.com/v1/checkout_sessions", content);
                var body = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode) return (false, null, null, $"PayMongo: {body}");

                using var json = JsonDocument.Parse(body);
                var data = json.RootElement.GetProperty("data");
                var attr = data.GetProperty("attributes");

                // Returns the exact same variables so your original code doesn't break
                return (true, attr.GetProperty("checkout_url").GetString(), data.GetProperty("id").GetString(), null);
            }
            catch (Exception ex)
            {
                return (false, null, null, ex.Message);
            }
        }

        public async Task<ServiceResponse<bool>> VerifyPayment(string payMongoReference)
        {
            var response = new ServiceResponse<bool>();

            try
            {
                var client = _httpClientFactory.CreateClient();
                var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_paymongoKey}:"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                var payMongoRes = await client.GetAsync($"https://api.paymongo.com/v1/checkout_sessions/{payMongoReference}");
                var body = await payMongoRes.Content.ReadAsStringAsync();

                if (!payMongoRes.IsSuccessStatusCode) return ErrorResponse(response, 400, "Could not verify with PayMongo.");

                using var json = JsonDocument.Parse(body);
                var attributes = json.RootElement.GetProperty("data").GetProperty("attributes");
                var payments = attributes.GetProperty("payments");

                bool isPaid = payments.GetArrayLength() > 0 && payments[0].GetProperty("attributes").GetProperty("status").GetString() == "paid";
                if (!isPaid) return ErrorResponse(response, 400, "Payment is not completed.");

                int rentalId = Convert.ToInt32(attributes.GetProperty("reference_number").GetString());
                decimal amountPaid = payments[0].GetProperty("attributes").GetProperty("amount").GetInt32() / 100m;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (SqlTransaction transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            string checkQuery = "SELECT PaymentType FROM Payment WHERE PayMongoRef = @Ref";
                            string existingType = null;

                            using (var cmd = new SqlCommand(checkQuery, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Ref", payMongoReference);
                                var dbResult = await cmd.ExecuteScalarAsync();
                                if (dbResult != null && dbResult != DBNull.Value)
                                {
                                    existingType = dbResult.ToString();
                                }
                            }

                            if (existingType == "Partial")
                            {
                                //IT'S THE 50% DOWNPAYMENT
                                string updatePayment = "UPDATE Payment SET PaymentStatus = 'Completed', UpdatedAt = GETDATE(), PaidAt = GETDATE() WHERE PayMongoRef = @Ref";
                                using (var cmd = new SqlCommand(updatePayment, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@Ref", payMongoReference);
                                    await cmd.ExecuteNonQueryAsync();
                                }

                                string updateRental = "UPDATE Rentals SET Status = 'Pending Review', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                                using (var cmd = new SqlCommand(updateRental, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                            else if (existingType == "Full" || existingType == "Balance")
                            {
                                // IT'S THE REMAINING BALANCE OR FULL PAYMENT
                                string updatePayment = "UPDATE Payment SET PaymentStatus = 'Completed', UpdatedAt = GETDATE(), PaidAt = GETDATE() WHERE PayMongoRef = @Ref AND PaymentStatus != 'Completed'";
                                using (var cmd = new SqlCommand(updatePayment, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@Ref", payMongoReference);
                                    await cmd.ExecuteNonQueryAsync();
                                }

                                string updateRental = "UPDATE Rentals SET Status = 'Rented', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                                using (var cmd = new SqlCommand(updateRental, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                            else if (existingType == "Penalty")
                            {
                                string updatePayment = "UPDATE Payment SET PaymentStatus = 'Completed', UpdatedAt = GETDATE(), PaidAt = GETDATE() WHERE PayMongoRef = @Ref AND PaymentStatus != 'Completed'";
                                using (var cmd = new SqlCommand(updatePayment, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@Ref", payMongoReference);
                                    await cmd.ExecuteNonQueryAsync();
                                }

                                // STATUS HIMUONG 'Returned'
                                string updateRental = "UPDATE Rentals SET Status = 'Returned', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                                using (var cmd = new SqlCommand(updateRental, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                                    await cmd.ExecuteNonQueryAsync();
                                }

                                int userId = 0;
                                string getUserIdQuery = "SELECT UserID FROM Rentals WHERE RentalID = @RentalID";
                                using (var cmd = new SqlCommand(getUserIdQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                                    var uIdResult = await cmd.ExecuteScalarAsync();
                                    if (uIdResult != null) userId = Convert.ToInt32(uIdResult);
                                }

                                string penaltyMsg = "Penalty fee paid successfully. Your car rental is now officially marked as Returned. Thank you!";
                                await _notificationRepo.CreateNotification(userId, rentalId, penaltyMsg);
                            }
                            else
                            {
                                //IT'S THE REMAINING BALANCE (No Pending row existed)
                                string getUserIdQuery = "SELECT UserID FROM Rentals WHERE RentalID = @RentalID";
                                int userId = 0;
                                using (var cmd = new SqlCommand(getUserIdQuery, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                                    var uIdResult = await cmd.ExecuteScalarAsync();
                                    if (uIdResult != null) userId = Convert.ToInt32(uIdResult);
                                }

                                //Added "IF NOT EXISTS" to block duplicate inserts
                                string insertPayment = @"
                                    INSERT INTO Payment (RentalID, UserID, Amount, RemainingBalance, PaymentMethod, PaymentType, PaymentStatus, PayMongoRef, CreatedAt)
                                    SELECT @RentalID, @UserID, @Amount, 0, 'PayMongo', 'Full', 'Completed', @Ref, GETDATE()
                                    WHERE NOT EXISTS (
                                        SELECT 1 FROM Payment WITH (UPDLOCK, HOLDLOCK) 
                                        WHERE PayMongoRef = @Ref AND PaymentStatus = 'Completed'
                                    )";

                                using (var cmd = new SqlCommand(insertPayment, conn, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                                    cmd.Parameters.AddWithValue("@UserID", userId);
                                    cmd.Parameters.AddWithValue("@Amount", amountPaid);
                                    cmd.Parameters.AddWithValue("@Ref", payMongoReference);

                                    int rowsAffected = await cmd.ExecuteNonQueryAsync();

                                    if (rowsAffected > 0)
                                    {
                                        string updateRental = "UPDATE Rentals SET Status = 'Rented', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                                        using (var updateCmd = new SqlCommand(updateRental, conn, transaction))
                                        {
                                            updateCmd.Parameters.AddWithValue("@RentalID", rentalId);
                                            await updateCmd.ExecuteNonQueryAsync();
                                        }

                                        await _notificationRepo.CreateNotification(userId, rentalId, "Your rental is Rented. Thanks for using our website!");
                                    }
                                }
                            }

                            await transaction.CommitAsync();
                            response.Data = true;
                            response.StatusCode = 200;

                            if (existingType == "Partial")
                                response.Message = "Payment Successful! Your booking is now Pending Review.";
                            else if (existingType == "Penalty")
                                response.Message = "Penalty Paid! Your car is now successfully marked as Returned.";
                            else
                                response.Message = "Payment Complete! Your rental is now Rented.";
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            throw new Exception("Database error: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return ErrorResponse(response, 500, $"Error: {ex.Message}");
            }
            return response;
        }
        private ServiceResponse<T> ErrorResponse<T>(ServiceResponse<T> res, int code, string msg)
        {
            res.StatusCode = code;
            res.Message = msg;
            return res;
        }

        public async Task<ServiceResponse<PaymentResponse>> CreateBalancePayment(int rentalId, string successUrl, string cancelUrl)
        {
            var response = new ServiceResponse<PaymentResponse>();
            try
            {
                string rentalStatus = "";
                decimal remainingBalance = 0;
                int userId = 0;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Check current status and get UserID
                    const string statusQuery = "SELECT Status, UserID FROM Rentals WHERE RentalID = @RentalID";
                    using (var statusCmd = new SqlCommand(statusQuery, conn))
                    {
                        statusCmd.Parameters.AddWithValue("@RentalID", rentalId);
                        using (var reader = await statusCmd.ExecuteReaderAsync())
                        {
                            if (reader.Read())
                            {
                                rentalStatus = reader["Status"].ToString()!;
                                userId = Convert.ToInt32(reader["UserID"]);
                            }
                        }
                    }

                    if (rentalStatus == "Expired" || rentalStatus == "Refunded" || rentalStatus == "Cancelled")
                    {
                        response.StatusCode = 400;
                        response.Message = $"Cannot process payment. This rental has already been {rentalStatus.ToLower()}.";
                        return response;
                    }

                    // Check the remaining balance
                    const string balanceQuery = @"SELECT TOP 1 RemainingBalance FROM Payment WHERE RentalID = @RentalID AND PaymentStatus = 'Completed' ORDER BY CreatedAt DESC";
                    using (var balanceCmd = new SqlCommand(balanceQuery, conn))
                    {
                        balanceCmd.Parameters.AddWithValue("@RentalID", rentalId);
                        var balanceResult = await balanceCmd.ExecuteScalarAsync();
                        if (balanceResult != null && balanceResult != DBNull.Value)
                        {
                            remainingBalance = Convert.ToDecimal(balanceResult);
                        }
                    }
                }

                if (remainingBalance <= 0)
                {
                    response.StatusCode = 400;
                    response.Message = "Payment Complete! You have already paid the remaining balance for this car.";
                    return response;
                }

                // Create PayMongo Link
                string description = $"Remaining Balance for Rental #{rentalId}";
                var (success, checkoutUrl, reference, error) = await CreatePayMongoLink(remainingBalance, rentalId, description, successUrl, cancelUrl);

                if (!success)
                {
                    response.StatusCode = 500;
                    response.Message = error;
                    return response;
                }

                // 🟢 SEND NOTIFICATION WITH THE LINK 🟢
                // Gi-butangan nato og tag nga [PAY_ONLINE_LINK] para sayon basahon sa React
                string notifMessage = $"Please pay your remaining balance of PHP {remainingBalance}.[PAY_ONLINE_LINK]{checkoutUrl}[REF]{reference}";
                await _notificationRepo.CreateNotification(userId, rentalId, notifMessage);

                response.Data = new PaymentResponse { CheckoutUrl = checkoutUrl, Reference = reference, Amount = remainingBalance };
                response.StatusCode = 200;
                response.Message = "Balance checkout link generated and sent to user.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Error: {ex.Message}";
            }
            return response;
        }
        public async Task<ServiceResponse<bool>> RefundPayment(int paymentId, string reason)    
        {
            var response = new ServiceResponse<bool>();

            try
            {
                //Fetch Payment and Rental Details from DB
                string payMongoRef = null;
                decimal amount = 0;
                int userId = 0;
                int rentalId = 0;
                string userEmail = "";

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string query = @"
                SELECT p.PayMongoRef, p.Amount, p.UserID, p.RentalID, p.PaymentStatus, u.Email 
                FROM Payment p
                INNER JOIN Users u ON p.UserID = u.Id
                WHERE p.PaymentID = @ID";

                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@ID", paymentId);

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (reader["PaymentStatus"].ToString() == "Refunded")
                            return ErrorResponse(response, 400, "This payment has already been refunded.");

                        payMongoRef = reader["PayMongoRef"].ToString();
                        amount = Convert.ToDecimal(reader["Amount"]);
                        userId = Convert.ToInt32(reader["UserID"]);
                        rentalId = Convert.ToInt32(reader["RentalID"]);
                        userEmail = reader["Email"].ToString();
                    }
                    else return ErrorResponse(response, 404, "Payment record not found.");
                }

                //Get the actual "Payment ID" (pay_xxx) from the Checkout Session (cs_xxx)
                var client = _httpClientFactory.CreateClient();
                var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_paymongoKey}:"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                var sessionRes = await client.GetAsync($"https://api.paymongo.com/v1/checkout_sessions/{payMongoRef}");
                if (!sessionRes.IsSuccessStatusCode) return ErrorResponse(response, 400, "Failed to retrieve payment details from PayMongo.");

                using var sessionDoc = JsonDocument.Parse(await sessionRes.Content.ReadAsStringAsync());
                var paymentsArray = sessionDoc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("payments");

                if (paymentsArray.GetArrayLength() == 0) return ErrorResponse(response, 400, "No successful payment found to refund.");

                string actualPaymentId = paymentsArray[0].GetProperty("id").GetString();

                //Call PayMongo Refund API
                var refundPayload = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            amount = (int)(amount * 100), // Convert to cents
                            payment_id = actualPaymentId,
                            reason = "requested_by_customer"
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(refundPayload), Encoding.UTF8, "application/json");
                var refundRes = await client.PostAsync("https://api.paymongo.com/v1/refunds", content);
                var refundBody = await refundRes.Content.ReadAsStringAsync();

                if (!refundRes.IsSuccessStatusCode) return ErrorResponse(response, 500, $"PayMongo Refund Failed: {refundBody}");

                //Update Database (Payment & Rental Status)
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using var transaction = conn.BeginTransaction();
                    try
                    {
                        const string updateSql = @"
                        UPDATE Payment 
                        SET PaymentStatus = 'Refunded', 
                            RefundReason = @Reason, 
                            RemainingBalance = (SELECT TotalPrice FROM Rentals WHERE RentalID = @RID),
                            UpdatedAt = GETDATE() 
                        WHERE PaymentID = @PID;

                        UPDATE Rentals SET Status = 'Rejected', UpdatedAt = GETDATE() WHERE RentalID = @RID;";

                        using var cmd = new SqlCommand(updateSql, conn, transaction);
                        cmd.Parameters.AddWithValue("@PID", paymentId);
                        cmd.Parameters.AddWithValue("@RID", rentalId);

                        // This handles the custom text reason from your React prompt!
                        cmd.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(reason) ? "No reason provided" : reason);

                        await cmd.ExecuteNonQueryAsync();

                        string notificationMessage = "Your payment has been refunded successfully. Please rent a car again.";

                        //Send Notification
                        await _notificationRepo.CreateNotification(userId, rentalId, notificationMessage);

                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await _emailService.SendEmailAsync(userEmail, "Payment Refunded", notificationMessage);
                        }

                        await transaction.CommitAsync();

                        response.Data = true;
                        response.StatusCode = 200;
                        response.Message = "Refund successful";
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw new Exception("Database update failed: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                return ErrorResponse(response, 500, $"Critical Error: {ex.Message}");
            }

            return response;
        }

        public async Task<ServiceResponse<IEnumerable<PaymentDetailsResponse>>> GetAllPayments()
        {
            var response = new ServiceResponse<IEnumerable<PaymentDetailsResponse>>();
            var payments = new List<PaymentDetailsResponse>();

            using (var conn = new SqlConnection(_connectionString))
            {
                //JOINing Users, Rentals, and Cars to get the actual names
                const string query = @"
                    SELECT 
                        p.*, 
                        CONCAT(u.FirstName, ' ', u.LastName) AS UserName,     -- Change 'Name' if your Users table uses 'FullName'
                        c.CarName AS CarName,      -- Change 'Name' if your Cars table uses 'Model' or 'Brand'
                        r.TotalPrice AS TotalAmount,
                        r.FullName
                    FROM Payment p
                    INNER JOIN Users u ON p.UserID = u.Id
                    INNER JOIN Rentals r ON p.RentalID = r.RentalID
                    INNER JOIN Cars c ON r.CarID = c.CarID
                    ORDER BY p.CreatedAt DESC";

                await conn.OpenAsync();
                using var cmd = new SqlCommand(query, conn);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    payments.Add(MapToPaymentDetails(reader));
                }
            }
            response.Data = payments;
            response.StatusCode = 200;
            response.Message = "All payments retrieved.";
            return response;
        }

        private PaymentDetailsResponse MapToPaymentDetails(SqlDataReader reader)
        {
            string status = reader["PaymentStatus"].ToString()!;

            // 🟢 FIX 1: Gi-check ang RemainingBalance kung NULL ba
            decimal balance = reader["RemainingBalance"] != DBNull.Value ? Convert.ToDecimal(reader["RemainingBalance"]) : 0m;

            string reason = reader["RefundReason"] != DBNull.Value ? reader["RefundReason"].ToString()! : "No reason provided";
            string paymentType = reader["PaymentType"].ToString()!;

            string displayValue = status == "Refunded"
                ? $"Refunded: {reason}"
                : $"₱ {balance:N2}";

            // 🟢 FIX 2 & 3: Gi-check ang TotalAmount ug Amount kung NULL ba
            decimal originalTotal = reader["TotalAmount"] != DBNull.Value ? Convert.ToDecimal(reader["TotalAmount"]) : 0m;
            decimal paidAmount = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0m;

            decimal displayTotalAmount = paymentType == "Penalty" ? paidAmount : originalTotal;

            return new PaymentDetailsResponse
            {
                PaymentID = Convert.ToInt32(reader["PaymentID"]),
                RentalID = Convert.ToInt32(reader["RentalID"]),
                UserID = Convert.ToInt32(reader["UserID"]),

                // NEW MAPPINGS (Gi-butangan pod nakog safe check for strings para sigurado)
                UserName = reader["UserName"] != DBNull.Value ? reader["UserName"].ToString()! : "Unknown User",
                CarName = reader["CarName"] != DBNull.Value ? reader["CarName"].ToString()! : "Unknown Car",
                FullName = reader["FullName"] != DBNull.Value ? reader["FullName"].ToString()! : "Unknown Full Name",
                TotalAmount = displayTotalAmount,

                Amount = paidAmount,
                RemainingBalance = balance,
                PaymentType = paymentType,
                PaymentStatus = status,
                PaymentMethod = reader["PaymentMethod"].ToString()!,
                CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                RefundReason = reason,
                BalanceDisplay = displayValue
            };
        }

        public async Task<ServiceResponse<IEnumerable<PaymentDetailsResponse>>> GetPaymentsByUser(int userId)
        {
            var response = new ServiceResponse<IEnumerable<PaymentDetailsResponse>>();
            var payments = new List<PaymentDetailsResponse>();

            using (var conn = new SqlConnection(_connectionString))
            {
                const string query = "SELECT * FROM Payment WHERE UserID = @UserID ORDER BY CreatedAt DESC";
                await conn.OpenAsync();
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserID", userId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    payments.Add(MapToPaymentDetails(reader));
                }
            }
            response.Data = payments;
            response.Message = $"Payment history for User #{userId} retrieved.";
            return response;
        }

        public async Task<ServiceResponse<BalanceCalculationResponse>> GetRemainingBalance(int rentalId)
        {
            var response = new ServiceResponse<BalanceCalculationResponse>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string query = @"
                     SELECT 
                    (SELECT TotalPrice FROM Rentals WHERE RentalID = @RID) as Total,
                    ISNULL((SELECT SUM(Amount) FROM Payment WHERE RentalID = @RID AND PaymentStatus = 'Completed'), 0) as Paid";

                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@RID", rentalId);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        decimal total = reader.GetDecimal(0);
                        decimal paid = reader.GetDecimal(1);
                        response.Data = new BalanceCalculationResponse
                        {
                            TotalPrice = total,
                            PaidAmount = paid,
                            RemainingBalance = total - paid
                        };
                        response.Message = "Balance calculated.";
                    }
                }
            }
            catch (Exception ex) { return ErrorResponse(response, 500, ex.Message); }
            return response;
        }

        public async Task<ServiceResponse<PaymentResponse>> CreatePenaltyPayment(int rentalId, decimal amount, string successUrl, string cancelUrl)
        {
            var response = new ServiceResponse<PaymentResponse>();

            if (amount < 1)
            {
                return ErrorResponse(response, 400, "Total amount must be at least 1.00 to process payment.");
            }

            try
            {
                //Get UserID associated with the rental
                int userId = 0;
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string getUserIdQuery = "SELECT UserID FROM Rentals WHERE RentalID = @RentalID";
                    using var cmd = new SqlCommand(getUserIdQuery, conn);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null) userId = Convert.ToInt32(result);
                    else return ErrorResponse(response, 404, "Rental not found.");
                }

                //Create PayMongo Link utilizing your existing private method
                string description = $"Penalty Fee for late return - Rental #{rentalId}";
                var (success, checkoutUrl, reference, error) = await CreatePayMongoLink(amount, rentalId, description, successUrl, cancelUrl);

                if (!success) return ErrorResponse(response, 500, error);

                //Save Penalty Payment to DB
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string insertQuery = @"
                INSERT INTO Payment (RentalID, UserID, Amount, RemainingBalance, PaymentMethod, PaymentType, PaymentStatus, PayMongoRef, CreatedAt)
                VALUES (@RentalID, @UserID, @Amount, 0, 'PayMongo', 'Penalty', 'Pending', @Ref, GETDATE())";

                    using var cmd = new SqlCommand(insertQuery, conn);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    cmd.Parameters.AddWithValue("@Amount", amount);
                    cmd.Parameters.AddWithValue("@Ref", reference);

                    await cmd.ExecuteNonQueryAsync();
                }

                response.Data = new PaymentResponse { CheckoutUrl = checkoutUrl, Reference = reference, Amount = amount };
                response.StatusCode = 200;
                response.Message = "Penalty checkout link generated successfully.";
            }
            catch (Exception ex)
            {
                return ErrorResponse(response, 500, $"Error: {ex.Message}");
            }

            return response;
        }
        public async Task<ServiceResponse<bool>> ProcessCancellationRefunds(int rentalId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                var client = _httpClientFactory.CreateClient();
                var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_paymongoKey}:"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    //Fetch all completed payments for this rental
                    string query = "SELECT PaymentID, PayMongoRef, Amount, UserID FROM Payment WHERE RentalID = @RentalID AND PaymentStatus = 'Completed'";
                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@RentalID", rentalId);

                    var paymentsToRefund = new List<(int PaymentId, string Ref, decimal Amount, int UserId)>();

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            paymentsToRefund.Add((
                                Convert.ToInt32(reader["PaymentID"]),
                                reader["PayMongoRef"].ToString(),
                                Convert.ToDecimal(reader["Amount"]),
                                Convert.ToInt32(reader["UserID"])
                            ));
                        }
                    }

                    if (!paymentsToRefund.Any())
                    {
                        response.StatusCode = 400;
                        response.Message = "No completed payments found to refund.";
                        return response;
                    }

                    int userId = paymentsToRefund.First().UserId;
                    decimal totalRefunded = 0;

                    //Loop through each payment and refund 90%
                    foreach (var payment in paymentsToRefund)
                    {
                        decimal refundAmount = payment.Amount * 0.75m; // 90% Refund Rule
                        totalRefunded += refundAmount;

                        // Grab actual pay_xxx ID from cs_xxx
                        var sessionRes = await client.GetAsync($"https://api.paymongo.com/v1/checkout_sessions/{payment.Ref}");
                        if (!sessionRes.IsSuccessStatusCode) continue; // Skip if PayMongo lookup fails

                        using var sessionDoc = JsonDocument.Parse(await sessionRes.Content.ReadAsStringAsync());
                        var paymentsArray = sessionDoc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("payments");
                        if (paymentsArray.GetArrayLength() == 0) continue;

                        string actualPaymentId = paymentsArray[0].GetProperty("id").GetString();

                        // Hit PayMongo Refund API
                        var refundPayload = new
                        {
                            data = new
                            {
                                attributes = new
                                {
                                    amount = (int)(refundAmount * 100), // Convert to cents
                                    payment_id = actualPaymentId,
                                    reason = "requested_by_customer"
                                }
                            }
                        };

                        var content = new StringContent(JsonSerializer.Serialize(refundPayload), Encoding.UTF8, "application/json");
                        var refundRes = await client.PostAsync("https://api.paymongo.com/v1/refunds", content);

                        if (refundRes.IsSuccessStatusCode)
                        {
                            // Update Database for this specific payment
                            string updatePayment = "UPDATE Payment SET PaymentStatus = 'Refunded', RefundReason = '75% Cancellation Refund', UpdatedAt = GETDATE() WHERE PaymentID = @PID";
                            using var updateCmd = new SqlCommand(updatePayment, conn);
                            updateCmd.Parameters.AddWithValue("@PID", payment.PaymentId);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
                    }

                    //Finalize Database & Notifications
                    string updateRental = "UPDATE Rentals SET Status = 'Cancelled', UpdatedAt = GETDATE() WHERE RentalID = @RentalID";
                    using var finalizeCmd = new SqlCommand(updateRental, conn);
                    finalizeCmd.Parameters.AddWithValue("@RentalID", rentalId);
                    await finalizeCmd.ExecuteNonQueryAsync();

                    string emailQuery = "SELECT Email FROM Users WHERE Id = @UserID"; 
                    using var emailCmd = new SqlCommand(emailQuery, conn);
                    emailCmd.Parameters.AddWithValue("@UserID", userId);
                    var emailResult = await emailCmd.ExecuteScalarAsync();

                    string userEmail = emailResult?.ToString();

                    string successMessage = $"Your booking has been cancelled. A 75% refund totaling PHP {totalRefunded:N2} has been processed.";
                    await _notificationRepo.CreateNotification(userId, rentalId, successMessage);

                    if (!string.IsNullOrEmpty(userEmail))
                    {
                        await _emailService.SendEmailAsync(userEmail, "Rental Cancelled - Refund Processed", successMessage);
                    }
                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Cancellation and refunds processed successfully.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Critical Error: {ex.Message}";
            }
            return response;
        }

        public async Task<ServiceResponse<bool>> ProcessCashBalancePayment(CashPaymentRequest request)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                int userId = 0;
                decimal balanceToPay = 0; // 🟢 Maghimo tag variable para sa computed balance

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Kuhaon ang UserID ug ang TotalPrice sa Rental
                    decimal totalPrice = 0;
                    using (var userCmd = new SqlCommand("SELECT UserID, TotalPrice FROM Rentals WHERE RentalID = @RentalID", conn))
                    {
                        userCmd.Parameters.AddWithValue("@RentalID", request.RentalId);
                        using (var reader = await userCmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                userId = Convert.ToInt32(reader["UserID"]);
                                totalPrice = Convert.ToDecimal(reader["TotalPrice"]);
                            }
                            else
                            {
                                response.StatusCode = 404;
                                response.Message = "Error: Rental record not found.";
                                response.Data = false;
                                return response;
                            }
                        }
                    }

                    // 2. Kuhaon ang total nga nabayad na daan (e.g., Downpayment) para ani nga Rental
                    decimal totalPaid = 0;
                    using (var paidCmd = new SqlCommand("SELECT ISNULL(SUM(Amount), 0) FROM Payment WHERE RentalID = @RentalID AND PaymentStatus = 'Completed'", conn))
                    {
                        paidCmd.Parameters.AddWithValue("@RentalID", request.RentalId);
                        totalPaid = Convert.ToDecimal(await paidCmd.ExecuteScalarAsync());
                    }

                    // 3. I-compute ang Remaining Balance (TotalPrice - TotalPaid)
                    balanceToPay = totalPrice - totalPaid;

                    // 🟢 I-check kung fully paid na ba daan aron dili mag-doble
                    if (balanceToPay <= 0)
                    {
                        response.StatusCode = 400;
                        response.Message = "Error: This rental is already fully paid. No balance remaining.";
                        response.Data = false;
                        return response;
                    }

                    // 4. I-insert ang Payment gamit ang NA-COMPUTE nga balance (imposible na masayop ang admin)
                    string insertQuery = @"INSERT INTO Payment (RentalID, UserID, PaymentMethod, PaymentType, PaymentStatus, Amount, CreatedAt)
                                   VALUES (@RentalID, @UserID, 'Cash', 'Full', 'Completed', @Amount, GETDATE())";

                    using (var cmd = new SqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@RentalID", request.RentalId);
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        cmd.Parameters.AddWithValue("@Amount", balanceToPay); // 🟢 Computed balance ang atong i-save!
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // 5. I-update ang Rental Status (Himuon natong 'Rented')
                    using (var updateCmd = new SqlCommand("UPDATE Rentals SET Status = 'Rented' WHERE RentalID = @RentalID", conn))
                    {
                        updateCmd.Parameters.AddWithValue("@RentalID", request.RentalId);
                        await updateCmd.ExecuteNonQueryAsync();
                    }

                    // I-close ang connection dinhi dapita aron limpyo
                    await conn.CloseAsync();
                }

                // 6. Mag-send og Success Notification gamit ang saktong balance
                string notifMessage = $"Success! We have received your Cash payment of PHP {balanceToPay} for your remaining balance.";
                await _notificationRepo.CreateNotification(userId, request.RentalId, notifMessage);

                response.Data = true;
                response.StatusCode = 200;
                response.Message = "Cash payment processed successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Error: {ex.Message}";
                response.Data = false;
            }
            return response;
        }
    }
}