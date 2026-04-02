using CarRental.IRepository;
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
        public async Task<IActionResult> GetCars()
        {
            var result = await _carRepo.GetCars();
            return StatusCode(result.StatusCode, result);

        }
    }
}
