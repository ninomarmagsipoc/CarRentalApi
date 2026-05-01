namespace CarRental.Model
{
    public class PaymentRequest
    {
        public int RentalID { get; set; }
        public int UserID { get; set; }
        public string PaymentMethod { get; set; }

        public string SuccessUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }

    public class PaymentUrlRequest
    {
        public string SuccessUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }

    public class PenaltyRequest
    {
        public int RentalID { get; set; }
        public decimal Amount { get; set; }
        public string SuccessUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }

    public class CashPaymentRequest
    {
        public int RentalId { get; set; }
        public decimal Amount { get; set; }
    }
}
