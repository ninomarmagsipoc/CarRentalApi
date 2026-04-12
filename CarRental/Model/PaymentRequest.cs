namespace CarRental.Model
{
    public class PaymentRequest
    {
        public int RentalID { get; set; }
        public int UserID { get; set; }
        public string PaymentMethod { get; set; }
    }
}
