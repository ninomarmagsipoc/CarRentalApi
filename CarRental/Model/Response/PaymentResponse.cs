namespace CarRental.Model.Response
{
    public class PaymentResponse
    {
        public string CheckoutUrl { get; set; }
        public string Reference {  get; set; }
        public decimal Amount { get; set; }
    }

    public class PaymentDetailsResponse
    {
        public int PaymentID { get; set; }
        public int RentalID { get; set; }
        public int UserID { get; set; }

        public string UserName { get; set; } = string.Empty;
        public string CarName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }

        public decimal Amount { get; set; }
        public decimal RemainingBalance { get; set; }
        public string PaymentType { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public string RefundReason { get; set; } = string.Empty;
        public string BalanceDisplay { get; set; } = string.Empty;
    }

    public class BalanceCalculationResponse
    {
        public decimal TotalPrice { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingBalance { get; set; }
    }

    public class RefundRequestDto
    {
        public string Reason { get; set; } = string.Empty;
    }
}
