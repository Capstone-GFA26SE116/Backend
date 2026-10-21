using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Infrastructure.Services
{
    public class FocusSessionService : IFocusSessionService
    {
        private readonly IFocusSessionRepository _sessionRepository;
        private readonly IScheduleSlotRepository _slotRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly TimeProvider _timeProvider;
        private readonly IScheduleJobService _jobService;

        public FocusSessionService(
            IFocusSessionRepository sessionRepository,
            IScheduleSlotRepository slotRepository,
            IUnitOfWork unitOfWork,
            TimeProvider timeProvider,
            IScheduleJobService jobService)
        {
            _sessionRepository = sessionRepository;
            _slotRepository = slotRepository;
            _unitOfWork = unitOfWork;
            _timeProvider = timeProvider;
            _jobService = jobService;
        }


        public async Task<Guid> SelfReportSessionAsync(Guid userId, FocusSelfReportRequest request)
        {
            var now = _timeProvider.GetUtcNow();

            if (request.StartedAt >= request.EndedAt)
                throw new ValidationException("EndedAt must be greater than StartedAt.");

            if (request.StartedAt > now)
                throw new ValidationException("StartedAt cannot be in the future.");

            if ((request.EndedAt - request.StartedAt).TotalHours > 24)
                throw new ValidationException("Session duration cannot exceed 24 hours.");

            var session = new FocusSession
            {
                Id = Guid.NewGuid(),
                TaskId = request.TaskId,
                ScheduleSlotId = request.ScheduleSlotId,
                StartedAt = request.StartedAt.UtcDateTime,
                EndedAt = request.EndedAt.UtcDateTime,
                StopReason = request.StopReason.ToString(),
                Note = request.Note,
                CaptureMethod = CaptureMethod.SelfReported.ToString(),
                IsConfirmed = true
            };

            await _sessionRepository.AddAsync(session);
            await _unitOfWork.SaveChangesAsync();

            return session.Id;
        }

        public async Task<FocusCurrentResponse?> GetCurrentSessionAsync(Guid userId)
        {
            var now = _timeProvider.GetUtcNow();

            var runningSession = await _sessionRepository.GetRunningSessionWithSlotAsync(userId, now);
            if (runningSession == null) return null;

            int elapsedSeconds = (int)(now - runningSession.StartedAt).TotalSeconds;
            bool isOverrun = false;

            if (runningSession.ScheduleSlot != null)
            {
                isOverrun = elapsedSeconds > (runningSession.ScheduleSlot.ProposedMinutes * 60);
            }

            return new FocusCurrentResponse(
                SessionId: runningSession.Id,
                TaskId: runningSession.TaskId,
                TaskTitle: runningSession.Task?.Title ?? string.Empty,
                ScheduleSlotId: runningSession.ScheduleSlotId,
                StartedAt: runningSession.StartedAt,
                ElapsedSeconds: elapsedSeconds,
                ServerTime: now,
                IsOverrun: isOverrun
            );
        }

        public async Task<FocusTimeSummaryResponse> GetTimeSummaryAsync(Guid userId, Guid slotId)
        {
            var slot = await _slotRepository.GetSlotWithSessionsAsync(slotId)
                ?? throw new NotFoundException("Slot not found");

            var sessions = slot.FocusSessions
                .Where(s => s.EndedAt != null && (s.IsConfirmed || s.CaptureMethod != CaptureMethod.AutoTimeout.ToString()))
                .ToList();

            int actualMinutes = (int)sessions.Sum(s => (s.EndedAt!.Value - s.StartedAt).TotalMinutes);

            var sessionDtos = sessions.Select(s => new FocusSessionSummaryItem(
                StartedAt: new DateTimeOffset(s.StartedAt, TimeSpan.Zero),
                EndedAt: s.EndedAt.HasValue ? new DateTimeOffset(s.EndedAt.Value, TimeSpan.Zero) : null,
                Minutes: (s.EndedAt!.Value - s.StartedAt).TotalMinutes,
                CaptureMethod: ParseEnum<CaptureMethod>(s.CaptureMethod),
                StopReason: ParseNullableEnum<FocusStopReason>(s.StopReason)
            )).ToList();

            return new FocusTimeSummaryResponse(
                PlannedMinutes: slot.ProposedMinutes,
                ActualMinutes: actualMinutes,
                SessionCount: sessions.Count,
                Sessions: sessionDtos
            );
        }

        

        public async Task<FocusStartResponse> StartSessionAsync(Guid userId, FocusStartRequest request)
        {
            var now = _timeProvider.GetUtcNow();

            // 1. Kiểm tra 409: Có phiên nào đang chạy không?
            var runningSession = await _sessionRepository.GetRunningSessionAsync(userId, now);
            if (runningSession != null)
            {
                throw new FocusConflictException("A session is already running.", runningSession);
            }

            int plannedMinutes = 0;
            int startedEarlyMinutes = 0;
            string? warning = null;

            // 2. Tạo mới FocusSession
            var session = new FocusSession
            {
                Id = Guid.NewGuid(),
                TaskId = request.TaskId,
                SubTaskId = request.SubTaskId,
                ScheduleSlotId = request.ScheduleSlotId,
                FocusTrackId = request.FocusTrackId,
                StartedAt = now.UtcDateTime,
                CaptureMethod = CaptureMethod.Timer.ToString(),
                IsConfirmed = true
            };

            // 3. Cập nhật Slot nếu có
            if (request.ScheduleSlotId.HasValue)
            {
                var slot = await _slotRepository.GetByIdAsync(request.ScheduleSlotId.Value)
                    ?? throw new NotFoundException("Schedule slot not found.");

                slot.Status = ScheduleSlotStatus.InProgress.ToString();
                plannedMinutes = slot.ProposedMinutes;

                if (now < slot.StartTime)
                {
                    startedEarlyMinutes = (int)(slot.StartTime - now).TotalMinutes;
                    if (startedEarlyMinutes > 120)
                    {
                        warning = "STARTED_VERY_EARLY";
                    }
                }

                _jobService.RescheduleSlotJobs(slot.Id, actualStartTime: now);
            }

            await _sessionRepository.AddAsync(session);
            await _unitOfWork.SaveChangesAsync();

            return new FocusStartResponse(
                SessionId: session.Id,
                StartedAt: new DateTimeOffset(session.StartedAt, TimeSpan.Zero),
                CaptureMethod: ParseEnum<CaptureMethod>(session.CaptureMethod),
                PlannedMinutes: plannedMinutes,
                ExpectedEndAt: session.StartedAt.AddMinutes(plannedMinutes),
                StartedEarlyMinutes: startedEarlyMinutes,
                Warning: warning
            );
        }

        public async Task<FocusStopResponse> StopSessionAsync(Guid userId, Guid sessionId, FocusStopRequest request)
        {
            var now = _timeProvider.GetUtcNow();

            var session = await _sessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException("Session not found.");

            if (session.Task is null || session.Task.Project is null)
            {
                throw new NotFoundException("The task associated with this session was not found.");
            }

            if (session.Task.Project.UserId != userId)
            {
                throw new FocusForbiddenException("You do not have access to this session.");
            }

            if (session.EndedAt.HasValue)
            {
                throw new FocusConflictException("Session is already stopped.", session.EndedAt);
            }

            // 1. Ghi nhận thời gian dừng
            session.EndedAt = now.UtcDateTime;
            session.StopReason = request.StopReason.ToString();
            session.Note = request.Note;

            // 2. Tính toán thời gian
            int sessionMinutes = (int)(session.EndedAt.Value - session.StartedAt).TotalMinutes;

            var pastSessions = await _sessionRepository.GetEndedSessionsByTaskIdAsync(session.TaskId, session.Id);
            int taskTotalMinutes = sessionMinutes + (int)pastSessions.Sum(s => (s.EndedAt!.Value - s.StartedAt).TotalMinutes);

            // 3. Logic Check-in
            bool checkInRequired = !(request.StopReason == FocusStopReason.Interrupted || request.StopReason == FocusStopReason.Blocked);

            // 4. Hangfire Job Cleanup
            if (session.ScheduleSlotId.HasValue)
            {
                _jobService.CancelOverrunJobs(session.ScheduleSlotId.Value);

                if (request.StopReason == FocusStopReason.Completed)
                {
                    var slot = await _slotRepository.GetByIdAsync(session.ScheduleSlotId.Value);
                    if (slot != null) slot.Status = ScheduleSlotStatus.Done.ToString();
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return new FocusStopResponse(
                SessionId: session.Id,
                EndedAt: session.EndedAt.Value,
                SessionMinutes: sessionMinutes,
                TaskTotalMinutes: taskTotalMinutes,
                CheckInRequired: checkInRequired
            );
        }

        async System.Threading.Tasks.Task IFocusSessionService.ConfirmSessionAsync(Guid userId, Guid sessionId, FocusConfirmRequest request)
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException("Session not found");

            if (session.Task.Project.UserId != userId)
            {
                throw new FocusForbiddenException("You do not have access to this session.");
            }

            if (!string.Equals(session.CaptureMethod, CaptureMethod.AutoTimeout.ToString(), StringComparison.Ordinal) || session.IsConfirmed)
            {
                throw new ValidationException("Only unconfirmed AutoTimeout sessions can be confirmed.");
            }

            if (request.Confirmed)
            {
                if (request.CorrectedMinutes.HasValue)
                {
                    session.EndedAt = session.StartedAt.AddMinutes(request.CorrectedMinutes.Value);
                    session.CaptureMethod = CaptureMethod.SelfReported.ToString();
                }
                session.IsConfirmed = true;
            }
            else
            {
                session.IsConfirmed = false;
            }

            await _unitOfWork.SaveChangesAsync();
        }

        private static TEnum ParseEnum<TEnum>(string value)
            where TEnum : struct, Enum
        {
            return Enum.TryParse<TEnum>(value, ignoreCase: true, out var result)
                ? result
                : default;
        }

        private static TEnum? ParseNullableEnum<TEnum>(string? value)
            where TEnum : struct, Enum
        {
            return value != null && Enum.TryParse<TEnum>(value, ignoreCase: true, out var result)
                ? result
                : null;
        }
    }
}
