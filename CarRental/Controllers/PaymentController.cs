using CarRental.IRepository;
using CarRental.Model;
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

    }
}
