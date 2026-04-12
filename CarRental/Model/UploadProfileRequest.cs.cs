namespace CarRental.Model
{
    public class UploadProfileRequest
    {
        public int UserId { get; set; }
        public IFormFile File { get; set; }
    }
}
