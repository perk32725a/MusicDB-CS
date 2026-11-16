using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;

// This is the model for the project:
namespace MusicDatabase.Models
{
    // Music class
    //           database/table name
    /*--------------------------------------------------------------------------
     * CLASS Music
     * the Music table; referenced by Songs
     *--------------------------------------------------------------------------
     */
    public class Music
    {
        // fields in the Music table:
        public int ID { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }

        // TODO: extract release date from metadata
        [Display(Name = "Release Date")]
        [DataType(DataType.Date)]
        public DateTime ReleaseDate { get; set; }

        // Music constructor:
        public Music()
        {
            Album = new string("");
            Artist = new string("");
        }

        // Music constructor:
        public Music(string album, string artist)
        {
            Album = album;
            Artist = artist;
        }

        // Music constructor:
        public Music(string album, string artist, string released)
        {
            Album = album;
            Artist = artist;
            ReleaseDate = DateTime.Parse(released);
        }
    }

    /*--------------------------------------------------------------------------
     * CLASS Songs
     * the Songs table; references Music table:
     *--------------------------------------------------------------------------
     */
    public class Songs
    {
        public int ID { get; set; }            // key song ID
        public int AlbumArtistID { get; set; } // reference album/artist in Music
        public string SongTitle { get; set; }  // song title
        public int SongLength { get; set; }    // length of song in seconds

        /*----------------------------------------------------------------------
         * Songs(void)
         *----------------------------------------------------------------------
         */
        public Songs()
        {
            AlbumArtistID = new int();
            SongTitle = new string("");
            SongLength = new int();
        }

        /*----------------------------------------------------------------------
         * Songs(int, string, int)
         *----------------------------------------------------------------------
         */
        public Songs(int albumArtistID, string songTitle, int songLength)
        {
            AlbumArtistID = albumArtistID;
            SongTitle = songTitle;
            SongLength = songLength; // TODO: extract song lengths from metadata
        }
    }

    /*--------------------------------------------------------------------------
     * CLASS DirScan
     * This is the DirScan model
     *--------------------------------------------------------------------------
     */
    public class DirScan
    {
        // logging for the scan is done by the caller (MusicController.Refresh)

        public string Searchdir { get; set; }    // directory we're scanning
        public List<string> Output { get; set; } // output list

        /*----------------------------------------------------------------------
         * DirScan(void)
         *----------------------------------------------------------------------
         */
        public DirScan()
        {
            Searchdir = new string("");
            Output = new List<string>();
        }

        /*----------------------------------------------------------------------
         * DirScan(string, bool)
         *----------------------------------------------------------------------
         */
        public DirScan(string dirString, bool getSongList)
        {
            Searchdir = dirString;
            if (Directory.Exists(Searchdir))
            {
                int offset = dirString.Length + 1;
                IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(dirString, "*", SearchOption.AllDirectories);

                Searchdir = dirString;
                Output = new List<string>();

                foreach (string line in entries)
                {
                    string outline = line.Substring(offset);
                    string[] entryarray = outline.Split("/");
                    if (entryarray.GetLength(0) > 2)
                    {
                        Output.Add(outline);
                    }
                }

                Output.Sort();
            }
        }
    }
}

// EOF:
