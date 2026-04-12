using CarRental.Model;
using CarRental.Model.Response;

namespace CarRental.IRepository
{
    public interface IPaymentRepository
    {
        Task<ServiceResponse<PaymentResponse>> CreatePayment(PaymentRequest request);
    }
}
