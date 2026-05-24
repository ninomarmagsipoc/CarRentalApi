using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Dapper;
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

                var p = new DynamicParameters();
                p.Add("@Email", request.Email);
                p.Add("@EmailCount", dbType: DbType.Boolean, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("sp_CheckEmailExists", p, commandType: CommandType.StoredProcedure);

                int emailExists = p.Get<int>("@EmailCount");

                if (emailExists > 0)
                {
                    response.StatusCode = 400;
                    response.Message = "Email already exists!";
                    return response;
                }

                string hash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                var reg = new
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    PasswordHash = hash
                };

                await conn.ExecuteAsync("sp_InsetUser", reg, commandType: CommandType.StoredProcedure);

                string code = new Random().Next(100000, 999999).ToString();


                var sendOTP = new
                {
                    Code = code,
                    Expiry = DateTime.UtcNow.AddMinutes(1),
                    Email = request.Email
                };

                await conn.ExecuteAsync("sp_UpdateUserOTP", sendOTP, commandType: CommandType.StoredProcedure);

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

                var user = await conn.QueryFirstOrDefaultAsync<User>("sp_Login",
                    new { Email = request.Email }, commandType: CommandType.StoredProcedure
                    );
                if (user == null)
                {
                    response.StatusCode = 404;
                    response.Message = "User not found.";
                    return response;
                }

                bool valid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
                if (!valid)
                {
                    response.StatusCode = 401;
                    response.Message = "Invalid password.";
                    return response;
                }

                if (!user.IsVerified)
                {
                    response.StatusCode = 400;
                    response.Message = "Email is not Verified, Please Verify it";
                    response.Data = new { IsVerified = false };
                    return response;
                }

                string token = CreateToken(user.Id.ToString(), user.Email, user.Role);

                response.StatusCode = 200;
                response.Message = "Login successful.";
                response.Data = new
                {
                    Token = token,
                    Id = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    IsVerified = user.IsVerified,
                    Role = user.Role,
                    ProfileImage = string.IsNullOrEmpty(user.ProfileImage) ? "https://i.pravatar.cc/150" : user.ProfileImage
                };
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

                var paramerter = new
                {
                    Code = code,
                    Expiry = DateTime.UtcNow.AddMinutes(1),
                    Email = email
                };

                int rowAffected = await conn.ExecuteAsync("sp_UpdateUserOTP", paramerter, commandType: CommandType.StoredProcedure);

                if (rowAffected == 0)
                {
                    response.StatusCode = 404;
                    response.Message = "User not found";
                    return response;
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
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_VerifyUserOtp",
                    new { Email = email, Code = code },
                    commandType: CommandType.StoredProcedure
                );

                if (result != null)
                {
                    response.StatusCode = (int)result.StatusCode;
                    response.Message = (string)result.Message;
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
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                string code = new Random().Next(100000, 999999).ToString();

                int rowsAffected = await conn.ExecuteScalarAsync<int>(
                    "sp_UpdateResetOtp",
                    new
                    {
                        Email = email,
                        Code = code,
                        Expiry = DateTime.UtcNow.AddMinutes(5)
                    },
                    commandType: CommandType.StoredProcedure
                );

                if (rowsAffected == 0)
                {
                    response.StatusCode = 404;
                    response.Message = "User not found.";
                    return response;
                }

                await ResetPassSendOtpEmail(email, code);

                response.StatusCode = 200;
                response.Message = "Password reset OTP sent successfully.";
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

        public async Task<ServiceResponse<object>> ResetPassword(string email, string code, string newPassword)
        {
            var response = new ServiceResponse<object>();

            try
            {
                if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                {
                    response.StatusCode = 400;
                    response.Message = "Password must be at least 6 characters.";
                    return response;
                }

                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                string hash = BCrypt.Net.BCrypt.HashPassword(newPassword);

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_ResetUserPassword",
                    new
                    {
                        Email = email,
                        Code = code,
                        NewHash = hash,
                        UpdatedAt = DateTime.UtcNow
                    },
                    commandType: CommandType.StoredProcedure
                );

                if (result != null)
                {
                    response.StatusCode = (int)result.StatusCode;
                    response.Message = (string)result.Message;
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
                // 1. Validation (File Check)
                if (request.File == null || request.File.Length == 0)
                {
                    response.StatusCode = 400;
                    response.Message = "No file uploaded.";
                    return response;
                }

                // 2. Format Check
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/jpg" };
                if (!allowedTypes.Contains(request.File.ContentType))
                {
                    response.StatusCode = 400;
                    response.Message = "Only JPG and PNG allowed.";
                    return response;
                }

                // 3. File Saving Logic
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

                // 4. Dapper Database Update
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                // One-liner na lang ang pag-update sa Database
                await conn.ExecuteAsync(
                    "sp_UpdateUserProfileImage",
                    new { Id = request.UserId, Image = imageUrl },
                    commandType: CommandType.StoredProcedure
                );

                // 5. Success Response
                response.StatusCode = 200;
                response.Message = "Profile Uploaded Successfully";
                response.Data = new UploadProfileResponse
                {
                    ImageUrl = imageUrl
                };
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error: " + ex.Message;
            }
            finally
            {
                // 6. Close Connection
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }
            return response;
        }

        public async Task<ServiceResponse<object>> UpdateProfile(UpdateProfileRequest request)
        {
            var response = new ServiceResponse<object>();
            try
            {
                if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                var user = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_GetVerifyDetails",
                    new { Email = request.Email },
                    commandType: CommandType.StoredProcedure
                );

                if (user == null)
                {
                    return new ServiceResponse<object> { StatusCode = 404, Message = "User not found." };
                }

                if (user.VerificationCode != request.OtpCode)
                {
                    return new ServiceResponse<object> { StatusCode = 400, Message = "Invalid OTP." };
                }

                if (DateTime.UtcNow > (DateTime)user.VerificationExpiry)
                {
                    return new ServiceResponse<object> { StatusCode = 400, Message = "OTP has expired." };
                }

                string newHash = null;
                if (!string.IsNullOrWhiteSpace(request.NewPassword))
                {
                    if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                    {
                        return new ServiceResponse<object> { StatusCode = 400, Message = "Please enter current password." };
                    }

                    if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, (string)user.PasswordHash))
                    {
                        return new ServiceResponse<object> { StatusCode = 400, Message = "Incorrect current password." };
                    }

                    newHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                }

                await conn.ExecuteAsync("sp_UpdateUserProfileDetails", new
                {
                    UserId = request.UserId,
                    FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName,
                    LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName,
                    PasswordHash = newHash
                }, commandType: CommandType.StoredProcedure);

                response.StatusCode = 200;
                response.Message = "Profile updated successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error: " + ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open) await conn.CloseAsync();
            }
            return response;
        }

        public async Task<ServiceResponse<List<CustomerDto>>> GetAllCustomers()
        {
            var response = new ServiceResponse<List<CustomerDto>>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var customers = await conn.QueryAsync<CustomerDto>(
                    "sp_GetAllCustomers",
                    commandType: CommandType.StoredProcedure
                );

                response.Data = customers.ToList();
                response.StatusCode = 200;
                response.Message = "Customers retrieved successfully.";
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

        public async Task<ServiceResponse<string>> ToggleBlockUser(int userId, bool isBlocked)
        {
            var response = new ServiceResponse<string>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                int rowsAffected = await conn.ExecuteScalarAsync<int>(
                    "sp_ToggleBlockUser",
                    new { Id = userId, IsBlocked = isBlocked },
                    commandType: CommandType.StoredProcedure
                );

                if (rowsAffected == 0)
                {
                    response.StatusCode = 404;
                    response.Message = "User not found or cannot block an Admin.";
                    return response;
                }

                response.StatusCode = 200;
                response.Message = isBlocked ? "User blocked successfully." : "User unblocked successfully.";
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

        public async Task<ServiceResponse<List<CustomerRentalHistoryDto>>> GetCustomerRentalHistory(int userId)
        {
            var response = new ServiceResponse<List<CustomerRentalHistoryDto>>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var history = await conn.QueryAsync<CustomerRentalHistoryDto>(
                    "sp_GetCustomerRentalHistory",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure
                );

                response.Data = history.ToList();
                response.StatusCode = 200;
                response.Message = "Rental history retrieved successfully.";
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

    }
}