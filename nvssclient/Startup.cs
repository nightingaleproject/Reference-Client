using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using NVSSClient.Models;

namespace NVSSClient
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            StaticConfig = configuration;
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
            
            services.AddControllers();
        }

        // This method gets called by the runtime. We use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseMiniProfiler();
            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}