using CarRental.IRepository;
using CarRental.Model.Response;
using CarRental.Server;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api/rental")]
    public class RentalController : Controller
    {
        private IRentalRepository _rental;

        public RentalController(IRentalRepository rental)
        {
            _rental = rental;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateRental([FromForm] RentalRequest request)
        {
            var result = await _rental.CreateRental(request);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _rental.GetRentals();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _rental.GetRentalById(id);
            return StatusCode(result.StatusCode, result);


        }

        [HttpPut("admin/rentals/{rentalId}/review")]
        public async Task<IActionResult> ReviewBooking(int rentalId, [FromBody] ReviewBookingDto request)
        {
            var result = await _rental.ReviewBooking(rentalId, request.NewStatus, request.Reason);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("return/{rentalId}")]
        public async Task<IActionResult> ReturnCar(int rentalId)
        {
            var response = await _rental.ReturnCar(rentalId);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("check-overdue")]
        public async Task<IActionResult> CheckOverdueRentals()
        {
            var response = await _rental.CheckAndMarkOverdueRentals();
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("{rentalId}/request-cancel")]
        public async Task<IActionResult> RequestCancellation(int rentalId, [FromBody] int userId)
        {
            var response = await _rental.RequestCancellation(rentalId, userId);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("admin/rentals/{rentalId}/cancel-review")]
        public async Task<IActionResult> ReviewCancellation(int rentalId, [FromBody] string action) // Sends "Approved" or "Rejected"
        {
            var response = await _rental.ReviewCancellation(rentalId, action);
            return StatusCode(response.StatusCode, response);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetRentalsByUser(int userId)
        {
            var response = await _rental.GetRentalsByUserId(userId);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPut("admin/rentals/{rentalId}/status")]
        public async Task<IActionResult> UpdateRentalStatus(int rentalId, [FromBody] string newStatus)
        {
            var allowedStatuses = new List<string> { "On the Way", "Delivered" };
            if (!allowedStatuses.Contains(newStatus))
            {
                return BadRequest(new { message = "Invalid status." });
            }

            var result = await _rental.UpdateRentalStatus(rentalId, newStatus);

            if (result.StatusCode == 200)
            {
                return Ok(result);
            }

            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("user/rentals/{rentalId}/request-return")]
        public async Task<IActionResult> RequestReturn(int rentalId)
        {
            var result = await _rental.RequestReturn(rentalId);
            if (result.StatusCode == 200) return Ok(result);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("admin/rentals/{rentalId}/review-return")]
        public async Task<IActionResult> ReviewReturnRequest(int rentalId, [FromBody] ReviewReturnDto request)
        {
            var result = await _rental.ReviewReturnRequest(rentalId, request.Action, request.Reason);
            if (result.StatusCode == 200) return Ok(result);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("car/{carId}/booked-dates")]
        public async Task<IActionResult> GetBookedDatesForCar(int carId)
        {
           
            var response = await _rental.GetBookedDatesForCar(carId);

            if (response.StatusCode == 200)
            {
                return Ok(response.Data);
            }

            return StatusCode(response.StatusCode, response.Message);
        }

        [HttpDelete("trash/{rentalId}")]
        public async Task<IActionResult> MoveToTrash(int rentalId)
        {
            var response = await _rental.MoveToTrash(rentalId);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPut("{rentalId}/archive")]
        public async Task<IActionResult> ArchiveRental(int rentalId)
        {
            var result = await _rental.ArchiveRental(rentalId);
            if (result.StatusCode == 200)
                return Ok(result);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("{rentalId}/hide")]
        public async Task<IActionResult> HideRentalPermanently(int rentalId)
        {
            var result = await _rental.HideRentalPermanently(rentalId);
            if (result.StatusCode == 200)
                return Ok(result);

            return StatusCode(result.StatusCode, result);
        }
        [HttpPut("{rentalId}/restore")]
        public async Task<IActionResult> RestoreRental(int rentalId)
        {
            var result = await _rental.RestoreRental(rentalId);
            if (result.StatusCode == 200)
                return Ok(result);

            return StatusCode(result.StatusCode, result);
        }
    }
}
