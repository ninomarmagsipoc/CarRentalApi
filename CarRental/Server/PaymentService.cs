using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using Dapper;
using Microsoft.Extensions.Configuration;
using System.Data;
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
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var result = await conn.ExecuteScalarAsync<decimal?>(
                        "pay_GetRentalTotalPrice",
                        new { RentalID = request.RentalID },
                        commandType: CommandType.StoredProcedure
                    );

                    if (result == null) return ErrorResponse(response, 404, "Rental not found.");

                    decimal totalPrice = result.Value;

                    decimal downPayment = totalPrice * 0.5m;
                    decimal remainingAmount = totalPrice - downPayment;

                    string description = $"Downpayment for Rental #{request.RentalID}";
                    var (success, checkoutUrl, reference, error) = await CreatePayMongoLink(downPayment, request.RentalID, description, request.SuccessUrl, request.CancelUrl);

                    if (!success) return ErrorResponse(response, 500, error);

                    await conn.ExecuteAsync(
                        "pay_InsertPayment",
                        new
                        {
                            RentalID = request.RentalID,
                            UserID = request.UserID,
                            Amount = downPayment,
                            RemainingBalance = remainingAmount,
                            PaymentMethod = request.PaymentMethod,
                            PaymentType = "Partial",
                            PaymentStatus = "Pending",
                            PayMongoRef = reference
                        },
                        commandType: CommandType.StoredProcedure
                    );

                    response.Data = new PaymentResponse { CheckoutUrl = checkoutUrl, Reference = reference, Amount = downPayment };
                    response.StatusCode = 200;
                    response.Message = "Payment link generated.";

                }
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
                            payment_method_types = new[] { "gcash", "paymaya", "card" },

                            line_items = new[]
                            {
                        new
                        {
                            currency = "PHP",
                            amount = (int)(amount * 100),
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
                            var existingPayment = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                "pay_GetPaymentByRef",
                                new { PayMongoRef = payMongoReference },
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure
                            );

                            string existingType = existingPayment?.PaymentType;
                            string existingStatus = existingPayment?.PaymentStatus;

                            if (existingStatus == "Completed")
                            {
                                await transaction.RollbackAsync();
                                response.Data = true;
                                response.StatusCode = 200;
                                response.Message = "Payment already processed.";
                                return response;
                            }

                            var userDetails = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                "pay_GetUserDetailsByRental",
                                new { RentalID = rentalId },
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure
                            );

                            int userId = userDetails?.UserID ?? 0;
                            string userName = userDetails?.FullName ?? "A customer";

                            if (existingType == "Partial")
                            {
                                await conn.ExecuteAsync("pay_UpdatePaymentToCompleted", new { PayMongoRef = payMongoReference }, transaction, commandType: CommandType.StoredProcedure);
                                await conn.ExecuteAsync("pay_UpdateRentalStatus", new { RentalID = rentalId, Status = "Pending Review" }, transaction, commandType: CommandType.StoredProcedure);

                                await _notificationRepo.CreateNotification(1, rentalId, $"New Booking Alert: {userName} paid the 50% downpayment for Rental #{rentalId}. Review needed.");
                                if (userId > 0) await _notificationRepo.CreateNotification(userId, rentalId, $"Downpayment received for Rental #{rentalId}. Your booking is now Pending Review.");
                            }
                            else if (existingType == "Full" || existingType == "Balance")
                            {
                                await conn.ExecuteAsync("pay_UpdatePaymentToCompleted", new { PayMongoRef = payMongoReference }, transaction, commandType: CommandType.StoredProcedure);
                                await conn.ExecuteAsync("pay_UpdateRentalStatus", new { RentalID = rentalId, Status = "Rented" }, transaction, commandType: CommandType.StoredProcedure);

                                await _notificationRepo.CreateNotification(userId, rentalId, "Your rental is Rented. Thanks for using our website!");
                            }
                            else if (existingType == "Penalty")
                            {
                                await conn.ExecuteAsync("pay_UpdatePaymentToCompleted", new { PayMongoRef = payMongoReference }, transaction, commandType: CommandType.StoredProcedure);
                                await conn.ExecuteAsync("pay_UpdateRentalStatus", new { RentalID = rentalId, Status = "Returned" }, transaction, commandType: CommandType.StoredProcedure);

                                await _notificationRepo.CreateNotification(userId, rentalId, "Penalty fee paid successfully. Your car rental is now officially marked as Returned. Thank you!");
                            }
                            else
                            {
                                int rowsAffected = await conn.ExecuteAsync(
                                    "pay_InsertDirectFullPayment",
                                    new { RentalID = rentalId, UserID = userId, Amount = amountPaid, PayMongoRef = payMongoReference },
                                    transaction: transaction,
                                    commandType: CommandType.StoredProcedure
                                );

                                if (rowsAffected > 0)
                                {
                                    await conn.ExecuteAsync("pay_UpdateRentalStatus", new { RentalID = rentalId, Status = "Rented" }, transaction, commandType: CommandType.StoredProcedure);
                                    await _notificationRepo.CreateNotification(userId, rentalId, "Your rental is Rented. Thanks for using our website!");
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

                    var rentalInfo = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "pay_GetRentalStatusAndUser",
                        new { RentalID = rentalId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (rentalInfo != null)
                    {
                        rentalStatus = rentalInfo.Status;
                        userId = rentalInfo.UserID;
                    }

                    if (rentalStatus == "Expired" || rentalStatus == "Refunded" || rentalStatus == "Cancelled")
                    {
                        response.StatusCode = 400;
                        response.Message = $"Cannot process payment. This rental has already been {rentalStatus.ToLower()}.";
                        return response;
                    }

                    var balanceResult = await conn.ExecuteScalarAsync<decimal?>(
                        "pay_GetRemainingBalance",
                        new { RentalID = rentalId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (balanceResult.HasValue)
                    {
                        remainingBalance = balanceResult.Value;
                    }
                }

                if (remainingBalance <= 0)
                {
                    response.StatusCode = 400;
                    response.Message = "Payment Complete! You have already paid the remaining balance for this car.";
                    return response;
                }

                string description = $"Remaining Balance for Rental #{rentalId}";
                var (success, checkoutUrl, reference, error) = await CreatePayMongoLink(remainingBalance, rentalId, description, successUrl, cancelUrl);

                if (!success)
                {
                    response.StatusCode = 500;
                    response.Message = error;
                    return response;
                }

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
                string payMongoRef;
                decimal amount;
                int userId, rentalId;
                string userEmail;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    var paymentDetails = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "pay_GetPaymentForRefund",
                        new { PaymentID = paymentId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (paymentDetails == null)
                        return ErrorResponse(response, 404, "Payment record not found.");

                    if (paymentDetails.PaymentStatus == "Refunded")
                        return ErrorResponse(response, 400, "This payment has already been refunded.");

                    payMongoRef = paymentDetails.PayMongoRef;
                    amount = paymentDetails.Amount;
                    userId = paymentDetails.UserID;
                    rentalId = paymentDetails.RentalID;
                    userEmail = paymentDetails.Email;
                }

                var client = _httpClientFactory.CreateClient();
                var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_paymongoKey}:"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                var sessionRes = await client.GetAsync($"https://api.paymongo.com/v1/checkout_sessions/{payMongoRef}");
                if (!sessionRes.IsSuccessStatusCode) return ErrorResponse(response, 400, "Failed to retrieve payment details from PayMongo.");

                using var sessionDoc = JsonDocument.Parse(await sessionRes.Content.ReadAsStringAsync());
                var paymentsArray = sessionDoc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("payments");

                if (paymentsArray.GetArrayLength() == 0) return ErrorResponse(response, 400, "No successful payment found to refund.");

                string actualPaymentId = paymentsArray[0].GetProperty("id").GetString();

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

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using var transaction = conn.BeginTransaction();
                    try
                    {
                        string finalReason = string.IsNullOrEmpty(reason) ? "No reason provided" : reason;

                        await conn.ExecuteAsync(
                            "pay_UpdatePaymentToRefunded",
                            new { PaymentID = paymentId, RentalID = rentalId, RefundReason = finalReason },
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure
                        );

                        await conn.ExecuteAsync(
                            "pay_UpdateRentalToRejected",
                            new { RentalID = rentalId },
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure
                        );

                        string notificationMessage = "Your payment has been refunded successfully. Please rent a car again.";
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

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var payments = await conn.QueryAsync<PaymentDetailsResponse>(
                        "pay_GetAllPayments",
                        commandType: CommandType.StoredProcedure
                    );

                    response.Data = payments;
                    response.StatusCode = 200;
                    response.Message = "All payments retrieved.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }

            return response;
        }


        public async Task<ServiceResponse<IEnumerable<PaymentDetailsResponse>>> GetPaymentsByUser(int userId)
        {
            var response = new ServiceResponse<IEnumerable<PaymentDetailsResponse>>();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var payments = await conn.QueryAsync<PaymentDetailsResponse>(
                        "pay_GetPaymentsByUser",
                        new { UserID = userId },
                        commandType: CommandType.StoredProcedure
                    );

                    response.Data = payments;
                    response.StatusCode = 200;
                    response.Message = $"Payment history for User #{userId} retrieved.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }

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

                    var balanceData = await conn.QueryFirstOrDefaultAsync<BalanceCalculationResponse>(
                        "pay_CalculateRemainingBalance",
                        new { RentalID = rentalId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (balanceData != null)
                    {
                        response.Data = balanceData;
                        response.StatusCode = 200;
                        response.Message = "Balance calculated.";
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Message = "Rental not found.";
                    }
                }
            }
            catch (Exception ex)
            {
                return ErrorResponse(response, 500, ex.Message);
            }

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
                int userId = 0;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var userResult = await conn.ExecuteScalarAsync<int?>(
                        "pay_GetUserIdByRental",
                        new { RentalID = rentalId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (userResult.HasValue)
                    {
                        userId = userResult.Value;
                    }
                    else
                    {
                        return ErrorResponse(response, 404, "Rental not found.");
                    }

                    string description = $"Penalty Fee for late return - Rental #{rentalId}";
                    var (success, checkoutUrl, reference, error) = await CreatePayMongoLink(amount, rentalId, description, successUrl, cancelUrl);

                    if (!success) return ErrorResponse(response, 500, error);

                    await conn.ExecuteAsync(
                        "pay_InsertPenaltyPayment",
                        new
                        {
                            RentalID = rentalId,
                            UserID = userId,
                            Amount = amount,
                            PayMongoRef = reference
                        },
                        commandType: CommandType.StoredProcedure
                    );

                    response.Data = new PaymentResponse { CheckoutUrl = checkoutUrl, Reference = reference, Amount = amount };
                    response.StatusCode = 200;
                    response.Message = "Penalty checkout link generated successfully.";
                }
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

                    var paymentsToRefund = (await conn.QueryAsync<dynamic>(
                        "pay_GetCompletedPaymentsForRefund",
                        new { RentalID = rentalId },
                        commandType: CommandType.StoredProcedure
                    )).ToList();

                    if (!paymentsToRefund.Any())
                    {
                        response.StatusCode = 400;
                        response.Message = "No completed payments found to refund.";
                        return response;
                    }

                    int userId = (int)paymentsToRefund.First().UserID;
                    decimal totalRefunded = 0;

                    foreach (var payment in paymentsToRefund)
                    {
                        decimal amount = (decimal)payment.Amount;
                        decimal refundAmount = amount * 0.75m; // 75% refund rule
                        totalRefunded += refundAmount;

                        var sessionRes = await client.GetAsync($"https://api.paymongo.com/v1/checkout_sessions/{payment.PayMongoRef}");
                        if (!sessionRes.IsSuccessStatusCode) continue;

                        using var sessionDoc = JsonDocument.Parse(await sessionRes.Content.ReadAsStringAsync());
                        var paymentsArray = sessionDoc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("payments");

                        if (paymentsArray.GetArrayLength() == 0) continue;

                        string actualPaymentId = paymentsArray[0].GetProperty("id").GetString();

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
                            await conn.ExecuteAsync(
                                "pay_UpdatePaymentToRefundedWithReason",
                                new { PaymentID = (int)payment.PaymentID, RefundReason = "75% Cancellation Refund" },
                                commandType: CommandType.StoredProcedure
                            );
                        }
                    }

                    var userEmail = await conn.ExecuteScalarAsync<string>(
                        "pay_FinalizeCancellationAndGetEmail",
                        new { RentalID = rentalId, UserID = userId },
                        commandType: CommandType.StoredProcedure
                    );

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
                decimal balanceToPay = 0;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var details = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "pay_GetRentalBalanceDetails",
                        new { RentalID = request.RentalId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (details == null)
                    {
                        response.StatusCode = 404;
                        response.Message = "Error: Rental record not found.";
                        response.Data = false;
                        return response;
                    }

                    userId = details.UserID;
                    decimal totalPrice = details.TotalPrice;
                    decimal totalPaid = details.TotalPaid;

                    balanceToPay = totalPrice - totalPaid;

                    if (balanceToPay <= 0)
                    {
                        response.StatusCode = 400;
                        response.Message = "Error: This rental is already fully paid. No balance remaining.";
                        response.Data = false;
                        return response;
                    }

                    await conn.ExecuteAsync(
                        "pay_InsertCashPayment",
                        new
                        {
                            RentalID = request.RentalId,
                            UserID = userId,
                            Amount = balanceToPay
                        },
                        commandType: CommandType.StoredProcedure
                    );
                }

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