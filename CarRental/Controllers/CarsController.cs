using CarRental.IRepository;
using CarRental.Model;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api/")]
    public class CarsController : Controller
    {
        private readonly ICarRepository _carRepo;

        public CarsController(ICarRepository carRepo)
        {
            _carRepo = carRepo;
        }

        [HttpGet("cars")]
        public async Task<IActionResult> GetCars([FromQuery] int userId) 
        {
            
            var result = await _carRepo.GetCars(userId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("cars/favorite")]
        public async Task<IActionResult> ToggleFavorite([FromBody] FavoriteRequest request)
        {
            
            var result = await _carRepo.ToggleFavorite(request.UserId, request.CarId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("bookings/{carId}")]
        public async Task<IActionResult> GetCarBookings(int carId)
        {
            var response = await _carRepo.GetCarBookings(carId);
            if (response.StatusCode == 200)
            {
                return Ok(response);
            }
            return StatusCode(response.StatusCode, response);
        }

        [HttpDelete("{carId}")]
        public async Task<IActionResult> DeleteCar(int carId)
        {
            var response = await _carRepo.DeleteCar(carId);
            if (response.StatusCode == 200)
            {
                return Ok(response);
            }
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost]
        public async Task<IActionResult> AddCar([FromForm] CarRequest request)
        {
            var response = await _carRepo.AddCar(request);
            if (response.StatusCode == 200)
            {
                return Ok(response);
            }
            return StatusCode(response.StatusCode, response);
        }

        [HttpPut("edit/{carId}")]
        public async Task<IActionResult> EditCar(int carId, [FromForm] CarRequest request)
        {
            var response = await _carRepo.EditCar(carId, request);
            if (response.StatusCode == 200) return Ok(response);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPut("restore/{carId}")]
        public async Task<IActionResult> RestoreCar(int carId)
        {
            var response = await _carRepo.RestoreCar(carId);
            if (response.StatusCode == 200) return Ok(response);
            return StatusCode(response.StatusCode, response);
        }

        [HttpGet("archived")]
        public async Task<IActionResult> GetArchivedCars()
        {
            var response = await _carRepo.GetArchivedCars();
            if (response.StatusCode == 200) return Ok(response);
            return StatusCode(response.StatusCode, response);
        }
    }
}
