using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Contracts;
using Plutus.Entities;

namespace Plutus.DBService.Extensions
{
    public static class ServiceExtensions
    {
        public static void ConfigureCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
            });
        }

        // ConfigureAuthentication
        //
        // 
        // The runtime calls this method. Use this method to configure the HTTP request pipeline.
        /*public static void ConfigureAuthentication(IApplicationBuilder app, IHostingEnvironment env)
        {
            // more code
            app.UseAuthentication();
            app.UseAuthorization();
            // more code
        }
*/
        public static void ConfigureDBContext(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration["ConnectionString"];

            services.AddDbContext<MySqlDbContext>(o => o.UseMySql(ServerVersion.AutoDetect(connectionString)));
        }

        public static void ConfigureMySqlDBContext(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration["ConnectionString"];

            services.AddDbContext<RepositoryContext>(o => o.UseMySql(connectionString, MySqlServerVersion.LatestSupportedServerVersion));

            //BussinessIdProvider bussinessIdProvider = new BussinessIdProvider("1");
            //services.AddSingleton<BussinessIdProvider>(bussinessIdProvider);

            //HttpContextAccessor 
        }

        public static void ConfigureRepositoryWrapper(this IServiceCollection services)
        {
            services.AddScoped<IRepositoryWrapper, RepositoryWrapper>();
        }

        public static void ConfigureSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen();
        }

        public static void ConfigureControllers(this IServiceCollection services)
        {
            services.AddControllers().AddNewtonsoftJson(options => options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore);
        }
    }
}
