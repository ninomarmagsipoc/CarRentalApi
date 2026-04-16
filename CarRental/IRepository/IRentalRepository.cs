using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface IRentalRepository
    {
        Task<ServiceResponse<Rental>> CreateRental(RentalRequest request);
        Task<ServiceResponse<List<Rental>>> GetRentals();
        Task<ServiceResponse<Rental>> GetRentalById(int id);

        Task<ServiceResponse<bool>> ReviewBooking(int rentalId, string newStatus, string reason = null); 

        Task<ServiceResponse<bool>> CheckAndMarkOverdueRentals();
        Task<ServiceResponse<object>> ReturnCar(int rentalId);

        Task<ServiceResponse<bool>> RequestCancellation(int rentalId, int userId);
        Task<ServiceResponse<bool>> ReviewCancellation(int rentalId, string action); // action: "Approved" or "Rejected ADMIN"
        Task<ServiceResponse<List<Rental>>> GetRentalsByUserId(int userId);

        Task<ServiceResponse<bool>> UpdateRentalStatus(int rentalId, string newStatus);

        Task<ServiceResponse<bool>> RequestReturn(int rentalId);
        Task<ServiceResponse<object>> ReviewReturnRequest(int rentalId, string action, string reason);

    }
}
