using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface ILoginRepository
    {
        Task<ServiceResponse<object>> GetLogin(string username, string password);
    }
}
