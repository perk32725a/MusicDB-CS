using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MusicDatabase.Models;

    public class SongsContext : DbContext
    {
        public SongsContext (DbContextOptions<SongsContext> options)
            : base(options)
        {
        }

        public DbSet<MusicDatabase.Models.Songs> Songs { get; set; }
    }
