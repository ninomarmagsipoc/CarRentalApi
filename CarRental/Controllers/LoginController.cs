using CarRental.IRepository;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api")]
    public class LoginController : Controller
    {
        private ILoginRepository _login;
        public LoginController(ILoginRepository login) 
        {
            _login = login;
        }

        [HttpGet]
        [Route("GetLogin")]
        public async Task<IActionResult> GetUserLogin(string username, string password)
        {
            var result = await _login.GetLogin(username, password);
            return Ok(result);
        }
    }
}
