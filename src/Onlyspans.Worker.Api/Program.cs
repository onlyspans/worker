using Onlyspans.Worker.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddSerilog();

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddGrpcServices(builder.Environment, builder.Configuration);
builder.Services.AddHealthz(builder.Configuration);

var app = builder.Build();

app.UseGrpcServices();
app.UseHealthz();

app.MapGet("/",
    ()
        => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
