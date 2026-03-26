using CarRental.IRepository;
using CarRental.Model.Response;
using Microsoft.AspNetCore.SignalR.Protocol;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class AuthService : IAuthRepository
    {
        private readonly SqlConnection conn;

        public AuthService(IConfiguration config)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
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

                string query = @"SELECT Id, FirstName, LastName, Email, PasswordHash, IsVerified
                                    FROM Users WHERE Email = @Email";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", request.Email);

                    var reader = await cmd.ExecuteReaderAsync();

                    if (reader.Read())
                    {
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

                        response.StatusCode = 200;
                        response.Message = "Login successful.";
                        response.Data = new
                        {
                            Id = reader["Id"],
                            FirstName = reader["FirstName"],
                            LastName = reader["LastName"],
                            Email = reader["Email"],
                            IsVerified = isVerified
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
                        string storedCode = reader["VerificationCode"].ToString();
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
                Text = $"Your OTP code is: {code}. It will expire in 1 minute."
            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ninomarmagsipoc@gmail.com", "roen fdiw kwod icav");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }
    }
}