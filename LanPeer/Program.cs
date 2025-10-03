using LanPeer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddHostedService<DiscoveryWorker>();
builder.Services.AddHostedService<PeerHandshake>();
builder.Services.AddHostedService<DataHandler>();
builder.Services.AddHostedService<Worker>();

builder.Services.AddControllers();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000); // <-- API will be at http://127.0.0.1:5000
});


var app = builder.Build();

// Map controllers
app.MapControllers();

app.Run();
