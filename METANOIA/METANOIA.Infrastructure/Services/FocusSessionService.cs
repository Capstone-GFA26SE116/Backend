using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Ghi nhận thời gian làm việc THẬT (T_act). Các luật chính:
    //  - Chỉ người dùng bấm Start/Stop mới tạo dữ liệu độ tin cậy cao; hệ thống không tự bấm giờ.
    //  - Không có tạm dừng: tạm dừng = Stop (Interrupted) rồi Start phiên mới. T_act của task = tổng các phiên.
    //  - Mọi mốc thời gian lấy từ đồng hồ server (TimeProvider), không tin giờ do client gửi (trừ self-report).
    public class FocusSessionService : IFocusSessionService
    {
        // Phiên AutoTimeout mà người dùng từ chối được đánh dấu bằng ghi chú này (bảng FocusSession chưa có cột riêng)
        public const string DiscardedMarker = "[Người dùng đã loại bỏ phiên này]";

        private const int VeryEarlyMinutes = 120;
        private const int MaxSessionHours = 24;

        // Slot ở các trạng thái này mới bắt đầu làm được. Proposed (chưa duyệt), Done, Skipped, Cancelled thì không
        private static readonly HashSet<string> StartableSlotStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(ScheduleSlotStatus.Approved),
            nameof(ScheduleSlotStatus.Due),
            nameof(ScheduleSlotStatus.InProgress),
            nameof(ScheduleSlotStatus.Overrun),
            nameof(ScheduleSlotStatus.Missed),
            nameof(ScheduleSlotStatus.Postponed)
        };

        private readonly IFocusSessionRepository _sessionRepository;
        private readonly IScheduleSlotRepository _slotRepository;
        private readonly IFocusTrackRepository _focusTrackRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly TimeProvider _timeProvider;
        private readonly IScheduleJobService _jobService;

        public FocusSessionService(
            IFocusSessionRepository sessionRepository,
            IScheduleSlotRepository slotRepository,
            IFocusTrackRepository focusTrackRepository,
            IUnitOfWork unitOfWork,
            TimeProvider timeProvider,
            IScheduleJobService jobService)
        {
            _sessionRepository = sessionRepository;
            _slotRepository = slotRepository;
            _focusTrackRepository = focusTrackRepository;
            _unitOfWork = unitOfWork;
            _timeProvider = timeProvider;
            _jobService = jobService;
        }

        // ------------------------------------------------------------------ Start

        public async Task<FocusStartResponse> StartSessionAsync(Guid userId, FocusStartRequest request)
        {
            var now = _timeProvider.GetUtcNow();

            // Một người chỉ có một phiên đang chạy. Mở hai tab thì tab sau nhận lại phiên cũ trong thân lỗi 409
            var running = await _sessionRepository.GetRunningWithDetailsAsync(userId);
            if (running is not null)
            {
                throw new FocusConflictException("Bạn đang có một phiên làm việc chưa dừng.", ToCurrentResponse(running, now));
            }

            if (!await _slotRepository.TaskOwnedByUserAsync(request.TaskId, userId))
            {
                throw new NotFoundException("Không tìm thấy task.");
            }

            if (request.SubTaskId is { } subTaskId
                && !await _slotRepository.SubTaskBelongsToTaskAsync(subTaskId, request.TaskId))
            {
                throw new UserFriendlyException("Subtask không tồn tại trong task này.");
            }

            await EnsureTrackUsableAsync(request.FocusTrackId);

            var slot = await GetStartableSlotAsync(userId, request.TaskId, request.ScheduleSlotId);

            var session = new FocusSession
            {
                Id = Guid.NewGuid(),
                TaskId = request.TaskId,
                SubTaskId = request.SubTaskId,
                ScheduleSlotId = slot?.Id,
                FocusTrackId = request.FocusTrackId,
                StartedAt = now.UtcDateTime,
                CaptureMethod = CaptureMethod.Timer.ToString(),
                IsConfirmed = true
            };

            var plannedMinutes = 0;
            var startedEarlyMinutes = 0;
            string? warning = null;

            if (slot is not null)
            {
                plannedMinutes = slot.ProposedMinutes;
                slot.Status = ScheduleSlotStatus.InProgress.ToString();

                // Bắt đầu sớm hơn lịch thì vẫn được. Sớm hơn 2 giờ thì báo để giao diện hỏi lại cho chắc
                var slotStart = UtcTime.ToApi(slot.StartTime);
                if (now < slotStart)
                {
                    startedEarlyMinutes = (int)Math.Floor((slotStart - now).TotalMinutes);
                    if (startedEarlyMinutes > VeryEarlyMinutes)
                    {
                        warning = "STARTED_VERY_EARLY";
                    }
                }
            }

            await _sessionRepository.AddAsync(session);
            await _unitOfWork.SaveChangesAsync();

            // Hẹn lại các job phía cuối theo giờ bắt đầu thực tế. Sau khi đã lưu: lỗi hẹn giờ không làm mất phiên vừa tạo
            if (slot is not null)
            {
                _jobService.RescheduleSlotJobs(slot.Id, now);
            }

            return new FocusStartResponse(
                SessionId: session.Id,
                StartedAt: now,
                CaptureMethod: CaptureMethod.Timer,
                PlannedMinutes: plannedMinutes,
                ExpectedEndAt: now.AddMinutes(plannedMinutes),
                StartedEarlyMinutes: startedEarlyMinutes,
                Warning: warning);
        }

        // ------------------------------------------------------------------ Stop

        public async Task<FocusStopResponse> StopSessionAsync(Guid userId, Guid sessionId, FocusStopRequest request)
        {
            var now = _timeProvider.GetUtcNow();

            var session = await _sessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException("Không tìm thấy phiên làm việc.");

            EnsureOwner(session, userId);

            if (session.EndedAt.HasValue)
            {
                // Bấm Stop hai lần: trả lại mốc đã dừng thay vì ghi đè
                throw new FocusConflictException("Phiên này đã được dừng trước đó.",
                    new { sessionId = session.Id, endedAt = UtcTime.ToApi(session.EndedAt.Value) });
            }

            session.EndedAt = now.UtcDateTime;
            session.StopReason = request.StopReason.ToString();
            session.Note = request.Note;

            // T_act: phiên này + mọi phiên đã dừng trước đó của cùng task (không tính phiên AutoTimeout chưa xác nhận,
            // giống time-summary). Cộng theo phút có phần lẻ rồi mới làm tròn, tránh lệch từng phiên.
            var sessionMinutes = MinutesOf(session);
            var pastSessions = await _sessionRepository.GetEndedSessionsByTaskIdAsync(session.TaskId, session.Id);
            var taskTotalMinutes = sessionMinutes + pastSessions.Where(CountsTowardsActual).Sum(MinutesOf);

            // Tạm dừng hoặc bị chặn nghĩa là sẽ quay lại làm tiếp, chưa cần hỏi "xong chưa"
            var checkInRequired = request.StopReason is not (FocusStopReason.Interrupted or FocusStopReason.Blocked);

            var completed = request.StopReason == FocusStopReason.Completed;
            if (session.ScheduleSlotId is { } slotId)
            {
                var slot = await _slotRepository.GetByIdAsync(slotId);
                if (slot is not null)
                {
                    // Xong thì slot Done. Chưa xong thì trở lại Due để người dùng bấm Start làm tiếp
                    slot.Status = (completed ? ScheduleSlotStatus.Done : ScheduleSlotStatus.Due).ToString();
                }
            }

            await _unitOfWork.SaveChangesAsync();

            if (session.ScheduleSlotId is { } jobSlotId)
            {
                if (completed)
                {
                    _jobService.CancelAllJobs(jobSlotId);
                }
                else
                {
                    // Không còn ai làm: bỏ nhắc "sắp hết giờ", "quá giờ" và tự ngắt. Job SlotEnded vẫn giữ để biết có Missed không
                    _jobService.CancelOverrunJobs(jobSlotId);
                }
            }

            return new FocusStopResponse(
                SessionId: session.Id,
                EndedAt: now,
                SessionMinutes: Round(sessionMinutes),
                TaskTotalMinutes: Round(taskTotalMinutes),
                CheckInRequired: checkInRequired);
        }

        // ------------------------------------------------------------------ Current

        public async Task<FocusCurrentResponse?> GetCurrentSessionAsync(Guid userId)
        {
            var running = await _sessionRepository.GetRunningWithDetailsAsync(userId);
            return running is null ? null : ToCurrentResponse(running, _timeProvider.GetUtcNow());
        }

        // ------------------------------------------------------------------ Confirm

        public async Task ConfirmSessionAsync(Guid userId, Guid sessionId, FocusConfirmRequest request)
        {
            var now = _timeProvider.GetUtcNow();

            var session = await _sessionRepository.GetByIdAsync(sessionId)
                ?? throw new NotFoundException("Không tìm thấy phiên làm việc.");

            EnsureOwner(session, userId);

            var isPendingAutoTimeout = session.CaptureMethod == CaptureMethod.AutoTimeout.ToString() && !session.IsConfirmed;
            if (!isPendingAutoTimeout)
            {
                throw new UserFriendlyException("Chỉ xác nhận được phiên bị hệ thống tự ngắt (AutoTimeout) và chưa xác nhận.");
            }

            if (session.Note?.Contains(DiscardedMarker, StringComparison.Ordinal) == true)
            {
                throw new UserFriendlyException("Phiên này đã bị loại bỏ trước đó.");
            }

            if (!request.Confirmed)
            {
                // Từ chối: phiên không bao giờ được dùng để học (vẫn IsConfirmed = false), đánh dấu để không hỏi lại
                session.Note = string.IsNullOrWhiteSpace(session.Note) ? DiscardedMarker : $"{session.Note} {DiscardedMarker}";
                await _unitOfWork.SaveChangesAsync();
                return;
            }

            if (request.CorrectedMinutes is { } corrected)
            {
                if (corrected <= 0 || corrected > MaxSessionHours * 60)
                {
                    throw new UserFriendlyException($"Số phút điều chỉnh phải từ 1 đến {MaxSessionHours * 60}.");
                }

                var correctedEnd = UtcTime.ToApi(session.StartedAt).AddMinutes(corrected);
                if (correctedEnd > now)
                {
                    throw new UserFriendlyException("Số phút điều chỉnh làm giờ kết thúc nằm ở tương lai.");
                }

                // Người dùng tự nói thời gian thực tế: coi như tự khai báo, độ tin cậy thấp hơn Timer
                session.EndedAt = correctedEnd.UtcDateTime;
                session.CaptureMethod = CaptureMethod.SelfReported.ToString();
            }

            session.IsConfirmed = true;
            await _unitOfWork.SaveChangesAsync();
        }

        // ------------------------------------------------------------------ Self-report

        public async Task<Guid> SelfReportSessionAsync(Guid userId, FocusSelfReportRequest request)
        {
            var now = _timeProvider.GetUtcNow();

            if (request.EndedAt <= request.StartedAt)
            {
                throw new UserFriendlyException("EndedAt phải lớn hơn StartedAt.");
            }

            if (request.StartedAt > now || request.EndedAt > now)
            {
                throw new UserFriendlyException("Thời gian khai báo không được nằm trong tương lai.");
            }

            if ((request.EndedAt - request.StartedAt).TotalHours > MaxSessionHours)
            {
                throw new UserFriendlyException($"Một phiên không được dài quá {MaxSessionHours} giờ.");
            }

            if (!await _slotRepository.TaskOwnedByUserAsync(request.TaskId, userId))
            {
                throw new NotFoundException("Không tìm thấy task.");
            }

            var slot = await GetStartableSlotAsync(userId, request.TaskId, request.ScheduleSlotId);

            var session = new FocusSession
            {
                Id = Guid.NewGuid(),
                TaskId = request.TaskId,
                ScheduleSlotId = slot?.Id,
                StartedAt = request.StartedAt.UtcDateTime,
                EndedAt = request.EndedAt.UtcDateTime,
                StopReason = request.StopReason.ToString(),
                Note = request.Note,
                CaptureMethod = CaptureMethod.SelfReported.ToString(),
                IsConfirmed = true
            };

            var completed = request.StopReason == FocusStopReason.Completed;
            if (slot is not null && completed)
            {
                slot.Status = ScheduleSlotStatus.Done.ToString();
            }

            await _sessionRepository.AddAsync(session);
            await _unitOfWork.SaveChangesAsync();

            if (slot is not null && completed)
            {
                _jobService.CancelAllJobs(slot.Id);
            }

            return session.Id;
        }

        // ------------------------------------------------------------------ Time summary

        public async Task<FocusTimeSummaryResponse> GetTimeSummaryAsync(Guid userId, Guid slotId)
        {
            var slot = await _slotRepository.GetSlotWithSessionsAsync(slotId, userId)
                ?? throw new NotFoundException("Không tìm thấy slot.");

            var sessions = slot.FocusSessions
                .Where(s => s.EndedAt is not null && CountsTowardsActual(s))
                .OrderBy(s => s.StartedAt)
                .ToList();

            var items = sessions.Select(s => new FocusSessionSummaryItem(
                StartedAt: UtcTime.ToApi(s.StartedAt),
                EndedAt: UtcTime.ToApi(s.EndedAt),
                Minutes: Round(MinutesOf(s)),
                CaptureMethod: ParseEnum<CaptureMethod>(s.CaptureMethod),
                StopReason: ParseNullableEnum<FocusStopReason>(s.StopReason))).ToList();

            return new FocusTimeSummaryResponse(
                PlannedMinutes: slot.ProposedMinutes,
                ActualMinutes: Round(sessions.Sum(MinutesOf)),
                SessionCount: sessions.Count,
                Sessions: items);
        }

        // ------------------------------------------------------------------ Helpers

        // Slot (nếu có) phải của người dùng, đúng task và đang ở trạng thái làm được
        private async Task<ScheduleSlot?> GetStartableSlotAsync(Guid userId, Guid taskId, Guid? slotId)
        {
            if (slotId is not { } id)
            {
                return null;
            }

            var slot = await _slotRepository.GetByIdForUserAsync(id, userId)
                ?? throw new NotFoundException("Không tìm thấy slot.");

            if (slot.TaskId != taskId)
            {
                throw new UserFriendlyException("Slot không thuộc task này.");
            }

            if (!StartableSlotStatuses.Contains(slot.Status))
            {
                throw new UserFriendlyException(
                    $"Slot đang ở trạng thái '{slot.Status}' nên không thể làm việc trên đó. Hãy duyệt slot (Approved) trước, hoặc bắt đầu mà không gắn slot.");
            }

            return slot;
        }

        private async Task EnsureTrackUsableAsync(Guid? trackId)
        {
            if (trackId is not { } id)
            {
                return;
            }

            var track = await _focusTrackRepository.GetByIdAsync(id);
            if (track is null || !track.IsActive)
            {
                throw new UserFriendlyException("Bài nhạc tập trung không tồn tại hoặc đã bị tắt.");
            }
        }

        private static void EnsureOwner(FocusSession session, Guid userId)
        {
            if (session.Task?.Project is null)
            {
                throw new NotFoundException("Không tìm thấy task của phiên làm việc này.");
            }

            if (session.Task.Project.UserId != userId)
            {
                throw new FocusForbiddenException("Bạn không có quyền với phiên làm việc này.");
            }
        }

        private static FocusCurrentResponse ToCurrentResponse(FocusSession session, DateTimeOffset now)
        {
            var started = UtcTime.ToApi(session.StartedAt);
            var elapsedSeconds = (long)Math.Max(0, (now - started).TotalSeconds);
            var isOverrun = session.ScheduleSlot is not null && elapsedSeconds > session.ScheduleSlot.ProposedMinutes * 60L;

            return new FocusCurrentResponse(
                SessionId: session.Id,
                TaskId: session.TaskId,
                TaskTitle: session.Task?.Title ?? string.Empty,
                ScheduleSlotId: session.ScheduleSlotId,
                StartedAt: started,
                ElapsedSeconds: elapsedSeconds,
                ServerTime: now,
                IsOverrun: isOverrun);
        }

        // Phiên AutoTimeout chưa được xác nhận không được tính vào thời gian thực tế (cũng không dùng để học)
        private static bool CountsTowardsActual(FocusSession session)
        {
            return session.IsConfirmed || session.CaptureMethod != CaptureMethod.AutoTimeout.ToString();
        }

        private static double MinutesOf(FocusSession session)
        {
            return session.EndedAt is { } ended ? Math.Max(0, (ended - session.StartedAt).TotalMinutes) : 0;
        }

        private static double Round(double minutes) => Math.Round(minutes, 2);

        private static TEnum ParseEnum<TEnum>(string value)
            where TEnum : struct, Enum
        {
            return Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) ? result : default;
        }

        private static TEnum? ParseNullableEnum<TEnum>(string? value)
            where TEnum : struct, Enum
        {
            return value != null && Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) ? result : null;
        }
    }
}
