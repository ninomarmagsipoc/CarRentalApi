using CarRental.Hub;
using CarRental.IRepository;
using CarRental.Model;
using Microsoft.AspNetCore.SignalR; 
using Microsoft.Data.SqlClient;
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
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"INSERT INTO Notifications (UserID, RentalID, Message, IsRead, CreatedAt) 
                                VALUES (@UserID, @RentalID, @Message, 0, GETDATE())";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserID", userId);
            cmd.Parameters.AddWithValue("@RentalID", rentalId);
            cmd.Parameters.AddWithValue("@Message", message);

            await conn.OpenAsync();
            bool isSaved = await cmd.ExecuteNonQueryAsync() > 0;

            if (isSaved)
            {
                const string countSql = "SELECT COUNT(*) FROM Notifications WHERE UserID = @UserID AND IsRead = 0";
                using var countCmd = new SqlCommand(countSql, conn);
                countCmd.Parameters.AddWithValue("@UserID", userId);

                int unreadCount = (int)await countCmd.ExecuteScalarAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveNotification", unreadCount);
            }

            return isSaved;
        }

        public async Task<IEnumerable<Notification>> GetUserNotifications(int userId)
        {
            var list = new List<Notification>();
            using var conn = new SqlConnection(_connectionString);
            const string sql = "SELECT * FROM Notifications WHERE UserID = @UserID ORDER BY CreatedAt DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserID", userId);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new Notification
                {
                    NotificationID = reader.GetInt32(0),
                    UserID = reader.GetInt32(1),
                    RentalID = reader.GetInt32(2),
                    Message = reader.GetString(3),
                    IsRead = reader.GetBoolean(4),
                    CreatedAt = reader.GetDateTime(5)
                });
            }
            return list;
        }

        public async Task<bool> MarkAsRead(int notificationId)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = "UPDATE Notifications SET IsRead = 1 WHERE NotificationID = @ID";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ID", notificationId);

            await conn.OpenAsync();
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
    }
}