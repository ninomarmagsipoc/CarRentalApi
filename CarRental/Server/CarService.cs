using CarRental.IRepository;
using CarRental.Model;
using CarRental.Model.Response;
using Dapper;
using System.Data;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class CarService : ICarRepository
    {
        private readonly SqlConnection conn;
        private readonly IWebHostEnvironment _env;

        public CarService(IConfiguration config, IWebHostEnvironment env)
        {
            conn = new SqlConnection(config["ConnectionStrings:CarRental"]);
            _env = env;
        }

        public async Task<ServiceResponse<object>> GetCars(int? userId = null)
        {
            var response = new ServiceResponse<object>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var cars = await conn.QueryAsync<CarModel>(
                    "cars_GetCars",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure
                );

                response.StatusCode = 200;
                response.Data = cars;
                response.Message = "Cars retrieved successfully.";
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

        public async Task<ServiceResponse<string>> ToggleFavorite(int userId, int carId)
        {
            var response = new ServiceResponse<string>();
            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var actionMessage = await conn.QueryFirstOrDefaultAsync<string>(
                    "sp_ToggleFavorite",
                    new { UserId = userId, CarId = carId },
                    commandType: CommandType.StoredProcedure
                );

                response.StatusCode = 200;
                response.Data = actionMessage;
                response.Message = actionMessage;
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

        public async Task<ServiceResponse<List<CarBookingDTO>>> GetCarBookings(int carId)
        {
            var response = new ServiceResponse<List<CarBookingDTO>>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var bookings = await conn.QueryAsync<CarBookingDTO>(
                    "cars_GetCarBookings",
                    new { CarId = carId },
                    commandType: CommandType.StoredProcedure
                );

                response.Data = bookings.ToList();
                response.StatusCode = 200;
                response.Message = "Bookings retrieved successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error fetching bookings: " + ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<string>> DeleteCar(int carId)
        {
            var response = new ServiceResponse<string>();
            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_HideCar",
                    new { CarId = carId },
                    commandType: CommandType.StoredProcedure
                );

                if (result != null)
                {
                    response.StatusCode = (int)result.StatusCode;
                    response.Message = (string)result.Message;

                    if (response.StatusCode == 200)
                    {
                        response.Data = response.Message;
                    }
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

        public async Task<ServiceResponse<string>> AddCar(CarRequest request)
        {
            var response = new ServiceResponse<string>();
            try
            {
                string imagePath = null;

                if (request.ImageFile != null && request.ImageFile.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "images");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    string uniqueFileName = $"{Guid.NewGuid()}_{request.ImageFile.FileName}";
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await request.ImageFile.CopyToAsync(fileStream);
                    }

                    imagePath = "/images/" + uniqueFileName;
                }

                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var parameters = new
                {
                    request.CarName,
                    request.CarInfo,
                    request.Seats,
                    request.PricePerDay,
                    CarImage = imagePath
                };

                await conn.ExecuteAsync("cars_AddCar", parameters, commandType: CommandType.StoredProcedure);

                response.Data = "Car added successfully!";
                response.StatusCode = 200;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error adding car: " + ex.Message;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    await conn.CloseAsync();
            }

            return response;
        }

        public async Task<ServiceResponse<string>> EditCar(int carId, CarRequest request)
        {
            var response = new ServiceResponse<string>();
            try
            {
                string imagePath = null;

                if (request.ImageFile != null && request.ImageFile.Length > 0)
                {
                    string uploadFolder = Path.Combine(_env.WebRootPath, "images");
                    if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);

                    string uniqueFileName = $"{Guid.NewGuid()}_{request.ImageFile.FileName}";
                    string filePath = Path.Combine(uploadFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await request.ImageFile.CopyToAsync(fileStream);
                    }
                    imagePath = "/images/" + uniqueFileName;
                }

                if (conn.State == ConnectionState.Closed) await conn.OpenAsync();

                var parameters = new
                {
                    CarId = carId,
                    request.CarName,
                    request.CarInfo,
                    request.Seats,
                    request.PricePerDay,
                    request.MaintenanceMonth,
                    CarImage = imagePath
                };

                await conn.ExecuteAsync("cars_EditCar", parameters, commandType: CommandType.StoredProcedure);

                response.Data = "Car updated successfully!";
                response.StatusCode = 200;
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error updating car: " + ex.Message;
            }
            finally { await conn.CloseAsync(); }
            return response;
        }

        public async Task<ServiceResponse<string>> RestoreCar(int carId)
        {
            var response = new ServiceResponse<string>();
            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                int rowsAffected = await conn.ExecuteScalarAsync<int>(
                    "cars_RestoreCar",
                    new { CarId = carId },
                    commandType: CommandType.StoredProcedure
                );

                if (rowsAffected == 0)
                {
                    response.StatusCode = 404;
                    response.Message = "Car not found or already visible.";
                    return response;
                }

                response.Data = "Car restored successfully";
                response.StatusCode = 200;
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
        public async Task<ServiceResponse<object>> GetArchivedCars()
        {
            var response = new ServiceResponse<object>();

            try
            {
                if (conn.State == ConnectionState.Closed)
                    await conn.OpenAsync();

                var cars = await conn.QueryAsync<CarModel>(
                    "cars_GetArchivedCars",
                    commandType: CommandType.StoredProcedure
                );

                response.Data = cars;
                response.StatusCode = 200;
                response.Message = "Archived cars retrieved successfully.";
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Message = "Error fetching archived cars: " + ex.Message;
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