using CarRental.IRepository;
using CarRental.Model.Response;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class AuthService : IAuthRepository
    {
        private readonly SqlConnection conn;

        public AuthService(IConfiguration config)
        {
            conn = new SqlConnection(config["ConnectionString:CarRental"]);
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

                string query = @"SELECT Id, FirstName, LastName, Email, PasswordHash
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

                        response.StatusCode = 200;
                        response.Message = "Login successful.";
                        response.Data = new
                        {
                            Id = reader["Id"],
                            FirstName = reader["FirstName"],
                            LastName = reader["LastName"],
                            Email = reader["Email"]
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
    }
}