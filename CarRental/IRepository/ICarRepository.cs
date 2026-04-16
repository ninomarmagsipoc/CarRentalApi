using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface ICarRepository
    {
        Task<ServiceResponse<object>> GetCars(int? userId = null);

        Task<ServiceResponse<string>> ToggleFavorite(int userId, int carId);

    }
}
