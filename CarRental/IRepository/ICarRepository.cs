using CarRental.Model;
using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface ICarRepository
    {
        Task<ServiceResponse<object>> GetCars(int? userId = null);

        Task<ServiceResponse<string>> ToggleFavorite(int userId, int carId);

        Task<ServiceResponse<string>> AddCar(CarRequest request);

        Task<ServiceResponse<List<CarBookingDTO>>> GetCarBookings(int carId);
        Task<ServiceResponse<string>> DeleteCar(int carId);

        Task<ServiceResponse<string>> EditCar(int carId, CarRequest request);
        Task<ServiceResponse<string>> RestoreCar(int carId);
        Task<ServiceResponse<object>> GetArchivedCars();
    }
}
