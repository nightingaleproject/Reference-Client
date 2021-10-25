using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NVSSClient.Models;
using Microsoft.Extensions.Configuration;

namespace NVSSClient.tests
{
    public class CustomWebApplicationFactory<TStartup> : WebApplicationFactory<TStartup> where TStartup: class
    {
           public IConfiguration Configuration { get; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Database setup is handled by overriding which Database configuration is used
            builder.ConfigureAppConfiguration((context, builder) => {
                builder.AddJsonFile("appsettings.Test.json");
            });
            builder.ConfigureServices(services =>
            {
                var sp = services.BuildServiceProvider();

                using (var scope = sp.CreateScope())
                {
                    var scopedServices = scope.ServiceProvider;
                    var db = scopedServices.GetRequiredService<AppDbContext>();
                    var logger = scopedServices
                        .GetRequiredService<ILogger<CustomWebApplicationFactory<TStartup>>>();

                    db.Database.EnsureCreated();
                }
            });
        }
    }
}