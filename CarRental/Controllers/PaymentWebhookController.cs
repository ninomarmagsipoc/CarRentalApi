using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Data.SqlClient;

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api/webhook")]
    public class PaymentWebhookController : ControllerBase
    {
        private readonly string _connectionString;

        public PaymentWebhookController(IConfiguration config)
        {
            _connectionString = config["ConnectionStrings:CarRental"];
        }

        [HttpPost("paymongo")]
        public async Task<IActionResult> HandlePayMongoWebhook()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            var json = JsonDocument.Parse(body);

            try
            {
                var eventType = json.RootElement
                    .GetProperty("data")
                    .GetProperty("attributes")
                    .GetProperty("type")
                    .GetString();

                // 🔥 IMPORTANT EVENTS
                if (eventType == "link.payment.paid")
                {
                    var reference = json.RootElement
                        .GetProperty("data")
                        .GetProperty("attributes")
                        .GetProperty("data")
                        .GetProperty("id")
                        .GetString();

                    await MarkPaymentAsPaid(reference);
                }

                if (eventType == "link.payment.failed")
                {
                    var reference = json.RootElement
                        .GetProperty("data")
                        .GetProperty("attributes")
                        .GetProperty("data")
                        .GetProperty("id")
                        .GetString();

                    await MarkPaymentAsFailed(reference);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest($"Webhook Error: {ex.Message}");
            }
        }

        // ===========================
        // UPDATE DB METHODS
        // ===========================

        private async Task MarkPaymentAsPaid(string reference)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string query = @"
                UPDATE Payment
                SET PaymentStatus = 'Paid'
                WHERE PayMongoRef = @Ref
            ";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Ref", reference);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task MarkPaymentAsFailed(string reference)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string query = @"
                UPDATE Payment
                SET PaymentStatus = 'Failed'
                WHERE PayMongoRef = @Ref
            ";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Ref", reference);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}