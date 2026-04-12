namespace CarRental.Model.Response
{
    public class Rental
    {
        public int RentalID { get; set; }
        public int UserID { get; set; }
        public string UserName { get; set; }
        public int CarID { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public int TotalDays { get; set; }
        public decimal TotalPrice { get; set; }

        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
