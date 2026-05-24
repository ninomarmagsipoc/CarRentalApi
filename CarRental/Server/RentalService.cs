using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace CarRental.Server
{
    public class RentalService : IRentalRepository
    {
        private readonly SqlConnection conn;
        private readonly INotificationRepository _notificationRepo;
        private readonly IEmailService _emailService;
        private readonly IPaymentRepository _paymentRepo;
        private readonly IWebHostEnvironment _env;

        public RentalService(IConfiguration config, INotificationRepository notificationRepo, IPaymentRepository paymentRepo, IEmailService emailService, IWebHostEnvironment env)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
            _notificationRepo = notificationRepo;
            _emailService = emailService;
            _paymentRepo = paymentRepo;
            _env = env;
        }

        public async Task<ServiceResponse<Rental>> CreateRental(RentalRequest request)
        {
            var response = new ServiceResponse<Rental>();
            try
            {
                if (request.StartDate.Date < DateTime.Today)
                    return new ServiceResponse<Rental> { StatusCode = 400, Message = "Start Date cannot be in the past." };

                if (request.EndDate.Date < request.StartDate.Date)
                    return new ServiceResponse<Rental> { StatusCode = 400, Message = "End Date cannot be earlier than Start Date." };

                string licenseFileName = null;
                if (request.DriverLicense != null)
                {
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "licenses");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                    licenseFileName = $"{Guid.NewGuid()}_{request.DriverLicense.FileName}";
                    using (var stream = new FileStream(Path.Combine(uploadsFolder, licenseFileName), FileMode.Create))
                    {
                        await request.DriverLicense.CopyToAsync(stream);
                    }
                }

                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>("sp_CreateRental", new
                {
                    request.UserID,
                    request.CarID,
                    request.StartDate,
                    request.EndDate,
                    request.FullName,
                    request.ContactNumber,
                    request.PickupLocation,
                    DriverLicense = licenseFileName
                }, commandType: CommandType.StoredProcedure);

                if (result != null && result.StatusCode == 200)
                {
                    response.StatusCode = 200;
                    response.Message = result.Message;
                    response.Data = new Rental
                    {
                        RentalID = result.RentalID,
                        UserID = result.UserID,
                        TotalPrice = result.TotalPrice,
                        Status = result.Status,
                        CreatedAt = result.CreatedAt
                    };
                }
                else
                {
                    response.StatusCode = result?.StatusCode ?? 400;
                    response.Message = result?.Message ?? "Booking Failed";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) await conn.CloseAsync();
            }
            return response;
        }

        public async Task<ServiceResponse<List<Rental>>> GetRentals()
        {
            var response = new ServiceResponse<List<Rental>>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var rentals = await conn.QueryAsync<Rental>(
                    "rent_GetRentals",
                    commandType: CommandType.StoredProcedure
                );

                response.Data = rentals.ToList();
                response.StatusCode = 200;
                response.Message = "Rentals retrieved successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error fetching rentals: " + ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<Rental>> GetRentalById(int id)
        {
            var response = new ServiceResponse<Rental>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var rental = await conn.QueryFirstOrDefaultAsync<Rental>(
                    "rent_GetRentalById",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure
                );

                if (rental != null)
                {
                    response.Data = rental;
                    response.StatusCode = 200;
                    response.Message = "Rental found.";
                }
                else
                {
                    response.StatusCode = 404;
                    response.Message = "Rental not found.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error: " + ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> ReviewBooking(int rentalId, string newStatus, string reason = null)
        {
            var response = new ServiceResponse<bool>();

            try
            {
                if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                var info = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "rent_ReviewBooking",
                    new { RentalID = rentalId, Status = newStatus },
                    commandType: CommandType.StoredProcedure
                );

                if (info != null)
                {
                    int userId = info.UserID;
                    string userEmail = info.Email;
                    string notificationMessage = string.Empty;
                    string emailSubject = "Rental Update";

                    if (newStatus == "Approved")
                    {
                        notificationMessage = "Your rental is approved! Please print this Agreement Paper and present it upon pick-up.";
                        emailSubject = "Rental Approved - Action Required";
                    }
                    else if (newStatus == "Refund Required")
                    {
                        notificationMessage = "Your rental request has been rejected and your down payment will be refunded.";
                        if (!string.IsNullOrEmpty(reason)) notificationMessage += $" Reason: {reason}";
                        emailSubject = "Rental Update - Refund Processing";
                    }

                    if (!string.IsNullOrEmpty(notificationMessage))
                    {
                        await _notificationRepo.CreateNotification(userId, rentalId, notificationMessage);
                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await _emailService.SendEmailAsync(userEmail, emailSubject, notificationMessage);
                        }
                    }

                    response.StatusCode = 200;
                    response.Data = true;
                    response.Message = $"Booking successfully marked as {newStatus}.";
                }
                else
                {
                    response.StatusCode = 404;
                    response.Message = "Rental not found";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally { await conn.CloseAsync(); }

            return response;
        }

        public async Task<ServiceResponse<bool>> CheckAndMarkOverdueRentals()
        {
            var response = new ServiceResponse<bool>();
            try
            {
                if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                var overdueRentals = await conn.QueryAsync<dynamic>(
                    "rent_CheckAndMarkOverdueRentals",
                    commandType: CommandType.StoredProcedure
                );

                var list = overdueRentals.ToList();

                if (list.Count > 0)
                {
                    string message = "Your rental is overdue. Please return the car immediately to avoid penalties.";

                    foreach (var rental in list)
                    {
                        await _notificationRepo.CreateNotification((int)rental.UserID, (int)rental.RentalID, message);
                        await _emailService.SendEmailAsync((string)rental.Email, "Rental Overdue Notice", message);
                    }
                }

                response.StatusCode = 200;
                response.Data = true;
                response.Message = $"{list.Count} rentals marked as overdue.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<object>> ReturnCar(int rentalId)
        {
            var response = new ServiceResponse<object>();
            try
            {
                if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "rent_ReturnCar",
                    new { RentalID = rentalId },
                    commandType: CommandType.StoredProcedure
                );

                if (result != null && result.StatusCode == 200)
                {
                    decimal penaltyFee = (decimal)result.PenaltyFee;
                    int userId = (int)result.UserID;
                    string userEmail = (string)result.Email;

                    string subject, body, notifMsg;

                    if (penaltyFee > 0)
                    {
                        subject = "Late Return Penalty - Action Required";
                        body = $"You returned the car late. Please log in and pay the penalty fee of PHP {penaltyFee:N2} to close your rental.";
                        notifMsg = $"You have a pending penalty of PHP {penaltyFee:N2} for late return. Please pay via your history.";
                    }
                    else
                    {
                        subject = "Rental Completed";
                        body = "Thank you for returning the car on time.";
                        notifMsg = "Thank you for returning the car on time.";
                    }

                    await _notificationRepo.CreateNotification(userId, rentalId, notifMsg);
                    await _emailService.SendEmailAsync(userEmail, subject, body);

                    response.StatusCode = 200;
                    response.Data = new { PenaltyFee = penaltyFee };
                    response.Message = penaltyFee > 0 ? "Penalty applied. Waiting for user payment." : "Car returned successfully.";
                }
                else
                {
                    response.StatusCode = result?.StatusCode ?? 404;
                    response.Message = result?.Message ?? "Rental not found.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error: " + ex.Message;
            }
            finally { await conn.CloseAsync(); }

            return response;
        }

        public async Task<ServiceResponse<bool>> RequestCancellation(int rentalId, int userId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "rent_RequestCancellation",
                    new { RentalID = rentalId, UserID = userId },
                    commandType: CommandType.StoredProcedure
                );

                if (result != null && result.StatusCode == 200)
                {
                    string userName = result.UserName ?? $"User #{userId}";

                    await _notificationRepo.CreateNotification(userId, rentalId,
                        $"You requested cancellation for Rental #{rentalId}. Please wait for review.");

                    await _notificationRepo.CreateNotification(1, rentalId,
                        $"User {userName} requested to cancel Rental #{rentalId}. Review needed.");

                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = result.Message;
                }
                else
                {
                    response.StatusCode = result?.StatusCode ?? 400;
                    response.Message = result?.Message ?? "Action failed.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Error: {ex.Message}";
            }
            finally
            {
                if (conn.State == ConnectionState.Open) await conn.CloseAsync();
            }
            return response;
        }

        public async Task<ServiceResponse<bool>> ReviewCancellation(int rentalId, string action)
        {
            var response = new ServiceResponse<bool>();

            try
            {
                if (action == "Approved")
                {
                    return await _paymentRepo.ProcessCancellationRefunds(rentalId);
                }

                if (action == "Rejected")
                {
                    if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                    var info = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "rent_ReviewCancellationReject",
                        new { RentalID = rentalId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (info != null)
                    {
                        int userId = info.UserID;
                        string userEmail = info.Email;
                        string rejectMsg = "Your cancellation request has been rejected. Your booking remains Approved.";

                        await _notificationRepo.CreateNotification(userId, rentalId, rejectMsg);
                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await _emailService.SendEmailAsync(userEmail, "Cancellation Request Rejected", rejectMsg);
                        }

                        response.Data = true;
                        response.StatusCode = 200;
                        response.Message = "Cancellation request rejected. Rental reverted to Approved.";
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Message = "Rental not found.";
                    }
                    return response;
                }

                response.StatusCode = 400;
                response.Message = "Invalid action.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Error: {ex.Message}";
            }
            finally
            {
                if (conn.State == ConnectionState.Open) await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<List<Rental>>> GetRentalsByUserId(int userId)
        {
            var response = new ServiceResponse<List<Rental>>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var rentals = await conn.QueryAsync<Rental>(
                    "rent_GetRentalsByUserId",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure
                );

                response.Data = rentals.ToList();
                response.StatusCode = 200;
                response.Message = "User rentals retrieved successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = $"Error: {ex.Message}";
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> UpdateRentalStatus(int rentalId, string newStatus)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                var info = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "rent_UpdateRentalStatus",
                    new { RentalID = rentalId, Status = newStatus },
                    commandType: CommandType.StoredProcedure
                );

                if (info != null)
                {
                    int userId = info.UserID;
                    string userEmail = info.Email;

                    string message = newStatus == "On the Way"
                        ? "🚗 Good news! Your rented car is now on the way to your location."
                        : "✅ Your rented car has been delivered. Please note that cancellation is no longer allowed.";

                    string subject = $"Car Rental Update: {newStatus}";

                    await _notificationRepo.CreateNotification(userId, rentalId, message);

                    if (!string.IsNullOrEmpty(userEmail))
                    {
                        await _emailService.SendEmailAsync(userEmail, subject, message);
                    }

                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = $"Status successfully updated to {newStatus}";
                }
                else
                {
                    response.StatusCode = 404;
                    response.Message = "Rental not found";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> RequestReturn(int rentalId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var userId = await conn.QueryFirstOrDefaultAsync<int?>(
                    "rent_RequestReturn",
                    new { RentalID = rentalId },
                    commandType: CommandType.StoredProcedure
                );

                if (userId.HasValue)
                {
                    await _notificationRepo.CreateNotification(1, rentalId, $"User requested to return Rental #{rentalId}. Review needed.");

                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Return request submitted.";
                }
                else
                {
                    response.StatusCode = 404;
                    response.Message = "Rental not found.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<object>> ReviewReturnRequest(int rentalId, string action, string reason = null)
        {
            if (action == "Approved")
            {
                return await ReturnCar(rentalId);
            }

            if (action == "Rejected")
            {
                var response = new ServiceResponse<object>();
                try
                {
                    if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                    var info = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "rent_ReviewReturnRequestReject",
                        new { RentalID = rentalId },
                        commandType: CommandType.StoredProcedure
                    );

                    if (info != null)
                    {
                        int userId = info.UserID;
                        string userEmail = info.Email;
                        string msg = $"Your return request was rejected. Reason: {reason}";

                        await _notificationRepo.CreateNotification(userId, rentalId, msg);

                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await _emailService.SendEmailAsync(userEmail, "Return Request Rejected", msg);
                        }

                        response.Data = true;
                        response.StatusCode = 200;
                        response.Message = "Return rejected. User notified.";
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Message = "Rental not found";
                    }
                }
                catch (Exception ex)
                {
                    response.StatusCode = 500;
                    response.Message = ex.Message;
                }
                finally { if (conn.State == ConnectionState.Open) await conn.CloseAsync(); }

                return response;
            }

            return new ServiceResponse<object> { StatusCode = 400, Message = "Invalid action" };
        }

        public async Task<ServiceResponse<List<BookedDateDto>>> GetBookedDatesForCar(int carId)
        {
            var response = new ServiceResponse<List<BookedDateDto>>();

            try
            {
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                var bookedDates = await conn.QueryAsync<BookedDateDto>(
                    "sp_GetBookedDatesForCar",
                    new { CarID = carId },
                    commandType: CommandType.StoredProcedure
                );

                response.Data = bookedDates.ToList();
                response.StatusCode = 200;
                response.Message = "Booked dates fetched successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> MoveToTrash(int rentalId)
        {
            var response = new ServiceResponse<bool>();

            try
            {
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                int rows = await conn.ExecuteAsync(
                    "rent_MoveToTrash",
                    new { RentalID = rentalId },
                    commandType: CommandType.StoredProcedure
                );

                if (rows > 0)
                {
                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Rental moved to trash.";
                }
                else
                {
                    response.Data = false;
                    response.StatusCode = 404;
                    response.Message = "Rental not found.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> ArchiveRental(int rentalId)
        {
            var response = new ServiceResponse<bool>();

            try
            {
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                int rows = await conn.ExecuteAsync(
                    "rent_ArchiveRental",
                    new { RentalID = rentalId },
                    commandType: CommandType.StoredProcedure
                );

                if (rows > 0)
                {
                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Rental successfully moved to Archive.";
                }
                else
                {
                    response.Data = false;
                    response.StatusCode = 404;
                    response.Message = "Rental not found.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<bool>> HideRentalPermanently(int rentalId)
        {
            var response = new ServiceResponse<bool>();

            try
            {
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                int rows = await conn.ExecuteAsync(
                    "rent_HideRentalPermanently",
                    new { RentalID = rentalId },
                    commandType: CommandType.StoredProcedure
                );

                if (rows > 0)
                {
                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Rental permanently hidden from user.";
                }
                else
                {
                    response.Data = false;
                    response.StatusCode = 404;
                    response.Message = "Rental not found.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }

            return response;
        }
        public async Task<ServiceResponse<bool>> RestoreRental(int rentalId)
        {
            var response = new ServiceResponse<bool>();
            try
            {
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                int rows = await conn.ExecuteAsync(
                    "sp_RestoreRental",
                    new { RentalID = rentalId },
                    commandType: CommandType.StoredProcedure
                );

                if (rows > 0)
                {
                    response.Data = true;
                    response.StatusCode = 200;
                    response.Message = "Rental successfully restored.";
                }
                else
                {
                    response.Data = false;
                    response.StatusCode = 404;
                    response.Message = "Rental not found.";
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                if (conn.State == System.Data.ConnectionState.Open)
                {
                    await conn.CloseAsync();
                }
            }

            return response;
        }
    }
}
