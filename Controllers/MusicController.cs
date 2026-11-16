using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MusicDatabase.Models;
using MusicDatabase.Services;

namespace MusicDatabase.Controllers
{
    public class MusicController : Controller
    {
        private readonly MusicDatabaseContext _context;

        // Redis cache for the album list, searches and album details:
        private readonly MusicCache _cache;

        // folder that Refresh() scans; set by MusicLibrary:Path in appsettings
        // or the MusicLibrary__Path environment variable (Kubernetes):
        private readonly string _musicPath;

        // ASP.NET's built-in logger: writes to the console (which Kubernetes
        // collects for 'kubectl logs'); levels are set under "Logging" in appsettings.
        private readonly ILogger<MusicController> _logger;

        /*----------------------------------------------------------------------
         * MusicController()
         *----------------------------------------------------------------------
         */
        public MusicController(MusicDatabaseContext context, IConfiguration config, ILogger<MusicController> logger, MusicCache cache)
        {
            _context = context;
            _musicPath = config["MusicLibrary:Path"] ?? "/music";
            _logger = logger;
            _cache = cache;
        }

        /*----------------------------------------------------------------------
         * Index()
         * GET: Music: goes to Views/Music/Index.cshtml
         * changed so we can search; results come from the cache when possible
         *----------------------------------------------------------------------
         */
        public async Task<IActionResult> Index(string searchString, string searchFor)
        {
            _logger.LogDebug("Index()");

            // display the whole thing if searchString is empty:
            if (string.IsNullOrEmpty(searchString))
            {
                return View(await AllAlbums());
            }

            // assume they want both if searchFor is empty:
            if (string.IsNullOrEmpty(searchFor))
            {
                searchFor = "Both";
            }

            // if they want both:
            if (searchFor == "Both")
            {
                List<Music> albumList = await AlbumsMatching(searchString);
                List<Music> artistList = await ArtistsMatching(searchString);
                if (albumList.Count + artistList.Count != 0)
                {
                    ViewData["SearchMsg"] = "Search Results from Both:";
                    return View(artistList.Concat(albumList));
                }

                ViewData["SearchMsg"] = "No Results for '" + searchString + "'";
                return View(await AllAlbums());
            }

            // search by artist:
            if (searchFor.Equals("Artist"))
            {
                return View(await ArtistsMatching(searchString));
            }

            // search by album
            if (searchFor.Equals("Album"))
            {
                return View(await AlbumsMatching(searchString));
            }

            return View(await AllAlbums());
        }

        // cached queries used by Index(); search keys are lower-case because
        // MySQL's comparisons ignore case, so "Zappa" and "zappa" share an entry
        private Task<List<Music>> AllAlbums() =>
            _cache.GetOrCreateAsync("albums",
                () => _context.Music.AsNoTracking().ToListAsync());

        private Task<List<Music>> ArtistsMatching(string text) =>
            _cache.GetOrCreateAsync($"search:artist:{text.ToLowerInvariant()}",
                () => _context.Music.AsNoTracking().Where(m => m.Artist.Contains(text)).ToListAsync());

        private Task<List<Music>> AlbumsMatching(string text) =>
            _cache.GetOrCreateAsync($"search:album:{text.ToLowerInvariant()}",
                () => _context.Music.AsNoTracking().Where(m => m.Album.Contains(text)).ToListAsync());

        /*----------------------------------------------------------------------
         * Details()
         * GET: Music/Details/5: goes to Views/Music/Details.cshtml
         *----------------------------------------------------------------------
         */
        public async Task<IActionResult> Details(int? id)
        {
            _logger.LogDebug("Details({Id})", id);
            if (id == null)
            {
                return NotFound();
            }

            // the album and its songs, cached together:
            AlbumDetails details = await _cache.GetOrCreateAsync($"details:{id}", async () =>
            {
                Music album = await _context.Music.AsNoTracking().FirstOrDefaultAsync(m => m.ID == id);
                if (album == null)
                {
                    return null;   // not found: nothing to cache
                }
                List<Models.Songs> songs = await _context.Songs.AsNoTracking()
                    .Where(s => s.AlbumArtistID == id).ToListAsync();
                return new AlbumDetails { Album = album, Songs = songs };
            });

            if (details == null)
            {
                return NotFound();
            }

            // set these for the details page:
            ViewData["Artist"] = details.Album.Artist;
            ViewData["Album"] = details.Album.Album;
            return View(details.Songs);
        }

        /*----------------------------------------------------------------------
         * Refresh()
         * GET: Music/Refresh: redirects to Views/Music/Index.cshtml
         * Scans only the configured music folder; the folder can't be chosen
         * from the web, so a visitor can't make the server scan other folders.
         *----------------------------------------------------------------------
         */
        public async Task<IActionResult> Refresh()
        {
            _logger.LogInformation("Refresh: scanning {MusicPath}", _musicPath);
            if (!Directory.Exists(_musicPath))
            {
                _logger.LogWarning("Refresh: music folder {MusicPath} not found; nothing scanned", _musicPath);
                return RedirectToAction("Index");
            }

            DirScan ds = new DirScan(_musicPath, true); // get directories & songs
            _logger.LogInformation("Refresh: found {Count} song entries", ds.Output.Count);

            // no 'using (_context)': the context belongs to ASP.NET's
            // dependency injection, which disposes it after the request.
            if ((_context != null) && ModelState.IsValid)
            {
                // Load what's already in the database ONCE (2 queries), then
                // compare in memory. The old code ran 2 queries per file
                // (~60,000 for a full library). Matching is exact but ignores
                // case, like MySQL's default collation.
                var comparer = StringComparer.OrdinalIgnoreCase;

                // "artist/album" -> album ID; lowest ID wins if there are duplicates
                var albumIds = new Dictionary<string, int>(comparer);
                foreach (var m in _context.Music.AsNoTracking().OrderBy(m => m.ID))
                {
                    albumIds.TryAdd(AlbumKey(m.Artist, m.Album), m.ID);
                }

                // "albumID/song title" for every song already stored
                var songKeys = new HashSet<string>(comparer);
                foreach (var s in _context.Songs.AsNoTracking())
                {
                    songKeys.Add(SongKey(s.AlbumArtistID, s.SongTitle));
                }

                // split each "artist/album/song..." entry once:
                var entries = ds.Output.Select(line =>
                {
                    string[] parts = line.Split("/");
                    string artist = parts[0].Trim();
                    string album  = parts.Length > 1 ? parts[1].Trim() : "BLANK album";
                    string song   = parts.Length > 2 ? parts[2].Trim() : "BLANK lclSong";
                    return (artist, album, song);
                }).ToList();

                // 1. albums: add each artist/album not already stored, in one batch
                _logger.LogDebug("Refresh: adding new artists/albums");
                var newAlbums = new Dictionary<string, Music>(comparer);
                foreach (var e in entries)
                {
                    string key = AlbumKey(e.artist, e.album);
                    if (!albumIds.ContainsKey(key) && !newAlbums.ContainsKey(key))
                    {
                        newAlbums[key] = new Music(e.album, e.artist);
                    }
                }
                _context.Music.AddRange(newAlbums.Values);
                _context.SaveChanges();   // fills in the new albums' IDs
                foreach (var pair in newAlbums)
                {
                    albumIds[pair.Key] = pair.Value.ID;
                }
                _logger.LogInformation("Refresh: added {Count} albums", newAlbums.Count);

                // 2. songs: add each song not already stored for its album, in one batch
                _logger.LogDebug("Refresh: adding new songs");
                var newSongs = new List<Models.Songs>();
                foreach (var e in entries)
                {
                    int albumId = albumIds[AlbumKey(e.artist, e.album)];
                    if (songKeys.Add(SongKey(albumId, e.song)))   // Add() is false if already there
                    {
                        newSongs.Add(new Models.Songs(albumId, e.song, 100));
                    }
                }
                _context.Songs.AddRange(newSongs);
                _context.SaveChanges();
                _logger.LogInformation("Refresh: added {Count} songs", newSongs.Count);

                if (newAlbums.Count + newSongs.Count > 0)
                {
                    await _cache.InvalidateAsync();
                }
            }

            _logger.LogDebug("Refresh: back to Index()");
            return RedirectToAction("Index");
        }

        // lookup keys for Refresh(); '\u001f' (unit separator) can't appear in a file name
        private static string AlbumKey(string artist, string album) => $"{artist}\u001f{album}";
        private static string SongKey(int albumId, string song) => $"{albumId}\u001f{song}";

        /*----------------------------------------------------------------------
         * Create()
         * GET: Music/Create: goes to Views/Music/Create.cshtml
         *----------------------------------------------------------------------
         */
        public IActionResult Create()
        {
            return View();
        }

        /*----------------------------------------------------------------------
         * Create()
         * POST: Music/Create: goes to Views/Music/Create.cshtml
         * To protect from overposting attacks, please enable the specific properties you want to bind to, for
         * more details see http://go.microsoft.com/fwlink/?LinkId=317598.
         *----------------------------------------------------------------------
         */
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ID,Artist,Album,ReleaseDate")] Music music)
        {
            if (ModelState.IsValid)
            {
                _context.Add(music);
                await _context.SaveChangesAsync();
                await _cache.InvalidateAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(music);
        }

        /*----------------------------------------------------------------------
         * Edit()
         * GET: Music/Edit/5: goes to Views/Music/Edit.cshtml
         *----------------------------------------------------------------------
         */
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            Music music = await _context.Music.FindAsync(id);
            return music == null ? NotFound() : (IActionResult)View(music);
        }

        /*----------------------------------------------------------------------
         * Edit()
         * POST: Music/Edit/5: goes to Views/Music/Edit.cshtml
         * To protect from overposting attacks, please enable the specific properties you want to bind to, for
         * more details see http://go.microsoft.com/fwlink/?LinkId=317598.
         *----------------------------------------------------------------------
         */
        [HttpPost]
        [ValidateAntiForgeryToken]
        // [FromRoute]: take id from the URL only. Without it, ASP.NET fills id
        // from the form's "ID" field first, so the check below could never fail.
        public async Task<IActionResult> Edit([FromRoute] int id, [Bind("ID,Artist,Album,ReleaseDate")] Music music)
        {
            if (id != music.ID)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(music);
                    await _context.SaveChangesAsync();
                    await _cache.InvalidateAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MusicExists(music.ID))
                    {
                        return NotFound();
                    }
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(music);
        }

        /*----------------------------------------------------------------------
         * Delete()
         * GET: Music/Delete/5: goes to Views/Music/Delete.cshtml
         *----------------------------------------------------------------------
         */
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            Music music = await _context.Music.FirstOrDefaultAsync(m => m.ID == id);
            return music == null ? NotFound() : (IActionResult)View(music);
        }

        /*----------------------------------------------------------------------
         * DeleteConfirmed()
         * POST: Music/Delete/5: goes to Views/Music/Delete.cshtml
         *----------------------------------------------------------------------
         */
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            Music music = await _context.Music.FindAsync(id);
            _context.Music.Remove(music);
            await _context.SaveChangesAsync();
            await _cache.InvalidateAsync();
            return RedirectToAction(nameof(Index));
        }

        /*----------------------------------------------------------------------
         * MusicExists()
         * used by Edit() (found here)
         *----------------------------------------------------------------------
         */
        private bool MusicExists(int id)
        {
            return _context.Music.Any(e => e.ID == id);
        }
    }
}
