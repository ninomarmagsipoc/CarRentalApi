using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface IAuthRepository
    {

        // Registration and login
        Task<ServiceResponse<object>> Register(RegisterRequest request);
        Task<ServiceResponse<object>> Login(LoginRequest request);

        //OTP for registration
        Task<ServiceResponse<object>> SendOtp(string email);
        Task<ServiceResponse<object>> VerifyOtp(string email, string code);

        //Forgot password
        Task<ServiceResponse<object>> SendResetOtp(string email);
        Task<ServiceResponse<object>> ResetPassword(string email, string code, string newPassword);
    }
}
