using System;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using VRDR;
using RestSharp;
using NVSSClient.Services;

namespace NVSSClient
{
    class Program{
        public static void Main(string[] args) 
        {
            CreateHostBuilder(args).Build().Run();
        }
        // EF Core uses this method at design time to access the DbContext   
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<TimedHostedService>();
                });
        
        public IConfiguration Configuration { get; }
    }
}