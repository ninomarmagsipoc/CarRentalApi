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
    }
}
