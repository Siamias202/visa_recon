using VISA_RECON.API.Application.Interfaces;
using VISA_RECON.API.Application.Interfaces.Repositories;
using VISA_RECON.API.Application.Interfaces.Services;
using VISA_RECON.API.Database;
using VISA_RECON.API.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<IGLTransactionService, GLTransactionService>();
builder.Services.AddScoped<IGLTransactionRepository, GLTransactionRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
