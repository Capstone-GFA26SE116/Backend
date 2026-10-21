using METANOIA.Application.Dto;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface IFocusSessionRepository
    {
        Task<FocusSession?> GetRunningSessionAsync(Guid userId, DateTimeOffset now);
        Task<FocusSession?> GetRunningSessionWithSlotAsync(Guid userId, DateTimeOffset now);
        Task<FocusSession?> GetByIdAsync(Guid sessionId);
        Task<List<FocusSession>> GetEndedSessionsByTaskIdAsync(Guid taskId, Guid excludeSessionId);
        System.Threading.Tasks.Task AddAsync(FocusSession session);
    }
}
