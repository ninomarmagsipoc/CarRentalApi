using CarRental.IRepository;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api")]
    public class LoginController : Controller
    {
        private ILoginRespository _login;
        public LoginController(ILoginRespository login) 
        {
            _login = login;
        }

        [HttpGet]
        [Route("GetLogin")]
        public async Task<IActionResult> GetUserLogin(string username, string password)
        {
            var test = _login.GetLogin(username, password);
            return Ok();
        }
    }
}
