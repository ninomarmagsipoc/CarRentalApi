namespace CarRental.Model
{

    public class FavoriteRequest
    {
        public int UserId { get; set; }
        public int CarId { get; set; }
    }

    public class CarRequest
    {
        public string CarName { get; set; }
        public string CarInfo { get; set; }
        public int Seats { get; set; }
        public decimal PricePerDay { get; set; }
        public IFormFile? ImageFile { get; set; }
        public string? MaintenanceMonth { get; set; }
    }

    public class CarBookingDTO
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; }
    }

    public class CarModel
    {
        public int CarID { get; set; }
        public string CarName { get; set; }
        public string CarInfo { get; set; }
        public int Seats { get; set; }
        public decimal PricePerDay { get; set; }
        public string CarImage { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsHidden { get; set; }
    }

}
