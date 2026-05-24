using CarRental.Hub;
using CarRental.IRepository;
using CarRental.Model;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Data;
namespace CarRental.Server
{
    public class NotificationService : INotificationRepository
    {
        private readonly string _connectionString;
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(IConfiguration configuration, IHubContext<NotificationHub> hubContext)
        {
            _connectionString = configuration.GetConnectionString("CarRental")!;
            _hubContext = hubContext;
        }

        public async Task<bool> CreateNotification(int userId, int rentalId, string message)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    int unreadCount = await conn.ExecuteScalarAsync<int>(
                        "noti_CreateNotification",
                        new
                        {
                            UserID = userId,
                            RentalID = rentalId,
                            Message = message
                        },
                        commandType: CommandType.StoredProcedure
                    );

                    if (userId == 1)
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveAdminNotification", unreadCount);
                    }
                    else
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveUserNotification", userId, unreadCount);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Notification Error: {ex.Message}");
                return false;
            }
        }
        public async Task<IEnumerable<Notification>> GetUserNotifications(int userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var notifications = await conn.QueryAsync<Notification>(
                    "noti_GetUserNotifications",
                    new { UserID = userId },
                    commandType: CommandType.StoredProcedure
                );

                return notifications;
            }
        }

        public async Task<bool> MarkAsRead(int notificationId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    int rows = await conn.ExecuteAsync(
                        "sp_MarkNotificationAsRead",
                        new { NotificationID = notificationId },
                        commandType: CommandType.StoredProcedure
                    );

                    return rows > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MarkAsRead: {ex.Message}");
                return false;
            }
        }
    }
}