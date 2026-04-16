using Microsoft.AspNetCore.Mvc;
using CarRental.Model.Response;
using CarRental.IRepository;

namespace CarRental.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationRepository _notificationService;

        public NotificationController(INotificationRepository notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet("user/{userId}")]
        public async Task<ActionResult<ServiceResponse<IEnumerable<NotificationResponse>>>> GetUserNotifications(int userId)
        {
            var notifications = await _notificationService.GetUserNotifications(userId);

            var response = new ServiceResponse<IEnumerable<NotificationResponse>>
            {
                StatusCode = 200,
                Message = "Notifications retrieved successfully.",
                Data = notifications.Select(n => new NotificationResponse
                {
                    NotificationID = n.NotificationID,
                    UserID = n.UserID,
                    RentalID = n.RentalID,
                    Message = n.Message,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
            };

            return Ok(response);
        }

        [HttpPut("read/{notificationId}")]
        public async Task<ActionResult<ServiceResponse<bool>>> MarkAsRead(int notificationId)
        {
            var result = await _notificationService.MarkAsRead(notificationId);

            return Ok(new ServiceResponse<bool>
            {
                StatusCode = result ? 200 : 404,
                Message = result ? "Notification marked as read." : "Notification not found.",
                Data = result
            });
        }
    }
}