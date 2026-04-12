using CarRental.IRepository;
using CarRental.Model.Response;
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
            public async Task<IActionResult> CreateRental([FromBody] RentalRequest request)
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
        public async Task<IActionResult> ReviewBooking(int rentalId, [FromBody] string newStatus)
        {
            var result = await _rental.ReviewBooking(rentalId, newStatus);
            return StatusCode(result.StatusCode, result);
        }
    }
    }
