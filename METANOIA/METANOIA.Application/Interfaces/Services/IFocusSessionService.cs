using METANOIA.Application.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IFocusSessionService
    {
        Task<FocusStartResponse> StartSessionAsync(Guid userId, FocusStartRequest request);
        Task<FocusStopResponse> StopSessionAsync(Guid userId, Guid sessionId, FocusStopRequest request);
        Task<FocusCurrentResponse?> GetCurrentSessionAsync(Guid userId);
        Task ConfirmSessionAsync(Guid userId, Guid sessionId, FocusConfirmRequest request);
        Task<Guid> SelfReportSessionAsync(Guid userId, FocusSelfReportRequest request);
        Task<FocusTimeSummaryResponse> GetTimeSummaryAsync(Guid userId, Guid slotId);
        Task<IReadOnlyList<FocusPendingConfirmationItem>> GetPendingConfirmationAsync(Guid userId);
    }
}
