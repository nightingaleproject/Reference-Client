using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NVSSClient.Models;


namespace NVSSClient
{
    public class Startup
    {
        private readonly IWebHostEnvironment env;
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            StaticConfig = configuration;
            this.env = env;
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

            if (env.IsDevelopment())
            {
                services.AddOptions<AppConfig>().Bind(Configuration).ValidateDataAnnotations().ValidateOnStart();
            }

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
    }
}