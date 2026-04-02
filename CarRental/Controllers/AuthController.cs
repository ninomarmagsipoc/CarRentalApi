using CarRental.IRepository;
using CarRental.Model.Response;
using Microsoft.AspNetCore.Mvc;

namespace CarRental.Controllers
{

    [ApiController]
    [Route("api/auth")]
    public class AuthController : Controller
    {
        private readonly IAuthRepository _auth;

        public AuthController(IAuthRepository auth)
        {
            _auth = auth;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var result = await _auth.Register(request);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _auth.Login(request);



            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("send-otp")]

        public async Task<IActionResult> SendOtp(string email)
        {
            var result = await _auth.SendOtp(email);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("verify-otp")]

        public async Task<IActionResult> VerifyOtp(string email, string code)
        {
            var result = await _auth.VerifyOtp(email, code);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("send-reset-otp")]
        public async Task<IActionResult> SendResetOtp(string email)
        {
            var result = await _auth.SendResetOtp(email);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(string email, string code, string newPassword)
        {
            var result = await _auth.ResetPassword(email, code, newPassword);
            return StatusCode(result.StatusCode, result);
        }
    }
}
