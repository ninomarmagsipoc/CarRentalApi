using CarRental.IRepository;
using CarRental.Model;
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

        [HttpPost("upload-profile")]
        public async Task<IActionResult> UploadProfile([FromForm] UploadProfileRequest request)
        {
            var result = await _auth.UploadProfile(request);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("update-profile")]
        public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
        {
            var response = await _auth.UpdateProfile(request);
            if (response.StatusCode == 200) return Ok(response);
            return BadRequest(response);
        }

        [HttpGet("customers")]
        public async Task<ActionResult<ServiceResponse<List<CustomerDto>>>> GetAllCustomers()
        {
            var response = await _auth.GetAllCustomers();
            if (response.StatusCode == 200)
                return Ok(response);
            return BadRequest(response);
        }

        [HttpPut("customers/{id}/toggle-block")]
        public async Task<ActionResult<ServiceResponse<string>>> ToggleBlockUser(int id, [FromQuery] bool isBlocked)
        {
            var response = await _auth.ToggleBlockUser(id, isBlocked);
            if (response.StatusCode == 200)
                return Ok(response);

            return BadRequest(response);
        }

        [HttpGet("customers/{id}/history")]
        public async Task<ActionResult<ServiceResponse<List<CustomerRentalHistoryDto>>>> GetCustomerRentalHistory(int id)
        {
            var response = await _auth.GetCustomerRentalHistory(id);
            if (response.StatusCode == 200)
                return Ok(response);

            return BadRequest(response);
        }
    }
}
