using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace MusicDatabase.Models
{
    /* this class defines some seed data for the database
     */
    public static class SeedData
    {
        public static void Initialize(IServiceProvider serviceProvider)
        {
            using (var context = new MusicDatabaseContext(
                serviceProvider.GetRequiredService<DbContextOptions<MusicDatabaseContext>>()))
            {
                // Look for any MusicDB entries:
                if (context.Album.Count() > 0)
                {
                    return;   // DB has been seeded
                }

                context.AddRange(
                    new Music
                    {
                        Album = "Emerson, Lake & Palmer",
                        Artist = "Emerson, Lake & Palmer",
                        ReleaseDate = DateTime.Parse("1970-11-20")
                    },
                    new Songs
                    {
                        AlbumArtistID = 1,
                        SongTitle = "The Barbarian",
                        SongLength = 273

                    },
                    new Songs
                    {
                        AlbumArtistID = 1,
                        SongTitle = "Take A Pebble",
                        SongLength = 222
                    },
                    new Music
                    {
                        Album    = "Tarkus",
                        Artist   = "Emerson, Lake & Palmer",
                        ReleaseDate = DateTime.Parse("1971-6-14")
                    },
                    new Songs
                    {
                        AlbumArtistID = 2,
                        SongTitle = "Tarkus",
                        SongLength = 444
                    },
                    new Music
                    {
                        Album    = "Pictures At An Exhibition",
                        Artist   = "Emerson, Lake & Palmer",
                        ReleaseDate = DateTime.Parse("1971-11-1")
                    },
                    new Music
                    {
                        Album    = "Trilogy",
                        Artist   = "Emerson, Lake & Palmer",
                        ReleaseDate = DateTime.Parse("1972-7-6")
                    },
                    new Music
                    {
                        Album    = "Brain Salad Surgery",
                        Artist   = "Emerson, Lake & Palmer",
                        ReleaseDate = DateTime.Parse("1973-11-19")
                    },
                    new Music
                    {
                        Album = "Live",
                        Artist = "801",
                        ReleaseDate = DateTime.Parse("1985-12-12")
                    }

                );

                context.SaveChanges();
            }
        }
    }
}
