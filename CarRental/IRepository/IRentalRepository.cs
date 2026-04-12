using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface IRentalRepository
    {
        Task<ServiceResponse<Rental>> CreateRental(RentalRequest request);
        Task<ServiceResponse<List<Rental>>> GetRentals();
        Task<ServiceResponse<Rental>> GetRentalById(int id);

        Task<ServiceResponse<bool>> ReviewBooking(int rentalId, string newStatus);

    }
}
