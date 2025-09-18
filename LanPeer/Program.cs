using LanPeer;
using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            services.AddHostedService<DiscoveryWorker>();
        }).Build().Run();