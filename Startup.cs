using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MusicDatabase.Models;
using StackExchange.Redis;

namespace MusicDatabase
{
    public class Startup
    {
        /*----------------------------------------------------------------------
         * Startup():
         *----------------------------------------------------------------------
         */
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        /*----------------------------------------------------------------------
         * ConfigureServices():
         * This method gets called by the runtime.
         * Use this method to add services to the container.
         *----------------------------------------------------------------------
        */
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential
                // cookies is needed for a given request.
                options.CheckConsentNeeded = context => true; // just set it to true
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            // .NET 10: replaces AddMvc().SetCompatibilityVersion(Version_2_1)
            services.AddControllersWithViews();

            // MySQL: the connection string comes from configuration
            // (appsettings.json, or the ConnectionStrings__DefaultConnection
            // environment variable, which is how Kubernetes will pass it in).
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<MusicDatabaseContext>(options => options.UseMySQL(connectionString));
            services.AddDbContext<SongsContext>(options         => options.UseMySQL(connectionString));

            // Redis (ConnectionStrings:Redis, or ConnectionStrings__Redis in Kubernetes),
            // shared by all copies of the app, holds two things:
            //  1. the page cache (MusicCache)
            //  2. the Data Protection keys that sign anti-forgery tokens and cookies.
            //     Each copy otherwise makes its own keys, so a form loaded from one
            //     copy and posted to another fails with 400 Bad Request.
            // With no Redis configured, fall back to an in-memory cache and the
            // default (local) key storage, so the app still runs on its own.
            var redisConnection = Configuration.GetConnectionString("Redis");
            if (!string.IsNullOrEmpty(redisConnection))
            {
                // one connection, shared by the cache and Data Protection;
                // abortConnect=false in the connection string means this doesn't
                // fail if Redis is down at startup: it keeps retrying
                IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnection);

                services.AddStackExchangeRedisCache(options =>
                {
                    options.ConnectionMultiplexerFactory = () => Task.FromResult(redis);
                    options.InstanceName = "musicdb:";   // prefix for every key
                });

                services.AddDataProtection()
                    .SetApplicationName("MusicDatabase")
                    .PersistKeysToStackExchangeRedis(() => redis.GetDatabase(), "musicdb:dataprotection-keys");
            }
            else
            {
                services.AddDistributedMemoryCache();
            }
            services.AddSingleton<MusicDatabase.Services.MusicCache>();

            // Health checks for Kubernetes probes (see Configure()):
            //  /healthz/live  - the app is running (no checks)
            //  /healthz/ready - the app can reach MySQL. Redis is left out on
            //                   purpose: the site still works without it.
            services.AddHealthChecks()
                .AddDbContextCheck<MusicDatabaseContext>("mysql", tags: new[] { "ready" });

            // Behind HAProxy/an ingress: trust X-Forwarded-For/-Proto so the app
            // sees the real client IP and the original http/https scheme.
            // Only turn on (ReverseProxy:Enabled) when a proxy is in front, since
            // otherwise any client could fake these headers.
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // the proxy's address changes in Kubernetes, so trust any sender
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        /*----------------------------------------------------------------------
         * Configure():
         * This method gets called by the runtime.
         * Use this method to configure the HTTP request pipeline.
         *----------------------------------------------------------------------
         */
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // must run first, so everything after sees the client's IP and scheme
            if (Configuration.GetValue<bool>("ReverseProxy:Enabled"))
            {
                app.UseForwardedHeaders();
            }

            // Redirect http -> https only if this app is reachable over https.
            // In the lab, HAProxy serves the site and the app only speaks http,
            // so this is off by default (Https:Redirect in appsettings).
            bool httpsRedirect = Configuration.GetValue<bool>("Https:Redirect");

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                if (httpsRedirect)
                {
                    app.UseHsts();
                }
            }

            if (httpsRedirect)
            {
                app.UseHttpsRedirection();
            }
            app.UseStaticFiles();
            app.UseCookiePolicy();

            // .NET 10: endpoint routing replaces app.UseMvc(routes => ...)
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });
                endpoints.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

                endpoints.MapControllerRoute(
                    name: "default",
                    // pattern: "{controller=Home}/{action=Index}/{id?}");
                    pattern: "{controller=Music}/{action=Index}/{id?}");
            });
        }
    }
}

// EOF:
