using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicDatabase.Models;

namespace MusicDatabase
{
    public class Program
    {
        // Run with this argument to apply migrations + seed data, then exit
        // (exit code 0 = success, 1 = failure) without starting the web server.
        // Used by the 'migrate' service in compose.yaml, and later by a
        // Kubernetes Job, so several copies of the app never race to migrate.
        private const string MigrateOnlyArg = "--migrate-only";

        public static int Main(string[] args)
        {
            bool migrateOnly = args.Contains(MigrateOnlyArg);
            // keep the flag away from the host's command-line configuration
            var host = CreateHostBuilder(args.Where(a => a != MigrateOnlyArg).ToArray()).Build();

            var config = host.Services.GetRequiredService<IConfiguration>();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            if (migrateOnly)
            {
                return MigrateAndSeed(host, logger) ? 0 : 1;
            }

            // No Redis configured: Startup fell back to an in-memory cache and
            // keys kept inside this copy of the app. Fine for one copy; say so,
            // because with several copies forms posted to another copy fail.
            if (string.IsNullOrEmpty(config.GetConnectionString("Redis")))
            {
                logger.LogWarning(
                    "ConnectionStrings:Redis is not set: using an in-memory cache and local " +
                    "Data Protection keys. Fine for a single copy of the app; set it when " +
                    "running more than one copy.");
            }

            // Apply any pending migrations and seed the database on startup,
            // unless Database:MigrateOnStartup is false. With several copies of
            // the app, turn it off for them and run '--migrate-only' once instead.
            // A failure here is logged but doesn't stop the site from starting
            // (/healthz/ready reports whether MySQL is usable).
            if (config.GetValue("Database:MigrateOnStartup", true))
            {
                MigrateAndSeed(host, logger);
            }

            host.Run();
            return 0;
        }

        /*----------------------------------------------------------------------
         * MigrateAndSeed()
         * applies pending EF Core migrations, then adds the seed albums if the
         * database is empty; returns false (and logs why) if anything fails
         *----------------------------------------------------------------------
         */
        private static bool MigrateAndSeed(IHost host, ILogger logger)
        {
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                try
                {
                    var context = services.GetRequiredService<MusicDatabaseContext>();
                    context.Database.Migrate();
                    SeedData.Initialize(services);
                    logger.LogInformation("Database migrated and seeded");
                    return true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred migrating or seeding the DB.");
                    return false;
                }
            }
        }

        // .NET 10: the generic host (Host) replaces the 2.1 WebHost;
        // Startup.cs still does the service and pipeline setup.
        // ('dotnet ef' also calls this method to find the DbContext.)
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
