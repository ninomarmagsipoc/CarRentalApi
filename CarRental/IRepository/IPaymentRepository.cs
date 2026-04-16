using CarRental.Model;
using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface IPaymentRepository
    {
        Task<ServiceResponse<PaymentResponse>> CreatePayment(PaymentRequest request);// For handling initial payments when a user confirms a rental booking 
        Task<ServiceResponse<bool>> VerifyPayment(string payMongoReference);// For handling payment verification after the user completes the payment on the payment gateway
        Task<ServiceResponse<PaymentResponse>> CreateBalancePayment(int rentalId);// For handling remaining balance payments when a user wants to make a payment after the initial payment
        Task<ServiceResponse<bool>> RefundPayment(int paymentId, string reason = null);// For handling refunds when a user requests cancellation or when there are penalties

        Task<ServiceResponse<IEnumerable<PaymentDetailsResponse>>> GetAllPayments();// For admin to view all payments
        Task<ServiceResponse<IEnumerable<PaymentDetailsResponse>>> GetPaymentsByUser(int userId);// For users to view their payment history
        Task<ServiceResponse<BalanceCalculationResponse>> GetRemainingBalance(int rentalId);// For calculating remaining balance when a user wants to make a payment

        Task<ServiceResponse<PaymentResponse>> CreatePenaltyPayment(int rentalId, decimal amount);// For handling penalties such as late returns or damages

        Task<ServiceResponse<bool>> ProcessCancellationRefunds(int rentalId);// For handling refunds when a rental is cancelled
    }
}
