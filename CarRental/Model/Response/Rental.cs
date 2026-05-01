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

        public string FullName { get; set; }
        public string ContactNumber { get; set; }
        public string PickupLocation { get; set; }
        public string? DriverLicense { get; set; }
        public int? PaymentID { get; set; }

        public string CarName { get; set; }

        public decimal PenaltyFee { get; set; }
        public decimal Amount { get; set; }
        public string PaymentReference { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsPermanentlyHidden { get; set; }
    }

    public class ReviewBookingDto
    {
        public string NewStatus { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class ReviewReturnDto
    {
        public string Action { get; set; }
        public string Reason { get; set; }
    }

    public class BookedDateDto
    {
       
        public string startDate { get; set; }
        public string endDate { get; set; }
    }
}
