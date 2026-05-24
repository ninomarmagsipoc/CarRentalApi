using CarRental.IRepository;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

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
                // Check every 1 minute
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task DeleteAbandonedBookings()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    int affected = await conn.ExecuteAsync(
                        "sp_DeleteAbandonedBookings",
                        commandType: CommandType.StoredProcedure
                    );

                    if (affected > 0)
                    {
                        Console.WriteLine($"Automatically deleted {affected} rows from abandoned initial checkouts (5-minute rule).");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteAbandonedBookings: {ex.Message}");
            }
        }

        private async Task RejectExpiredRentals()
        {
            using var scope = _serviceProvider.CreateScope();

            var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var toRefund = await conn.QueryAsync<dynamic>(
                    "exp_GetExpiredRentalsForRefund",
                    commandType: CommandType.StoredProcedure
                );

                foreach (var item in toRefund)
                {
                    int paymentId = item.PaymentID;
                    int rentalId = item.RentalID;
                    int userId = item.UserID;

                    try
                    {
                        var refundResult = await paymentRepo.RefundPayment(paymentId);

                        if (refundResult.StatusCode == 200)
                        {
                            await conn.ExecuteAsync(
                                "exp_UpdateRentalToCancelled",
                                new { RentalID = rentalId },
                                commandType: CommandType.StoredProcedure
                            );

                            await notificationRepo.CreateNotification(
                                userId,
                                rentalId,
                                "Your rental timed out. Your downpayment has been automatically refunded."
                            );

                            Console.WriteLine($"[AUTO-REFUND] Successfully processed Refund and Cancelled Rental #{rentalId}");
                        }
                        else
                        {
                            Console.WriteLine($"[AUTO-REFUND-ERROR] Failed to refund Rental #{rentalId}: {refundResult.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CRITICAL-ERROR] Auto-refund loop failed for Rental #{rentalId}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL-ERROR] Database check failed in RejectExpiredRentals: {ex.Message}");
            }
        }
    }
}