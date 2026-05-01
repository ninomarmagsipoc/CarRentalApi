namespace CarRental.Model.Response
{
    public class CustomerDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string ProfileImage { get; set; }
        public bool IsVerified { get; set; }
        public bool IsBlocked { get; set; }
        public int TotalSuccessfulRentals { get; set; }
    }
    public class CustomerRentalHistoryDto
    {
        public int RentalId { get; set; }
        public string CarName { get; set; }
        public string CarImage { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
