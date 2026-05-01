
using CarRental.Hub;
using CarRental.IRepository;
using CarRental.Model;
using CarRental.Server;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("MyFrontendPolicy",
        policy =>
        {
            policy.WithOrigins("http://localhost:55479", "http://127.0.0.1:5173", "http://10.0.2.2:5554") // Your frontend URL
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });//
});

builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHttpClient();

builder.Services.AddSignalR();

builder.Services.AddScoped<IPaymentRepository, PaymentService>();
builder.Services.AddScoped<IAuthRepository, AuthService>();
builder.Services.AddScoped<ICarRepository, CarService>();
builder.Services.AddScoped<ILoginRepository, LoginClass>();
builder.Services.AddScoped<IRentalRepository, RentalService>();
builder.Services.AddScoped<INotificationRepository, NotificationService>();
builder.Services.AddHostedService<RentalExpiryService>();
builder.Services.AddScoped<IEmailService, EmailService>();
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

//app.UseHttpsRedirection();

app.UseCors("MyFrontendPolicy");

app.MapHub<NotificationHub>("/notificationHub");

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();
