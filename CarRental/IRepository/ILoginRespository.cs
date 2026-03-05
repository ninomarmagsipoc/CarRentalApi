using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface ILoginRespository
    {
        Task<ServiceResponse<object>> GetLogin(string username, string password);
    }
}
