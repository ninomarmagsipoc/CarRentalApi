using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using CarRental.Server;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api/payment")]
    public class PaymentController : Controller
    {
        private readonly IPaymentRepository _payment;

        public PaymentController(IPaymentRepository payment)
        {
            _payment = payment;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreatePayment([FromBody] PaymentRequest request)
        {
            var result = await _payment.CreatePayment(request);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("create-balance/{rentalId}")]
        public async Task<ActionResult<ServiceResponse<PaymentResponse>>> CreateBalancePayment(int rentalId)
        {
            var response = await _payment.CreateBalancePayment(rentalId);

            // Standardizing the HTTP Status codes based on your ServiceResponse
            if (response.StatusCode == 200)
            {
                return Ok(response);
            }
            if (response.StatusCode == 400)
            {
                return BadRequest(response);
            }
            if (response.StatusCode == 404)
            {
                return NotFound(response);
            }

            // Fallback for 500 errors
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("verify")]
        public async Task<IActionResult> VerifyPayment([FromQuery] string payMongoReference)
        {
            if (string.IsNullOrEmpty(payMongoReference))
            {
                return BadRequest(new { message = "Reference is required" });
            }

            // Call the service method we created earlier
            var response = await _payment.VerifyPayment(payMongoReference);

            // Return the result to React
            if (response.StatusCode == 200)
            {
                return Ok(response);
            }

            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("refund/{paymentId}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Refund(int paymentId, [FromBody] RefundRequestDto request)
        {
            var result = await _payment.RefundPayment(paymentId, request.Reason);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllPayments()
        {
            var result = await _payment.GetAllPayments();
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserPayments(int userId)
        {
            var result = await _payment.GetPaymentsByUser(userId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("remaining/{rentalId}")]
        public async Task<IActionResult> GetRemainingBalance(int rentalId)
        {
            var result = await _payment.GetRemainingBalance(rentalId);
            return StatusCode(result.StatusCode, result);
        }

    }
}
