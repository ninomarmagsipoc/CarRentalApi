
using CarRental.IRepository;
using CarRental.Server;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("MyFrontendPolicy",
        policy =>
        {
            policy.WithOrigins("http://localhost:5174", "http://127.0.0.1:5174") // Your frontend URL
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHttpClient();

builder.Services.AddScoped<IPaymentRepository, PaymentService>();
builder.Services.AddScoped<IAuthRepository, AuthService>();
builder.Services.AddScoped<ICarRepository, CarService>();
builder.Services.AddScoped<ILoginRepository, LoginClass>();
builder.Services.AddScoped<IRentalRepository, RentalService>();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();  

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("MyFrontendPolicy");

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();
