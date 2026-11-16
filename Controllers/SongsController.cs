using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicDatabase.Models;
using MusicDatabase.Services;

namespace MusicDatabase.Controllers
{
    // Named SongsController (URLs stay /Songs/...). It used to be named "Songs",
    // which made the 'Songs songs' parameters below mean this controller instead
    // of the Models.Songs entity, so Create and Edit failed on every POST.
    public class SongsController : Controller
    {
        private readonly SongsContext _context;

        // song changes show on the cached Music/Details pages, so writes invalidate it:
        private readonly MusicCache _cache;

        public SongsController(SongsContext context, MusicCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // GET: Songs
        public async Task<IActionResult> Index()
        {
            return View(await _context.Songs.ToListAsync());
        }

        // GET: Songs/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var songs = await _context.Songs
                .FirstOrDefaultAsync(m => m.ID == id);
            if (songs == null)
            {
                return NotFound();
            }

            return View(songs);
        }

        // GET: Songs/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Songs/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ID,AlbumArtistID,SongTitle,SongLength")] Songs songs)
        {
            if (ModelState.IsValid)
            {
                _context.Add(songs);
                await _context.SaveChangesAsync();
                await _cache.InvalidateAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(songs);
        }

        // GET: Songs/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var songs = await _context.Songs.FindAsync(id);
            if (songs == null)
            {
                return NotFound();
            }
            return View(songs);
        }

        // POST: Songs/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        // [FromRoute]: take id from the URL only. Without it, ASP.NET fills id
        // from the form's "ID" field first, so the check below could never fail.
        public async Task<IActionResult> Edit([FromRoute] int id, [Bind("ID,AlbumArtistID,SongTitle,SongLength")] Songs songs)
        {
            if (id != songs.ID)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(songs);
                    await _context.SaveChangesAsync();
                    await _cache.InvalidateAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SongsExists(id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(songs);
        }

        // GET: Songs/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var songs = await _context.Songs
                .FirstOrDefaultAsync(m => m.ID == id);
            if (songs == null)
            {
                return NotFound();
            }

            return View(songs);
        }

        // POST: Songs/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var songs = await _context.Songs.FindAsync(id);
            _context.Songs.Remove(songs);
            await _context.SaveChangesAsync();
            await _cache.InvalidateAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool SongsExists(int id)
        {
            return _context.Songs.Any(e => e.ID == id);
        }
    }
}
