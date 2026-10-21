
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using METANOIA.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Infrastructure.Repositories
{
    public class FocusSessionRepository : IFocusSessionRepository
    {
        private readonly AppDbContext _context;
        public FocusSessionRepository(AppDbContext context)
        {
            _context = context;
        }
        public async Task<FocusSession?> GetRunningSessionAsync(Guid userId, DateTimeOffset now)
        {
            return await _context.FocusSessions
                .FirstOrDefaultAsync(s => s.Task.Project.UserId == userId && s.StartedAt <= now && s.EndedAt == null);
        }

        public async Task<FocusSession?> GetRunningSessionWithSlotAsync(Guid userId, DateTimeOffset now)
        {
            return await _context.FocusSessions
                .Include(s => s.ScheduleSlot)
                .FirstOrDefaultAsync(s => s.Task.Project.UserId == userId && s.StartedAt <= now && s.EndedAt == null);
        }

        public async Task<FocusSession?> GetByIdAsync(Guid sessionId)
        {
            return await _context.FocusSessions
                .Include(s => s.Task)
                    .ThenInclude(t => t.Project)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
        }

        public async Task<List<FocusSession>> GetEndedSessionsByTaskIdAsync(Guid taskId, Guid excludeSessionId)
        {
            return await _context.FocusSessions
                .Where(s => s.TaskId == taskId && s.EndedAt != null && s.Id != excludeSessionId)
                .ToListAsync();
        }

        public async System.Threading.Tasks.Task AddAsync(FocusSession session)
        {
            await _context.FocusSessions.AddAsync(session);
        }
    }
}
