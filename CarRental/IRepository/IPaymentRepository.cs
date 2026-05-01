using CarRental.Model;
using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface IPaymentRepository
    {
        Task<ServiceResponse<PaymentResponse>> CreatePayment(PaymentRequest request);
        Task<ServiceResponse<bool>> VerifyPayment(string payMongoReference);
        Task<ServiceResponse<PaymentResponse>> CreateBalancePayment(int rentalId, string successUrl, string cancelUrl);
        Task<ServiceResponse<bool>> RefundPayment(int paymentId, string reason = null);

        Task<ServiceResponse<IEnumerable<PaymentDetailsResponse>>> GetAllPayments();
        Task<ServiceResponse<IEnumerable<PaymentDetailsResponse>>> GetPaymentsByUser(int userId);
        Task<ServiceResponse<BalanceCalculationResponse>> GetRemainingBalance(int rentalId);

        Task<ServiceResponse<PaymentResponse>> CreatePenaltyPayment(int rentalId, decimal amount, string successUrl, string cancelUrl);

        Task<ServiceResponse<bool>> ProcessCancellationRefunds(int rentalId);

        Task<ServiceResponse<bool>> ProcessCashBalancePayment(CashPaymentRequest request);
    }
}
