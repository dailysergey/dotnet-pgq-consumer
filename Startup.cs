using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetPGMQ
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<PostgresOptions>(Configuration.GetSection("Postgres"));
            services.AddOptions<PostgresOptions>().Bind(Configuration.GetSection("Postgres")).Validate(
                (o) =>
                {
                    return !string.IsNullOrWhiteSpace(o.ConnectionString);
                }, "Connection String is Empty!");
            services.AddHostedService<PgMQProvider>();
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            
        }
    }
}