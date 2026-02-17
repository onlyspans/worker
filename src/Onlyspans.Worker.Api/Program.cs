using Onlyspans.Worker.Api;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog logging
builder.Host.AddSerilog();

// Add services to the container
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddGrpcServices(builder.Environment);
builder.Services.AddHealthz(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseGrpcServices();
app.UseHealthz();

app.MapGet("/",
    ()
        => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
