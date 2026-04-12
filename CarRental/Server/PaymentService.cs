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

        // Inject IHttpClientFactory instead of creating new HttpClient
        public PaymentService(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _connectionString = config.GetConnectionString("CarRental");
            _paymongoKey = config["PayMongo:SecretKey"];
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ServiceResponse<PaymentResponse>> CreatePayment(PaymentRequest request)
        {
            var response = new ServiceResponse<PaymentResponse>();

            try
            {
                // 1. Fetch rental total price
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

                // 2. Calculations
                decimal downPayment = totalPrice * 0.5m;
                decimal remainingAmount = totalPrice - downPayment;

                // 3. Create PayMongo Link
                var (success, checkoutUrl, reference, error) = await CreatePayMongoLink(downPayment, request.RentalID);
                if (!success) return ErrorResponse(response, 500, error);

                // 4. Save to DB 
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

        private async Task<(bool Success, string CheckoutUrl, string Reference, string ErrorMessage)> CreatePayMongoLink(decimal amount, int rentalId)
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
                            // 1. Explicitly set testable payment methods
                            payment_method_types = new[] { "gcash", "paymaya", "card" },

                            // 2. Checkout API uses line_items instead of a single amount
                            line_items = new[]
                            {
                        new
                        {
                            currency = "PHP",
                            amount = (int)(amount * 100), // Convert to cents
                            name = $"Downpayment for Rental #{rentalId}",
                            quantity = 1
                        }
                    },

                            success_url = "http://127.0.0.1:5174/",
                            cancel_url = "http://127.0.0.1:5174/",

                            reference_number = rentalId.ToString()
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                // 4. Hit the Checkout Sessions endpoint instead of Links
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

        private ServiceResponse<T> ErrorResponse<T>(ServiceResponse<T> res, int code, string msg)
        {
            res.StatusCode = code;
            res.Message = msg;
            return res;
        }
    }
}