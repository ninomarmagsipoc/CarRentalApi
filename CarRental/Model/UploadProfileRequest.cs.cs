namespace CarRental.Model
{
    public class UploadProfileRequest
    {
        public int UserId { get; set; }
        public IFormFile File { get; set; }
    }

    public class UpdateProfileRequest
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? NewPassword { get; set; }
        public string OtpCode { get; set; }

        public string CurrentPassword { get; set; }
    }
}
