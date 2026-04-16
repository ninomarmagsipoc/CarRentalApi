namespace CarRental.Model.Response
{
    public class NotificationResponse
    {
        public int NotificationID { get; set; }
        public int UserID { get; set; }
        public int RentalID { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
