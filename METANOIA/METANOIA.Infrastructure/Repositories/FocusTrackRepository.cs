using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Repositories
{
    public class FocusTrackRepository : IFocusTrackRepository
    {
        private readonly AppDbContext _context;

        public FocusTrackRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<FocusTrack>> GetAsync(bool? isActive, CancellationToken cancellationToken = default)
        {
            var query = _context.FocusTracks.AsQueryable();
            if (isActive is { } active)
            {
                query = query.Where(t => t.IsActive == active);
            }

            return await query.OrderBy(t => t.Title).ThenBy(t => t.Artist).ToListAsync(cancellationToken);
        }

        public Task<FocusTrack?> GetByIdAsync(Guid trackId, CancellationToken cancellationToken = default)
        {
            return _context.FocusTracks.FirstOrDefaultAsync(t => t.Id == trackId, cancellationToken);
        }

        public async Task AddAsync(FocusTrack track, CancellationToken cancellationToken = default)
        {
            await _context.FocusTracks.AddAsync(track, cancellationToken);
        }
    }
}
