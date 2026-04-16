using CarRental.Model;

namespace CarRental.IRepository
{
    public interface INotificationRepository
    {
        Task<bool> CreateNotification(int userId, int rentalId, string message);
        Task<IEnumerable<Notification>> GetUserNotifications(int userId);
        Task<bool> MarkAsRead(int notificationId);
    }
}
