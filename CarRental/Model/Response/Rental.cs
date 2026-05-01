namespace CarRental.Model.Response
{
    public class Rental
    {
        public int RentalID { get; set; }// Primary key for the rental record
        public int UserID { get; set; }// Foreign key referencing the user who made the rental
        public string UserName { get; set; }// Name of the user who made the rental
        public int CarID { get; set; }// Foreign key referencing the car being rented

        public DateTime StartDate { get; set; }// Start date of the rental period
        public DateTime EndDate { get; set; }// End date of the rental period

        public int TotalDays { get; set; }// Total number of days for the rental
        public decimal TotalPrice { get; set; }// Total price for the rental
        public string Status { get; set; } = "Pending";// Current status of the rental (e.g., Pending, Approved, Rejected, Cancelled, Returned)
        public DateTime CreatedAt { get; set; }// Timestamp when the rental record was created
        public DateTime UpdatedAt { get; set; }// Timestamp when the rental record was last updated

        public string FullName { get; set; }// Full name of the user who made the rental
        public string ContactNumber { get; set; }// Contact number of the user who made the rental
        public string PickupLocation { get; set; }// Pickup location for the rental
        public string? DriverLicense { get; set; }
        public int? PaymentID { get; set; }// Foreign key referencing the payment record

        public string CarName { get; set; }// Name of the car being rented

        public decimal PenaltyFee { get; set; }
        public decimal Amount { get; set; }
        public string PaymentReference { get; set; }
    }

    public class ReviewBookingDto
    {
        public string NewStatus { get; set; } = string.Empty;// New status for the rental (e.g., Approved, Rejected, Cancelled)
        public string Reason { get; set; } = string.Empty;// Reason for the status change (e.g., reason for rejection or cancellation)
    }

    public class ReviewReturnDto
    {
        public string Action { get; set; }// Action to be taken for the return request (e.g., Approve, Reject)
        public string Reason { get; set; }// Reason for the action taken (e.g., reason for rejection)
    }

    public class BookedDateDto
    {
        // Gamiton nato ang camelCase diri aron inig abot sa React, sakto ang pagbasa niya
        public string startDate { get; set; }
        public string endDate { get; set; }
    }
}
