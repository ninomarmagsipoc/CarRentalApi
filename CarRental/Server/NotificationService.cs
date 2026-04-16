using Microsoft.Data.SqlClient;
using CarRental.IRepository;
using CarRental.Model;

namespace CarRental.Server
{
    public class NotificationService : INotificationRepository
    {
        private readonly string _connectionString;

        public NotificationService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("CarRental")!;
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
            return await cmd.ExecuteNonQueryAsync() > 0;
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