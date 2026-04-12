namespace CarRental.Model.Response
{
    public class PaymentResponse
    {
        public string CheckoutUrl { get; set; }
        public string Reference {  get; set; }
        public decimal Amount { get; set; }
    }
}
