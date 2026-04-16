using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CarRental.IRepository; // 🟢 Injected your repository

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api/webhook")]
    public class PaymentWebhookController : ControllerBase
    {
        private readonly IPaymentRepository _paymentRepo;

        public PaymentWebhookController(IPaymentRepository paymentRepo)
        {
            _paymentRepo = paymentRepo;
        }

        [HttpPost("paymongo")]
        public async Task<IActionResult> HandlePayMongoWebhook()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            try
            {
                using var json = JsonDocument.Parse(body);

                var eventType = json.RootElement
                    .GetProperty("data")
                    .GetProperty("attributes")
                    .GetProperty("type")
                    .GetString();

                // 🟢 Listen specifically for Checkout Sessions
                if (eventType == "checkout_session.payment.paid")
                {
                    // Grab the 'cs_xxxxxx' ID
                    var reference = json.RootElement
                        .GetProperty("data")
                        .GetProperty("attributes")
                        .GetProperty("data")
                        .GetProperty("id")
                        .GetString();

                    if (!string.IsNullOrEmpty(reference))
                    {
                         await _paymentRepo.VerifyPayment(reference);
                    }
                }

                // Always return 200 OK so PayMongo knows you received it safely
                return Ok();
            }
            catch (Exception ex)
            {
              
                Console.WriteLine($"Webhook Error: {ex.Message}");
                return Ok();
            }
        }
    }
}