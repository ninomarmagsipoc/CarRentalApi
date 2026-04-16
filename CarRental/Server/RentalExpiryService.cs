using CarRental.IRepository;
using Microsoft.Data.SqlClient;

namespace CarRental.Server
{
    public class RentalExpiryService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly string _connectionString;

        public RentalExpiryService(IServiceProvider serviceProvider, IConfiguration config)
        {
            _serviceProvider = serviceProvider;
            _connectionString = config["ConnectionStrings:CarRental"]!;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await DeleteAbandonedBookings();

                await RejectExpiredRentals();
                // Check every 1 minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task DeleteAbandonedBookings()
        {
            using var conn = new SqlConnection(_connectionString);

            // Delete payments first to prevent Foreign Key errors, then delete the rental
            const string query = @"
                -- Step 1: Delete associated abandoned payments
                DELETE FROM Payment 
                WHERE RentalID IN (
                    SELECT RentalID FROM Rentals 
                    WHERE Status = 'Pending' AND CreatedAt < DATEADD(MINUTE, -5, GETDATE())
                );

                -- Step 2: Delete the abandoned rental records
                DELETE FROM Rentals 
                WHERE Status = 'Pending' AND CreatedAt < DATEADD(MINUTE, -5, GETDATE());
            ";

            await conn.OpenAsync();
            using var cmd = new SqlCommand(query, conn);
            int affected = await cmd.ExecuteNonQueryAsync();

            if (affected > 0)
            {
                // Note: affected counts rows deleted from both tables
                Console.WriteLine($"Automatically deleted abandoned initial checkouts (5-minute rule).");
            }
        }

        private async Task RejectExpiredRentals()
        {
            using var scope = _serviceProvider.CreateScope();
            // Resolve the services needed for the automated refund
            var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            //Identify rentals that are Approved but haven't paid the 'Full' balance within 30 mins
            const string selectQuery = @"
            SELECT p.PaymentID, r.RentalID, r.UserID 
            FROM Rentals r
            JOIN Payment p ON r.RentalID = p.RentalID
                WHERE r.Status = 'Approved' 
            AND r.UpdatedAt < DATEADD(MINUTE, -2, GETDATE())
            AND p.PaymentType = 'Partial' 
            AND p.PaymentStatus = 'Completed'
            AND r.RentalID NOT IN (
                SELECT RentalID FROM Payment WHERE PaymentType = 'Full' AND PaymentStatus = 'Completed'
            )";

            var toRefund = new List<(int PaymentId, int RentalId, int UserId)>();

            using (var cmd = new SqlCommand(selectQuery, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    toRefund.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
                }
            }

            foreach (var item in toRefund)
            {
                try
                {
                    // This handles the PayMongo API call and updates DB status to 'Refunded'
                    var refundResult = await paymentRepo.RefundPayment(item.PaymentId);

                    if (refundResult.StatusCode == 200)
                    {
                        const string updateRentalQuery = "UPDATE Rentals SET Status = 'Cancelled' WHERE RentalID = @RentalID";
                        using (var updateCmd = new SqlCommand(updateRentalQuery, conn))
                        {
                            updateCmd.Parameters.AddWithValue("@RentalID", item.RentalId);
                            await updateCmd.ExecuteNonQueryAsync();
                        }

                        //Send a specific "Automatic Refund" notification
                        await notificationRepo.CreateNotification(item.UserId, item.RentalId,
                            "Your rental timed out. Your downpayment has been automatically refunded.");

                        Console.WriteLine($"[AUTO-REFUND] Successfully processed Refund and Cancelled Rental #{item.RentalId}");
                    }
                    else
                    {
                        // If PayMongo fails (e.g., sk_test issues), log it for manual admin intervention
                        Console.WriteLine($"[AUTO-REFUND-ERROR] Failed to refund Rental #{item.RentalId}: {refundResult.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL-ERROR] Auto-refund loop failed for Rental #{item.RentalId}: {ex.Message}");
                }
            }
        }
    }
}