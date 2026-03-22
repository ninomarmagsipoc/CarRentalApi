using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface IAuthRepository
    {
        Task<ServiceResponse<object>> Register(RegisterRequest request);
        Task<ServiceResponse<object>> Login(LoginRequest request);
    }
}
