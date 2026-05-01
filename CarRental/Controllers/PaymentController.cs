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
        public async Task<IActionResult> CreateBalancePayment(int rentalId, [FromBody] PaymentUrlRequest request)
        {
            var response = await _payment.CreateBalancePayment(rentalId, request.SuccessUrl, request.CancelUrl);

            if (response.StatusCode == 200) return Ok(response);
            return BadRequest(response);
        }

        [HttpPost("verify")]
        public async Task<IActionResult> VerifyPayment([FromQuery] string payMongoReference)
        {
            if (string.IsNullOrEmpty(payMongoReference))
            {
                return BadRequest(new { message = "Reference is required" });
            }

          
            var response = await _payment.VerifyPayment(payMongoReference);

            
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

        [HttpPost("penalty")]
        public async Task<IActionResult> CreatePenaltyPayment([FromBody] PenaltyRequest request)
        {
           
            var response = await _payment.CreatePenaltyPayment(
                request.RentalID,
                request.Amount,
                request.SuccessUrl,
                request.CancelUrl
            );

            if (response.StatusCode == 200) return Ok(response);
            return BadRequest(response);
        }

        [HttpPost("cash-payment")]
        public async Task<ActionResult<ServiceResponse<bool>>> ProcessCashPayment([FromBody] CashPaymentRequest request)
        {
            var result = await _payment.ProcessCashBalancePayment(request);
            if (result.StatusCode == 200) return Ok(result);
            return BadRequest(result);
        }

    }
}
