using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

//        Solution.Models
namespace MusicDatabase.Models
{
    //           SolutionContext
    public class MusicDatabaseContext : DbContext
    {
        //     SolutionContext                    SolutionContext
        public MusicDatabaseContext(DbContextOptions<MusicDatabaseContext> options)
            : base(options)
        {
        }

        //          <Solution.Models.Database> Database
        public DbSet<Music> Music { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DbSet<Music> Id { get; set; }
        public DbSet<Music> Album { get; set; }
        public DbSet<Music> Artist { get; set; }
        public DbSet<Music> Released { get; set; }

        public DbSet<Songs> Songs { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual DbSet<Songs> ID { get; set; }
        public virtual DbSet<Songs> AlbumArtistID { get; set; }
        public virtual DbSet<Songs> SongTitle { get; set; }
        public virtual DbSet<Songs> SongLength { get; set; }
    }
}

// code that was auto-generated; replace
/*using System;
namespace MusicDatabase.Models
{
    public class MusicDatabaseContext
    {
        public MusicDatabaseContext()
        {
        }
    }
}
*/
