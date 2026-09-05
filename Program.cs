using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repositories;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using VISA_RECON.API.Application.Services;
using VISA_RECON.API.Database;
using VISA_RECON.API.Infrastructure.Persistence;
using VISA_RECON.API.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Avoid the Windows Event Log provider, which requires elevated OS access and
// can turn an otherwise harmless log message into a failed HTTP request.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<IGLTransactionService, GLTransactionService>();
builder.Services.AddScoped<IGLTransactionRepository, GLTransactionRepository>();
builder.Services.AddScoped<IBOTransactionService, BOTransactionService>();
builder.Services.AddScoped<IBOTransactionRepository, BOTransactionRepository>();
builder.Services.AddScoped<IMatchingService, MatchingService>();
builder.Services.AddScoped<IMatchingRepository, MatchingRepository>();
builder.Services.AddScoped<IManualMatchingService, ManualMatchingService>();
builder.Services.AddScoped<IManualMatchingRepository, ManualMatchingRepository>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IReportingRepository, ReportingRepository>();
builder.Services.AddScoped<IAcquiringTransactionService, AcquiringTransactionService>();
builder.Services.AddScoped<IAcquiringTransactionRepository, AcquiringTransactionRepository>();
builder.Services.AddScoped<IAcquiringReconciliationService, AcquiringReconciliationService>();
builder.Services.AddScoped<IAcquiringReconciliationRepository, AcquiringReconciliationRepository>();
builder.Services.AddScoped<ITestDataResetRepository, TestDataResetRepository>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("NextJsPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("NextJsPolicy");

app.UseAuthorization();

app.MapControllers();

app.Run();
