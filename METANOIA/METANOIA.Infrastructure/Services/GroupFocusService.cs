using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Bấm giờ làm việc trên việc của nhóm (docs/GROUP_FEATURE.md mục 11). Khác nhánh cá nhân: không có slot, lịch hay AI;
    // mỗi phiên ghi giờ của MỘT người trên một việc (hoặc việc con), nên báo cáo được giờ làm của từng người.
    //  - Chỉ người được giao bấm được (việc, hoặc việc con; việc con chưa giao thì người được giao việc cha).
    //  - Việc có việc con thì phải chọn việc con để bấm giờ.
    //  - Mỗi người một phiên đang chạy một lúc. Việc nhóm kiểm tra thêm phiên cá nhân đang chạy (chỉ đọc IFocusSessionRepository).
    //  - Start: việc con ToDo thành InProgress, việc cha tự suy lại. Stop Completed: việc con (hoặc việc không có việc con) thành Done.
    public class GroupFocusService : IGroupFocusService
    {
        // Quên bấm Stop: phiên dài hơn mức này bị cắt tại đây khi dừng, để giờ làm không phình ra vô lý
        private const int MaxSessionHours = 12;

        private readonly IGroupWorkRepository _work;
        private readonly IFocusSessionRepository _personalSessions;
        private readonly IGroupAccessService _access;
        private readonly GroupTaskProgress _progress;
        private readonly IUnitOfWork _unitOfWork;
        private readonly TimeProvider _timeProvider;

        public GroupFocusService(
            IGroupWorkRepository work,
            IFocusSessionRepository personalSessions,
            IGroupAccessService access,
            GroupTaskProgress progress,
            IUnitOfWork unitOfWork,
            TimeProvider timeProvider)
        {
            _work = work;
            _personalSessions = personalSessions;
            _access = access;
            _progress = progress;
            _unitOfWork = unitOfWork;
            _timeProvider = timeProvider;
        }

        public async Task<GroupFocusStartResponseDto> StartAsync(
            Guid userId, Guid taskId, GroupFocusStartRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await _work.GetTaskAsync(taskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc.");
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();
            access.RequireActive();

            if (GroupRules.IsClosed(task.Status))
            {
                throw new UserFriendlyException($"Việc đang ở trạng thái '{task.Status}'. Hãy mở lại việc trước khi làm.");
            }

            GroupSubTask? subTask = null;
            if (request.SubTaskId is { } subTaskId)
            {
                subTask = task.SubTasks.FirstOrDefault(s => s.Id == subTaskId)
                    ?? throw new UserFriendlyException("Việc con không thuộc việc này.");

                if (GroupRules.IsClosed(subTask.Status))
                {
                    throw new UserFriendlyException($"Việc con đang ở trạng thái '{subTask.Status}'. Hãy mở lại việc con (ToDo) trước khi làm.");
                }
            }
            else if (task.SubTasks.Count > 0)
            {
                throw new UserFriendlyException("Việc này có việc con. Hãy chọn việc con để bấm giờ.");
            }

            // Chỉ người được giao mới bấm giờ: việc con của mình, hoặc việc (không có việc con) giao cho mình
            var mine = subTask is not null
                ? subTask.AssigneeId == userId || (subTask.AssigneeId is null && task.AssigneeId == userId)
                : task.AssigneeId == userId;
            if (!mine)
            {
                throw new GroupForbiddenException("Bạn chỉ bấm giờ được việc (hoặc việc con) được giao cho mình.");
            }

            await EnsureNothingRunningAsync(userId, cancellationToken);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var session = new GroupFocusSession
            {
                Id = Guid.NewGuid(),
                GroupTaskId = task.Id,
                GroupSubTaskId = subTask?.Id,
                UserId = userId,
                StartedAt = now,
                CaptureMethod = "Timer"
            };

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (subTask is not null && subTask.Status == GroupWorkStatuses.ToDo)
                {
                    GroupRules.EnsureSubTask(subTask.Status, GroupWorkStatuses.InProgress);
                    subTask.Status = GroupWorkStatuses.InProgress;
                    await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.SubTask, subTask.Id,
                        "StatusChanged", GroupWorkStatuses.ToDo, GroupWorkStatuses.InProgress), cancellationToken);
                }
                else if (subTask is null && task.Status == GroupWorkStatuses.ToDo)
                {
                    GroupRules.EnsureTask(task.Status, GroupWorkStatuses.InProgress);
                    task.Status = GroupWorkStatuses.InProgress;
                    await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Task, task.Id,
                        "StatusChanged", GroupWorkStatuses.ToDo, GroupWorkStatuses.InProgress), cancellationToken);
                    await _progress.StartMilestoneIfNeededAsync(task, userId, cancellationToken);
                }

                await _work.AddFocusSessionAsync(session, cancellationToken);
                await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.FocusSession, session.Id,
                    "Started", null, subTask?.Title ?? task.Title), cancellationToken);

                if (subTask is not null)
                {
                    await _progress.RefreshTaskAsync(task, task.SubTasks.ToList(), userId, cancellationToken);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            return new GroupFocusStartResponseDto
            {
                SessionId = session.Id,
                GroupTaskId = task.Id,
                SubTaskId = subTask?.Id,
                StartedAt = UtcTime.ToApi(session.StartedAt),
                TaskStatus = task.Status,
                SubTaskStatus = subTask?.Status
            };
        }

        public async Task<GroupFocusStopResponseDto> StopAsync(
            Guid userId, Guid sessionId, GroupFocusStopRequestDto request, CancellationToken cancellationToken = default)
        {
            var session = await _work.GetFocusSessionAsync(sessionId, cancellationToken);

            // Phiên của người khác coi như không tồn tại; người đã rời nhóm cũng không dừng được (không còn quyền vào nhóm)
            if (session is null || session.UserId != userId)
            {
                throw new NotFoundException("Không tìm thấy phiên làm việc.");
            }

            var task = session.GroupTask;
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);

            if (session.EndedAt is { } alreadyEnded)
            {
                throw new FocusConflictException("Phiên này đã được dừng trước đó.",
                    new { sessionId = session.Id, endedAt = UtcTime.ToApi(alreadyEnded) });
            }

            if (request.StopReason is FocusStopReason.Blocked or FocusStopReason.Other && string.IsNullOrWhiteSpace(request.Note))
            {
                throw new UserFriendlyException($"Hãy ghi rõ lý do (note) khi dừng với lý do {request.StopReason}.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var wasCapped = false;
            var maxEnd = session.StartedAt.AddHours(MaxSessionHours);
            var endedAt = now;
            if (now > maxEnd)
            {
                endedAt = maxEnd;
                wasCapped = true;
            }

            session.EndedAt = endedAt;
            session.StopReason = request.StopReason.ToString();
            session.Note = wasCapped
                ? $"{request.Note} [Phiên bị cắt còn {MaxSessionHours} giờ vì quá dài]".Trim()
                : request.Note;

            var subTask = session.GroupSubTask;
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.FocusSession, session.Id,
                    "Stopped", session.StartedAt.ToString("O"), $"{request.StopReason}, {Round(Minutes(session, now))} phút"), cancellationToken);

                // Nhóm đã lưu trữ: vẫn ghi giờ đã làm (dừng được phiên) nhưng không đổi status
                if (request.StopReason == FocusStopReason.Completed && access.Group.Status == GroupStatuses.Active)
                {
                    if (subTask is not null)
                    {
                        if (GroupRules.IsOpenSubTask(subTask))
                        {
                            GroupRules.EnsureSubTask(subTask.Status, GroupWorkStatuses.Done);
                            var oldStatus = subTask.Status;
                            subTask.Status = GroupWorkStatuses.Done;
                            subTask.CompletedById = userId;
                            subTask.CompletedAt = now;
                            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.SubTask, subTask.Id,
                                "StatusChanged", oldStatus, GroupWorkStatuses.Done), cancellationToken);
                        }

                        await _progress.RefreshTaskAsync(task, task.SubTasks.ToList(), userId, cancellationToken);
                    }
                    else if (session.GroupSubTaskId is null && task.SubTasks.Count == 0 && !GroupRules.IsClosed(task.Status))
                    {
                        var oldStatus = task.Status;
                        GroupRules.EnsureTask(oldStatus, GroupWorkStatuses.Done);
                        task.Status = GroupWorkStatuses.Done;
                        task.CompletedAt ??= now;
                        await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Task, task.Id,
                            "StatusChanged", oldStatus, GroupWorkStatuses.Done), cancellationToken);
                        await _progress.StartMilestoneIfNeededAsync(task, userId, cancellationToken);
                    }
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            var sessions = await _work.GetFocusSessionsOfTaskAsync(task.Id, cancellationToken);
            var finished = sessions.Where(s => s.EndedAt is not null).ToList();

            return new GroupFocusStopResponseDto
            {
                SessionId = session.Id,
                EndedAt = UtcTime.ToApi(endedAt),
                SessionMinutes = Round(Minutes(session, now)),
                MyTaskMinutes = Round(finished.Where(s => s.UserId == userId).Sum(s => Minutes(s, now))),
                TaskTotalMinutes = Round(finished.Sum(s => Minutes(s, now))),
                TaskStatus = task.Status,
                SubTaskStatus = subTask?.Status,
                WasCapped = wasCapped
            };
        }

        public async Task<GroupFocusCurrentDto?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var running = await _work.GetRunningFocusSessionAsync(userId, cancellationToken);
            return running is null ? null : ToCurrent(running, _timeProvider.GetUtcNow());
        }

        public async Task<GroupTaskTimeDto> GetTaskTimeAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await _work.GetTaskAsync(taskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc.");
            await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var sessions = await _work.GetFocusSessionsOfTaskAsync(taskId, cancellationToken);
            var finished = sessions.Where(s => s.EndedAt is not null).ToList();

            var byUser = finished
                .GroupBy(s => s.UserId)
                .Select(g => new GroupTimeByUserDto
                {
                    UserId = g.Key,
                    FullName = g.First().User.FullName,
                    Minutes = Round(g.Sum(s => Minutes(s, now))),
                    SessionCount = g.Count()
                })
                .OrderByDescending(u => u.Minutes)
                .ToList();

            var bySubTask = finished
                .GroupBy(s => s.GroupSubTaskId)
                .Select(g => new GroupTimeBySubTaskDto
                {
                    SubTaskId = g.Key,
                    Title = g.Key is { } id ? task.SubTasks.FirstOrDefault(x => x.Id == id)?.Title : null,
                    Minutes = Round(g.Sum(s => Minutes(s, now)))
                })
                .OrderByDescending(x => x.Minutes)
                .ToList();

            return new GroupTaskTimeDto
            {
                GroupTaskId = taskId,
                EstimatedMinutes = task.EstimatedMinutes,
                TotalMinutes = Round(finished.Sum(s => Minutes(s, now))),
                SessionCount = finished.Count,
                ByUser = byUser,
                BySubTask = bySubTask,
                Sessions = sessions.Select(s => new GroupFocusSessionDto
                {
                    Id = s.Id,
                    GroupTaskId = s.GroupTaskId,
                    SubTaskId = s.GroupSubTaskId,
                    UserId = s.UserId,
                    UserName = s.User.FullName,
                    StartedAt = UtcTime.ToApi(s.StartedAt),
                    EndedAt = UtcTime.ToApi(s.EndedAt),
                    Minutes = Round(Minutes(s, now)),
                    IsRunning = s.EndedAt is null,
                    StopReason = s.StopReason,
                    Note = s.Note
                }).ToList()
            };
        }

        // Mỗi người một phiên chạy một lúc, tính cả phiên cá nhân (chỉ đọc). 409 kèm phiên đang chạy để FE đưa người dùng tới đó
        private async Task EnsureNothingRunningAsync(Guid userId, CancellationToken cancellationToken)
        {
            var running = await _work.GetRunningFocusSessionAsync(userId, cancellationToken);
            if (running is not null)
            {
                throw new FocusConflictException("Bạn đang có một phiên làm việc của nhóm chưa dừng.",
                    new { source = "Group", session = ToCurrent(running, _timeProvider.GetUtcNow()) });
            }

            var personal = await _personalSessions.GetRunningWithDetailsAsync(userId);
            if (personal is not null)
            {
                throw new FocusConflictException("Bạn đang có một phiên làm việc cá nhân chưa dừng. Hãy dừng phiên đó trước.",
                    new { source = "Personal", sessionId = personal.Id, taskId = personal.TaskId });
            }
        }

        private static GroupFocusCurrentDto ToCurrent(GroupFocusSession session, DateTimeOffset now)
        {
            var started = UtcTime.ToApi(session.StartedAt);
            return new GroupFocusCurrentDto
            {
                SessionId = session.Id,
                GroupId = session.GroupTask.GroupId,
                GroupName = session.GroupTask.Group.Name,
                GroupTaskId = session.GroupTaskId,
                TaskTitle = session.GroupTask.Title,
                SubTaskId = session.GroupSubTaskId,
                SubTaskTitle = session.GroupSubTask?.Title,
                StartedAt = started,
                ElapsedSeconds = (long)Math.Max(0, (now - started).TotalSeconds),
                ServerTime = now,
                EstimatedMinutes = session.GroupTask.EstimatedMinutes
            };
        }

        // Số phút của một phiên; phiên đang chạy tính tới thời điểm hiện tại
        private static double Minutes(GroupFocusSession session, DateTime nowUtc)
        {
            var end = session.EndedAt ?? nowUtc;
            return Math.Max(0, (end - session.StartedAt).TotalMinutes);
        }

        private static double Round(double minutes) => Math.Round(minutes, 2);
    }
}
