using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface ICarRepository
    {
        Task<ServiceResponse<object>> GetCars();

    }
}
