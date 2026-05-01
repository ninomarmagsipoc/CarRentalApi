using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using Microsoft.AspNetCore.SignalR.Protocol;
using System.Data.SqlClient;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace CarRental.Server
{
    public class AuthService : IAuthRepository
    {
        private readonly SqlConnection conn;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public AuthService(IConfiguration config, IWebHostEnvironment env)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
            _env = env;
            _config = config;
        }

        public async Task<ServiceResponse<object>> Register(RegisterRequest request)
        {
            var response = new ServiceResponse<object>();

            try
            {
                if (string.IsNullOrWhiteSpace(request.FirstName) ||
                    string.IsNullOrWhiteSpace(request.LastName) ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    response.StatusCode = 400;
                    response.Message = "All fields are required.";
                    return response;
                }

                if (!request.Email.Contains("@"))
                {
                    response.StatusCode = 400;
                    response.Message = "Invalid email format.";
                    return response;
                }

                if (request.Password.Length < 6)
                {
                    response.StatusCode = 400;
                    response.Message = "Password must be at least 6 characters.";
                    return response;
                }

                await conn.OpenAsync();

                string check = "SELECT COUNT(*) FROM Users WHERE Email = @Email";
                using (SqlCommand cmd = new SqlCommand(check, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", request.Email);
                    int count = (int)await cmd.ExecuteScalarAsync();

                    if (count > 0)
                    {
                        response.StatusCode = 400;
                        response.Message = "Email already exists.";
                        return response;
                    }
                }

                string hash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                string insert = "INSERT INTO Users (FirstName, LastName, Email, PasswordHash)" +
                    " VALUES (@FirstName, @LastName, @Email, @PasswordHash)";

                using (SqlCommand cmd = new SqlCommand(insert, conn))
                {
                    cmd.Parameters.AddWithValue("@FirstName", request.FirstName);
                    cmd.Parameters.AddWithValue("@LastName", request.LastName);
                    cmd.Parameters.AddWithValue("@Email", request.Email);
                    cmd.Parameters.AddWithValue("@PasswordHash", hash);

                    await cmd.ExecuteNonQueryAsync();
                }

                string code = new Random().Next(100000, 999999).ToString();

               
                string otpQuery = @"UPDATE Users 
                    SET VerificationCode = @Code, VerificationExpiry = @Expiry 
                    WHERE Email = @Email";

                using (SqlCommand otpCmd = new SqlCommand(otpQuery, conn))
                {
                    otpCmd.Parameters.AddWithValue("@Code", code);
                    otpCmd.Parameters.AddWithValue("@Expiry", DateTime.UtcNow.AddMinutes(1));
                    otpCmd.Parameters.AddWithValue("@Email", request.Email);
                    await otpCmd.ExecuteNonQueryAsync();
                }

                await SendOtpEmail(request.Email, code);

                response.StatusCode = 200;
                response.Message = "User registered successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "An error occurred: " + ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<object>> Login(LoginRequest request)
        {
            var response = new ServiceResponse<object>();

            try
            {
                if (string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    response.StatusCode = 400;
                    response.Message = "Email and password are required.";
                    return response;
                }

                await conn.OpenAsync();

                string query = @"SELECT Id, FirstName, LastName, Email, PasswordHash, IsVerified, Role, ProfileImage
                                    FROM Users WHERE Email = @Email AND IsBlocked = 0";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", request.Email);

                    var reader = await cmd.ExecuteReaderAsync();

                    if (reader.Read())
                    {
                        string role = reader["Role"].ToString();
                        string hash = reader["PasswordHash"].ToString();

                        bool valid = BCrypt.Net.BCrypt.Verify(request.Password, hash);

                        if (!valid)
                        {
                            response.StatusCode = 401;
                            response.Message = "Invalid password.";
                            return response;
                        }

                        if (!request.Email.Contains("@"))
                        {
                            response.StatusCode = 400;
                            response.Message = "Invalid email format.";
                            return response;
                        }

                        

                        bool isVerified = (bool)reader["IsVerified"];

                        if (!isVerified)
                        {
                            response.StatusCode = 400;
                            response.Message = "Email is not Verified, Please Verify it";
                            response.Data = new { IsVerified = false };
                            return response;
                        }

                        string token = CreateToken(reader["Id"].ToString(), reader["Email"].ToString(), role);

                        response.StatusCode = 200;
                        response.Message = "Login successful.";
                        response.Data = new
                        {
                            Token = token,
                            Id = reader["Id"],
                            FirstName = reader["FirstName"],
                            LastName = reader["LastName"],
                            Email = reader["Email"],
                            IsVerified = isVerified,
                            Role = role,
                            ProfileImage = reader["ProfileImage"] == DBNull.Value ? "https://i.pravatar.cc/150" : reader["ProfileImage"].ToString()
                        };
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Message = "User not found.";
                    }
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = ex.Message;
            }
            finally
            {
                conn.Close();
            }
            return response;
        }

        private string CreateToken(string id, string email, string role)
        {
            List<Claim> claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, id),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            };

            // Grabs the secret key from your appsettings.json
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddHours(4),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<ServiceResponse<object>> SendOtp(string email)
        {
            var response = new ServiceResponse<object>();
            try
            {
                await conn.OpenAsync();

                string code = new Random().Next(100000, 999999).ToString();

                string query = @"UPDATE Users SET VerificationCode = @Code, VerificationExpiry = @Expiry WHERE Email = @Email";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Code", code);
                    cmd.Parameters.AddWithValue("@Expiry", DateTime.UtcNow.AddMinutes(1));
                    cmd.Parameters.AddWithValue("@Email", email);
                    int rows = await cmd.ExecuteNonQueryAsync();
                    if (rows == 0)
                    {
                        response.StatusCode = 404;
                        response.Message = "User not found.";
                        return response;
                    }
                }

                await SendOtpEmail(email, code);

                response.StatusCode = 200;
                response.Message = "OTP sent successfully.";
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

        public async Task<ServiceResponse<object>> VerifyOtp(string email, string code)
        {
            var response = new ServiceResponse<object>();
            try
            {
                await conn.OpenAsync();
                string query = @"SELECT VerificationCode, VerificationExpiry FROM Users WHERE Email = @Email";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", email);
                    var reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read())
                    {
                        string storedCode = reader["VerificationCode"]?.ToString();
                        DateTime expiry = reader["VerificationExpiry"] == DBNull.Value
                    ? DateTime.MinValue
                    : (DateTime)reader["VerificationExpiry"];
                        if (DateTime.UtcNow > expiry)
                        {
                            response.StatusCode = 400;
                            response.Message = "OTP has expired.";
                            return response;
                        }
                        if (storedCode != code)
                        {
                            response.StatusCode = 400;  
                            response.Message = "Invalid OTP.";
                            return response;
                        }

                        reader.Close();

                        string update = @"UPDATE Users 
                  SET IsVerified = 1, VerificationCode = NULL, VerificationExpiry = NULL 
                  WHERE Email = @Email"; ;

                        using (SqlCommand updateCmd = new SqlCommand(update, conn))
                        {
                            updateCmd.Parameters.AddWithValue("@Email", email);
                            await updateCmd.ExecuteNonQueryAsync();
                        }

                        response.StatusCode = 200;
                        response.Message = "OTP verified successfully.";
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Message = "User not found.";
                    }
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

        public async Task SendOtpEmail(string email, string code)
        {
            var message = new MimeKit.MimeMessage();
            message.From.Add(new MimeKit.MailboxAddress("Car Rental", "ninomarmagsipoc@gmail.com"));
            message.To.Add(new MimeKit.MailboxAddress("", email));
            message.Subject = "Your OTP Code";

            message.Body = new MimeKit.TextPart("plain")
            {
                Text = $"Your OTP code is: {code}. It will expire in 5 minute."
            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ninomarmagsipoc@gmail.com", "roen fdiw kwod icav");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

        public async Task<ServiceResponse<object>> SendResetOtp(string email)
        {

            var response = new ServiceResponse<object>();

            try
            {
                await conn.OpenAsync();

                string code = new Random().Next(100000, 999999).ToString();

                string query = @"UPDATE Users SET ResetToken = @Code, ResetTokenExpiry = @Expiry WHERE Email = @Email";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {

                    cmd.Parameters.AddWithValue("@Code", code);
                    cmd.Parameters.AddWithValue("@Expiry", DateTime.UtcNow.AddMinutes(5));
                    cmd.Parameters.AddWithValue("@Email", email);

                    int rows = await cmd.ExecuteNonQueryAsync();

                    if (rows == 0)
                    {
                        response.StatusCode = 404;
                        response.Message = "User not found.";
                        return response;
                    }
                }

                await ResetPassSendOtpEmail(email, code);

                response.StatusCode = 200;
                response.Message = "Password reset OTP sent successfully.";
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

        public async Task<ServiceResponse<object>> ResetPassword(string email, string code, string newPassword)
        {
            var response = new ServiceResponse<object>();

            try
            {
                await conn.OpenAsync();

                string query = @"SELECT ResetToken, ResetTokenExpiry FROM Users WHERE Email = @Email";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", email);

                    var reader = await cmd.ExecuteReaderAsync();

                    if (!reader.Read())
                    {
                        response.StatusCode = 404;
                        response.Message = "User not found.";
                        return response;
                    }

                    string storedCode = reader["ResetToken"]?.ToString();
                    DateTime expiry = reader["ResetTokenExpiry"] == DBNull.Value ?
                        DateTime.MinValue :
                        (DateTime)reader["ResetTokenExpiry"];

                    if (DateTime.UtcNow > expiry)
                    {
                        reader.Close();
                        response.StatusCode = 400;
                        response.Message = "Reset token has expired.";
                        return response;
                    }

                    if (storedCode != code)
                    {
                        reader.Close();
                        response.StatusCode = 400;
                        response.Message = "Invalid reset token.";
                        return response;
                    }

                    reader.Close();

                    if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                    {
                        response.StatusCode = 400;
                        response.Message = "Password must be at least 6 characters.";
                        return response;
                    }

                    string hash = BCrypt.Net.BCrypt.HashPassword(newPassword);

                    string update = @"UPDATE Users 
                        SET PasswordHash = @Hash, ResetToken = NULL, ResetTokenExpiry = NULL, UpdatedAt = @UpdatedAt 
                        WHERE Email = @Email";

                    using (SqlCommand updateCmd = new SqlCommand(update, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@Hash", hash);
                        updateCmd.Parameters.AddWithValue("@Email", email);
                        updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);

                        await updateCmd.ExecuteNonQueryAsync();
                    }

                    response.StatusCode = 200;
                    response.Message = "Password reset successfully.";
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

        public async Task ResetPassSendOtpEmail(string email, string code)
        {
            var message = new MimeKit.MimeMessage();
            message.From.Add(new MimeKit.MailboxAddress("Car Rental", "ninomarmagsipoc@gmail.com"));
            message.To.Add(new MimeKit.MailboxAddress("", email));
            message.Subject = "Your OTP Code";

            message.Body = new MimeKit.TextPart("plain")
            {
                Text = $"Your OTP code is: {code}. Reset your password now It will expire in 5 minute."
            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ninomarmagsipoc@gmail.com", "roen fdiw kwod icav");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

        public async Task<ServiceResponse<UploadProfileResponse>> UploadProfile(UploadProfileRequest request)
        {
            var response = new ServiceResponse<UploadProfileResponse>();

            try 
            {
                if (request.File == null || request.File.Length == 0)
                {
                    response.StatusCode = 400;
                    response.Message = "No file uploaded.";
                    return response;
                }

                var allowedTypes = new[] { "image/jpeg", "image/png", "image/jpg" };
                if (!allowedTypes.Contains(request.File.ContentType))
                {
                    response.StatusCode = 400;
                    response.Message = "Only JPG and PNG allowed.";
                    return response;
                }

                var uploadPath = Path.Combine(_env.WebRootPath, "upload");
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                }

                var fileName = $"{Guid.NewGuid()}_{request.File.FileName}";
                var filePath = Path.Combine(uploadPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                string imageUrl = $"/upload/{fileName}";

                await conn.OpenAsync();

                string query = "UPDATE Users Set ProfileImage = @Image Where Id = @Id";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Image", imageUrl);
                    cmd.Parameters.AddWithValue("@Id", request.UserId);

                    await cmd.ExecuteNonQueryAsync();
                }
                response.StatusCode = 200;
                response.Message = "Profile Uploaded Successfully";
                response.Data = new UploadProfileResponse
                {
                    ImageUrl = imageUrl
                };
            } catch (Exception ex)
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

        public async Task<ServiceResponse<object>> UpdateProfile(UpdateProfileRequest request)
        {
            var response = new ServiceResponse<object>();
            try
            {
                await conn.OpenAsync();

                string verifyQuery = @"SELECT VerificationCode, VerificationExpiry, PasswordHash FROM Users WHERE Email = @Email";
                string storedHash = "";

                using (SqlCommand cmd = new SqlCommand(verifyQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", request.Email);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!reader.Read())
                        {
                            response.StatusCode = 404; response.Message = "User not found."; return response;
                        }

                        string storedCode = reader["VerificationCode"]?.ToString();
                        DateTime expiry = reader["VerificationExpiry"] == DBNull.Value ? DateTime.MinValue : (DateTime)reader["VerificationExpiry"];
                        storedHash = reader["PasswordHash"]?.ToString(); 

                        if (storedCode != request.OtpCode)
                        {
                            response.StatusCode = 400; response.Message = "Invalid OTP."; return response;
                        }
                        if (DateTime.UtcNow > expiry)
                        {
                            response.StatusCode = 400; response.Message = "OTP has expired. Please request a new one."; return response;
                        }
                    }
                }

                var updateClauses = new List<string>();
                using (SqlCommand updateCmd = new SqlCommand())
                {
                    updateCmd.Connection = conn;

                    if (!string.IsNullOrWhiteSpace(request.FirstName))
                    {
                        updateClauses.Add("FirstName = @FN");
                        updateCmd.Parameters.AddWithValue("@FN", request.FirstName);
                    }

                    if (!string.IsNullOrWhiteSpace(request.LastName))
                    {
                        updateClauses.Add("LastName = @LN");
                        updateCmd.Parameters.AddWithValue("@LN", request.LastName);
                    }

                    if (!string.IsNullOrWhiteSpace(request.NewPassword))
                    {
                        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                        {
                            response.StatusCode = 400; response.Message = "Please enter your current password to set a new one."; return response;
                        }

                        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, storedHash))
                        {
                            response.StatusCode = 400; response.Message = "Incorrect current password. Cannot change password."; return response;
                        }

                        updateClauses.Add("PasswordHash = @Hash");
                        updateCmd.Parameters.AddWithValue("@Hash", BCrypt.Net.BCrypt.HashPassword(request.NewPassword));
                    }

                    if (updateClauses.Count == 0)
                    {
                        response.StatusCode = 400; response.Message = "No changes requested."; return response;
                    }

                    updateClauses.Add("VerificationCode = NULL");
                    updateClauses.Add("VerificationExpiry = NULL");

                    string updateQuery = $"UPDATE Users SET {string.Join(", ", updateClauses)} WHERE Id = @Id";

                    updateCmd.CommandText = updateQuery;
                    updateCmd.Parameters.AddWithValue("@Id", request.UserId);

                    await updateCmd.ExecuteNonQueryAsync();
                }

                response.StatusCode = 200;
                response.Message = "Profile updated successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500; response.Message = ex.Message;
            }
            finally
            {
                await conn.CloseAsync();
            }
            return response;
        }
        public async Task<ServiceResponse<List<CustomerDto>>> GetAllCustomers()
        {
            var response = new ServiceResponse<List<CustomerDto>>();
            var customers = new List<CustomerDto>();

            string query = @"
        SELECT u.Id, u.FirstName, u.LastName, u.Email, u.ProfileImage, u.IsVerified, u.IsBlocked,
               (SELECT COUNT(*) FROM Rentals r WHERE r.UserId = u.Id AND r.Status = 'Returned') AS TotalRentals
        FROM Users u
        WHERE u.Role != 'Admin'"; 

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                await conn.OpenAsync();
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        customers.Add(new CustomerDto
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            FirstName = reader["FirstName"].ToString()!,
                            LastName = reader["LastName"].ToString()!,
                            Email = reader["Email"].ToString()!,
                            ProfileImage = reader["ProfileImage"] != DBNull.Value ? reader["ProfileImage"].ToString()! : null,
                            IsVerified = Convert.ToBoolean(reader["IsVerified"]),
                            IsBlocked = Convert.ToBoolean(reader["IsBlocked"]),
                            TotalSuccessfulRentals = Convert.ToInt32(reader["TotalRentals"])
                        });
                    }
                }
                await conn.CloseAsync();
            }
            response.Data = customers;
            response.StatusCode = 200;
            return response;
        }

        public async Task<ServiceResponse<string>> ToggleBlockUser(int userId, bool isBlocked)
        {
            var response = new ServiceResponse<string>();
            string query = "UPDATE Users SET IsBlocked = @IsBlocked WHERE Id = @Id AND Role != 'Admin'";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@IsBlocked", isBlocked);
                cmd.Parameters.AddWithValue("@Id", userId);

                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();
                await conn.CloseAsync();
            }

            response.StatusCode = 200;
            response.Message = isBlocked ? "User blocked successfully." : "User unblocked successfully.";
            return response;
        }

        public async Task<ServiceResponse<List<CustomerRentalHistoryDto>>> GetCustomerRentalHistory(int userId)
        {
            var response = new ServiceResponse<List<CustomerRentalHistoryDto>>();
            var history = new List<CustomerRentalHistoryDto>();

            string query = @"
        SELECT r.RentalID, c.CarName, c.CarImage, r.StartDate, r.EndDate, r.TotalPrice
        FROM Rentals r
        JOIN Cars c ON r.CarID = c.CarId
        WHERE r.UserId = @UserId AND r.Status = 'Returned'";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                await conn.OpenAsync();
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        history.Add(new CustomerRentalHistoryDto
                        {
                            RentalId = Convert.ToInt32(reader["RentalID"]),
                            CarName = reader["CarName"].ToString()!,
                            CarImage = reader["CarImage"] != DBNull.Value ? reader["CarImage"].ToString()! : null,
                            StartDate = Convert.ToDateTime(reader["StartDate"]),
                            EndDate = Convert.ToDateTime(reader["EndDate"]),
                            TotalAmount = Convert.ToDecimal(reader["TotalPrice"])
                        });
                    }
                }
                await conn.CloseAsync();
            }
            response.Data = history;
            response.StatusCode = 200;
            return response;
        }

    }
}