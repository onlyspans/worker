using Microsoft.EntityFrameworkCore;
using Onlyspans.Worker.Api.Data;
using Onlyspans.Worker.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

// Add database
builder.Services.AddDbContext<WorkerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

// Add migration hosted service
builder.Services.AddHostedService<MigrationHostedService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
// TODO: Add WorkerService mapping in Phase 7
// app.MapGrpcService<WorkerService>();
app.MapGet("/",
    ()
        => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();