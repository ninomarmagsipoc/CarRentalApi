namespace CarRental.Model.Response
{
    public class RentalRequest
    {
        public int UserID { get; set; }
        public int CarID { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public string FullName { get; set; }
        public string ContactNumber { get; set; }
        public string PickupLocation { get; set; }
    }
}
