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
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // configure your db here, this example uses postgres
            var connection = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";
            // Postgres
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
            // MySql
            //services.AddDbContext<AppContext>(options => options.UseMySql(connection));
            // SQL server
            //services.AddDbContext<AppContext>(options => options.UseSqlServer(connection));
            
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
        }
    }
}