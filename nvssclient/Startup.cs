using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using NVSSClient.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NVSSClient
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            StaticConfig = configuration;
  //          ValidateKestrelSettings();
        }

        public IConfiguration Configuration { get; }
        public static IConfiguration StaticConfig {get; private set;}

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddMiniProfiler(options => options.RouteBasePath = "/profiler").AddEntityFramework();
            
            // Configure your db here, this example uses Postgres, MySql and SQL server examples are provided
            // *** Postgres ***
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(Configuration.GetConnectionString("ClientDatabase")));
            // *** MySql ***
            //services.AddDbContext<AppContext>(options => options.UseMySql(Configuration.GetConnectionString("ClientDatabase")));
            // *** SQL server ***
            //services.AddDbContext<AppContext>(options => options.UseSqlServer(Configuration.GetConnectionString("ClientDatabase")));
            services.AddOptions<AppConfig>().Bind(Configuration).ValidateDataAnnotations().ValidateOnStart();
 
            services.AddControllers();
        }

        // This method gets called by the runtime. We use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseMiniProfiler();
            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.Use((context, next) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                return next.Invoke();
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }


        //private void ValidateKestrelSettings()
        //{
        //    var kestrelSettings = new KestrelSettings();
        //    Configuration.GetSection("Kestrel").Bind(kestrelSettings);

        //    var validationContext = new ValidationContext(kestrelSettings, serviceProvider: null, items: null);
        //    var validationResults = new List<ValidationResult>();

        //    if (!Validator.TryValidateObject(kestrelSettings, validationContext, validationResults, validateAllProperties: true))
        //    {
        //        // Log or throw exceptions for validation failures
        //        foreach (var validationResult in validationResults)
        //        {
        //            Console.WriteLine($"Validation Error: {validationResult.ErrorMessage}");
        //            // Optionally throw an exception to stop application startup
        //            // throw new InvalidOperationException($"Configuration validation failed: {validationResult.ErrorMessage}");
        //        }
        //        throw new InvalidOperationException("Kestrel configuration is invalid. Please check appsettings.json.");
        //    }
        //    Console.WriteLine("Kestrel configuration validated successfully.");
        //}
    }
}