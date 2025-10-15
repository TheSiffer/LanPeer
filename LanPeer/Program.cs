using LanPeer;
using LanPeer.Interfaces;
using LanPeer.Workers;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;


var builder = WebApplication.CreateBuilder(args);

// Singleton declarations
builder.Services.AddSingleton<IDiscoveryWorker, DiscoveryWorker>();
builder.Services.AddSingleton<IDataHandler, DataHandler>();
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
builder.Services.AddSingleton<IQueueManager, QueueManager>();
builder.Services.AddSingleton<ICodeManager, CodeManager>();

// Add services (dont create new instance for bg service for some, use their instance created for DI)
builder.Services.AddHostedService(provider => (DiscoveryWorker)provider.GetRequiredService<IDiscoveryWorker>());
//builder.Services.AddHostedService<PeerHandshake>();
builder.Services.AddHostedService(provider => (DataHandler)provider.GetRequiredService<IDataHandler>());
builder.Services.AddHostedService(provider => (ConnectionManager)provider.GetRequiredService<IConnectionManager>());
builder.Services.AddHostedService<Worker>();

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000); // <-- API will be at http://127.0.0.1:5000
    options.ListenAnyIP(5001, o => o.UseHttps());
});


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// Map controllers
app.MapControllers();

app.Run();
