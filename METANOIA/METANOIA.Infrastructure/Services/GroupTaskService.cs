using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Việc và việc con của nhóm (docs/GROUP_FEATURE.md mục 4 và 5).
    //  - Leader tạo, sửa, xóa việc và việc con, và giao việc.
    //  - Member chỉ đổi status việc được giao cho mình. Việc có việc con thì status tự suy từ việc con (GroupRules.Derive).
    //  - Viewer chỉ xem.
    public class GroupTaskService : IGroupTaskService
    {
        private const int MaxTitleLength = 300;
        private const int MaxDescriptionLength = 4000;

        private readonly IGroupWorkRepository _work;
        private readonly IGroupRepository _groups;
        private readonly IGroupAccessService _access;
        private readonly IGroupNotifier _notifier;
        private readonly IUnitOfWork _unitOfWork;
        private readonly GroupTaskProgress _progress;
        private readonly IGroupAvailabilityService _availability;

        public GroupTaskService(
            IGroupWorkRepository work,
            IGroupRepository groups,
            IGroupAccessService access,
            IGroupNotifier notifier,
            IUnitOfWork unitOfWork,
            GroupTaskProgress progress,
            IGroupAvailabilityService availability)
        {
            _work = work;
            _groups = groups;
            _access = access;
            _notifier = notifier;
            _unitOfWork = unitOfWork;
            _progress = progress;
            _availability = availability;
        }

        // ================================================================ Việc

        public async Task<IReadOnlyList<GroupTaskDto>> GetTasksAsync(
            Guid userId, Guid groupId, string? status, Guid? assigneeId, Guid? milestoneId, CancellationToken cancellationToken = default)
        {
            await _access.GetAccessAsync(groupId, userId, cancellationToken);

            if (!string.IsNullOrWhiteSpace(status) && !GroupWorkStatuses.IsTaskStatus(status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: ToDo, InProgress, Done, Skipped.");
            }

            var tasks = await _work.GetTasksAsync(groupId, status, assigneeId, milestoneId, cancellationToken);
            return tasks.Select(t => ToDto(t, t.Assignee?.FullName)).ToList();
        }

        public async Task<GroupTaskDto> GetTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            return ToDto(task, task.Assignee?.FullName);
        }

        public async Task<GroupTaskDto> CreateTaskAsync(Guid userId, Guid groupId, GroupTaskRequestDto request, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            ValidateRequest(request);
            await ValidateMilestoneAsync(groupId, request.MilestoneId, null, cancellationToken);
            await EnsureAssignableAsync(groupId, request.AssigneeId, cancellationToken);
            if (request.AssigneeId is { } newAssignee)
            {
                await _availability.EnsureAvailableAsync(newAssignee, ToStorage(request.FixedStart), ToStorage(request.FixedEnd),
                    ToStorage(request.Deadline), request.EstimatedMinutes, null, request.ConfirmConflicts, cancellationToken);
            }

            var task = new GroupTask
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                AssigneeId = request.AssigneeId,
                Status = GroupWorkStatuses.ToDo,
                CreatedAt = DateTime.UtcNow
            };
            ApplyRequest(task, request);

            await _work.AddTaskAsync(task, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(groupId, userId, GroupEntityTypes.Task, task.Id, "Created", null, task.Title), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyAssignedAsync(access, task.AssigneeId, $"việc \"{task.Title}\"", task.Id, cancellationToken);

            var names = await _work.GetUserNamesAsync(Ids(task.AssigneeId), cancellationToken);
            return ToDto(task, NameOf(names, task.AssigneeId));
        }

        public async Task<GroupTaskDto> UpdateTaskAsync(Guid userId, Guid taskId, GroupTaskRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            ValidateRequest(request);

            // Đổi sang giai đoạn khác thì giai đoạn mới phải thuộc nhóm và chưa giao cho khách
            if (request.MilestoneId != task.MilestoneId)
            {
                await ValidateMilestoneAsync(task.GroupId, request.MilestoneId, task.Status, cancellationToken);
            }

            var timingChanged = task.FixedStart != ToStorage(request.FixedStart) || task.FixedEnd != ToStorage(request.FixedEnd)
                                || task.Deadline != ToStorage(request.Deadline) || task.EstimatedMinutes != request.EstimatedMinutes;
            if (timingChanged && task.AssigneeId is { } currentAssignee && !GroupRules.IsClosed(task.Status))
            {
                await _availability.EnsureAvailableAsync(currentAssignee, ToStorage(request.FixedStart), ToStorage(request.FixedEnd),
                    ToStorage(request.Deadline), request.EstimatedMinutes, task.Id, request.ConfirmConflicts, cancellationToken);
            }

            var oldTitle = task.Title;
            ApplyRequest(task, request);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Task, task.Id, "Updated", oldTitle, task.Title), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(task, task.Assignee?.FullName);
        }

        public async Task DeleteTaskAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();

            _work.RemoveTask(task);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Task, task.Id, "Deleted", task.Title, null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<GroupTaskDto> ChangeTaskStatusAsync(Guid userId, Guid taskId, GroupTaskStatusRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();
            access.RequireActive();

            if (!GroupWorkStatuses.IsTaskStatus(request.Status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: ToDo, InProgress, Done, Skipped.");
            }

            // Member chỉ đổi việc được giao cho mình, và chỉ khi việc không có việc con (có việc con thì status tự suy)
            if (!access.IsLeader && (task.AssigneeId != userId || task.SubTasks.Count > 0))
            {
                throw new GroupForbiddenException(task.AssigneeId != userId
                    ? "Bạn chỉ đổi được status của việc được giao cho mình."
                    : "Việc này có việc con nên status tự suy từ việc con. Hãy đổi status của việc con.");
            }

            var oldStatus = task.Status;
            var newStatus = request.Status;
            GroupRules.EnsureTask(oldStatus, newStatus);

            if (oldStatus != newStatus)
            {
                task.Status = newStatus;
                task.CompletedAt = newStatus == GroupWorkStatuses.Done ? task.CompletedAt ?? DateTime.UtcNow : null;

                // Đóng việc bằng tay: việc con còn dở thành Skipped
                if (GroupRules.IsClosed(newStatus))
                {
                    GroupRules.CascadeClose(task.SubTasks);
                }

                await StartMilestoneIfNeededAsync(task, userId, cancellationToken);
                await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Task, task.Id, "StatusChanged", oldStatus, newStatus), cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await NotifyStatusChangedAsync(access, task.AssigneeId, $"việc \"{task.Title}\"", oldStatus, newStatus, task.Id, cancellationToken);
            }

            return ToDto(task, task.Assignee?.FullName);
        }

        public async Task<GroupTaskDto> AssignTaskAsync(Guid userId, Guid taskId, GroupAssigneeRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            await EnsureAssignableAsync(task.GroupId, request.AssigneeId, cancellationToken);

            if (task.AssigneeId == request.AssigneeId)
            {
                return ToDto(task, task.Assignee?.FullName);
            }

            if (request.AssigneeId is { } newAssignee && !GroupRules.IsClosed(task.Status))
            {
                await _availability.EnsureAvailableAsync(newAssignee, task.FixedStart, task.FixedEnd, task.Deadline,
                    task.EstimatedMinutes, task.Id, request.ConfirmConflicts, cancellationToken);
            }

            var oldAssignee = task.AssigneeId;
            task.AssigneeId = request.AssigneeId;

            var names = await _work.GetUserNamesAsync(new[] { oldAssignee, request.AssigneeId }.OfType<Guid>(), cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.Task, task.Id, "Assigned",
                NameOf(names, oldAssignee), NameOf(names, request.AssigneeId)), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyAssignedAsync(access, request.AssigneeId, $"việc \"{task.Title}\"", task.Id, cancellationToken);

            return ToDto(task, NameOf(names, request.AssigneeId));
        }

        // ================================================================ Việc con

        public async Task<IReadOnlyList<GroupSubTaskDto>> GetSubTasksAsync(Guid userId, Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);

            var names = await _work.GetUserNamesAsync(task.SubTasks.Select(s => s.AssigneeId).OfType<Guid>(), cancellationToken);
            return task.SubTasks.OrderBy(s => s.OrderIndex).Select(s => ToDto(s, NameOf(names, s.AssigneeId))).ToList();
        }

        public async Task<GroupSubTaskDto> GetSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default)
        {
            var subTask = await GetSubTaskOrThrowAsync(subTaskId, cancellationToken);
            await _access.GetAccessAsync(subTask.GroupTask.GroupId, userId, cancellationToken);
            return ToDto(subTask, subTask.Assignee?.FullName);
        }

        public async Task<GroupSubTaskDto> CreateSubTaskAsync(Guid userId, Guid taskId, GroupSubTaskRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = await GetTaskOrThrowAsync(taskId, cancellationToken);
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            ValidateSubTaskRequest(request);
            await EnsureAssignableAsync(task.GroupId, request.AssigneeId, cancellationToken);

            var orderIndex = request.OrderIndex ?? await _work.NextSubTaskOrderIndexAsync(taskId, cancellationToken);
            if (await _work.SubTaskOrderIndexTakenAsync(taskId, orderIndex, Guid.Empty, cancellationToken))
            {
                throw new UserFriendlyException("Số thứ tự việc con đã tồn tại trong việc này.");
            }

            var subTask = new GroupSubTask
            {
                Id = Guid.NewGuid(),
                GroupTaskId = taskId,
                AssigneeId = request.AssigneeId,
                Title = request.Title.Trim(),
                EstimatedMinutes = request.EstimatedMinutes,
                OrderIndex = orderIndex,
                Status = GroupWorkStatuses.ToDo
            };

            await _work.AddSubTaskAsync(subTask, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.SubTask, subTask.Id, "Created", null, subTask.Title), cancellationToken);

            // Việc con mới (ToDo) có thể mở lại một việc đã Done
            await RefreshTaskAsync(task, task.SubTasks.Append(subTask).Distinct().ToList(), userId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyAssignedAsync(access, subTask.AssigneeId, $"việc con \"{subTask.Title}\" (việc \"{task.Title}\")", task.Id, cancellationToken);

            var names = await _work.GetUserNamesAsync(Ids(subTask.AssigneeId), cancellationToken);
            return ToDto(subTask, NameOf(names, subTask.AssigneeId));
        }

        public async Task<GroupSubTaskDto> UpdateSubTaskAsync(Guid userId, Guid subTaskId, GroupSubTaskRequestDto request, CancellationToken cancellationToken = default)
        {
            var subTask = await GetSubTaskOrThrowAsync(subTaskId, cancellationToken);
            var access = await _access.GetAccessAsync(subTask.GroupTask.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            ValidateSubTaskRequest(request);

            var orderIndex = request.OrderIndex ?? subTask.OrderIndex;
            if (orderIndex != subTask.OrderIndex
                && await _work.SubTaskOrderIndexTakenAsync(subTask.GroupTaskId, orderIndex, subTaskId, cancellationToken))
            {
                throw new UserFriendlyException("Số thứ tự việc con đã tồn tại trong việc này.");
            }

            var oldTitle = subTask.Title;
            subTask.Title = request.Title.Trim();
            subTask.EstimatedMinutes = request.EstimatedMinutes;
            subTask.OrderIndex = orderIndex;

            await _work.AddActivityAsync(GroupRules.Log(subTask.GroupTask.GroupId, userId, GroupEntityTypes.SubTask, subTask.Id, "Updated", oldTitle, subTask.Title), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(subTask, subTask.Assignee?.FullName);
        }

        public async Task DeleteSubTaskAsync(Guid userId, Guid subTaskId, CancellationToken cancellationToken = default)
        {
            var subTask = await GetSubTaskOrThrowAsync(subTaskId, cancellationToken);
            var task = subTask.GroupTask;
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();

            _work.RemoveSubTask(subTask);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.SubTask, subTask.Id, "Deleted", subTask.Title, null), cancellationToken);

            // Xóa việc con cuối cùng chưa xong có thể làm việc cha đủ điều kiện Done
            await RefreshTaskAsync(task, task.SubTasks.Where(s => s.Id != subTask.Id).ToList(), userId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<GroupSubTaskDto> ChangeSubTaskStatusAsync(Guid userId, Guid subTaskId, GroupTaskStatusRequestDto request, CancellationToken cancellationToken = default)
        {
            var subTask = await GetSubTaskOrThrowAsync(subTaskId, cancellationToken);
            var task = subTask.GroupTask;
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireCanWork();
            access.RequireActive();

            if (!GroupWorkStatuses.IsTaskStatus(request.Status))
            {
                throw new UserFriendlyException("Trạng thái không hợp lệ. Chọn một trong: ToDo, InProgress, Done, Skipped.");
            }

            // Member đổi được việc con của mình; việc con chưa giao thì người được giao việc cha đổi được
            var mine = subTask.AssigneeId == userId || (subTask.AssigneeId is null && task.AssigneeId == userId);
            if (!access.IsLeader && !mine)
            {
                throw new GroupForbiddenException("Bạn chỉ đổi được status của việc con được giao cho mình.");
            }

            var oldStatus = subTask.Status;
            var newStatus = request.Status;
            GroupRules.EnsureSubTask(oldStatus, newStatus);

            if (oldStatus != newStatus)
            {
                subTask.Status = newStatus;
                subTask.CompletedById = newStatus == GroupWorkStatuses.Done ? userId : null;
                subTask.CompletedAt = newStatus == GroupWorkStatuses.Done ? DateTime.UtcNow : null;

                await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.SubTask, subTask.Id, "StatusChanged", oldStatus, newStatus), cancellationToken);
                await RefreshTaskAsync(task, task.SubTasks.ToList(), userId, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await NotifyStatusChangedAsync(access, subTask.AssigneeId, $"việc con \"{subTask.Title}\"", oldStatus, newStatus, task.Id, cancellationToken);
            }

            return ToDto(subTask, subTask.Assignee?.FullName);
        }

        public async Task<GroupSubTaskDto> AssignSubTaskAsync(Guid userId, Guid subTaskId, GroupAssigneeRequestDto request, CancellationToken cancellationToken = default)
        {
            var subTask = await GetSubTaskOrThrowAsync(subTaskId, cancellationToken);
            var task = subTask.GroupTask;
            var access = await _access.GetAccessAsync(task.GroupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();
            await EnsureAssignableAsync(task.GroupId, request.AssigneeId, cancellationToken);

            if (subTask.AssigneeId == request.AssigneeId)
            {
                return ToDto(subTask, subTask.Assignee?.FullName);
            }

            var oldAssignee = subTask.AssigneeId;
            subTask.AssigneeId = request.AssigneeId;

            var names = await _work.GetUserNamesAsync(new[] { oldAssignee, request.AssigneeId }.OfType<Guid>(), cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(task.GroupId, userId, GroupEntityTypes.SubTask, subTask.Id, "Assigned",
                NameOf(names, oldAssignee), NameOf(names, request.AssigneeId)), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyAssignedAsync(access, request.AssigneeId, $"việc con \"{subTask.Title}\" (việc \"{task.Title}\")", task.Id, cancellationToken);

            return ToDto(subTask, NameOf(names, request.AssigneeId));
        }

        // ================================================================ Dùng chung

        // Suy lại status việc cha sau khi việc con đổi (luật dùng chung với bấm giờ: GroupTaskProgress)
        private Task RefreshTaskAsync(
            GroupTask task, IReadOnlyCollection<GroupSubTask> subTasks, Guid actorId, CancellationToken cancellationToken)
        {
            return _progress.RefreshTaskAsync(task, subTasks, actorId, cancellationToken);
        }

        private Task StartMilestoneIfNeededAsync(GroupTask task, Guid actorId, CancellationToken cancellationToken)
        {
            return _progress.StartMilestoneIfNeededAsync(task, actorId, cancellationToken);
        }

        // Chỉ giao cho thành viên có vai trò Leader hoặc Member (Viewer không nhận việc)
        private async Task EnsureAssignableAsync(Guid groupId, Guid? assigneeId, CancellationToken cancellationToken)
        {
            if (assigneeId is not { } id)
            {
                return;
            }

            var member = await _groups.GetMemberAsync(groupId, id, cancellationToken);
            if (member is null)
            {
                throw new UserFriendlyException("Chỉ giao việc được cho thành viên của nhóm.");
            }

            if (member.Role == GroupRoles.Viewer)
            {
                throw new UserFriendlyException("Viewer chỉ được xem, không nhận việc được. Hãy đổi vai trò trước.");
            }
        }

        private async Task ValidateMilestoneAsync(Guid groupId, Guid? milestoneId, string? taskStatus, CancellationToken cancellationToken)
        {
            if (milestoneId is not { } id)
            {
                return;
            }

            var milestone = await _work.GetMilestoneAsync(id, cancellationToken);
            if (milestone is null || milestone.GroupId != groupId)
            {
                throw new UserFriendlyException("Giai đoạn không tồn tại trong nhóm này.");
            }

            // Giai đoạn đã giao cho khách thì muốn thêm việc phải chuyển InRevision trước (việc đã đóng thì không ảnh hưởng)
            if ((taskStatus is null || !GroupRules.IsClosed(taskStatus))
                && milestone.Status is GroupWorkStatuses.Delivered or GroupWorkStatuses.Approved)
            {
                throw new UserFriendlyException(
                    $"Giai đoạn đang ở trạng thái '{milestone.Status}' nên không nhận thêm việc. Hãy chuyển giai đoạn sang InRevision trước.");
            }
        }

        private async Task<GroupTask> GetTaskOrThrowAsync(Guid taskId, CancellationToken cancellationToken)
        {
            return await _work.GetTaskAsync(taskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc.");
        }

        private async Task<GroupSubTask> GetSubTaskOrThrowAsync(Guid subTaskId, CancellationToken cancellationToken)
        {
            return await _work.GetSubTaskAsync(subTaskId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy việc con.");
        }

        private async Task NotifyAssignedAsync(GroupAccess access, Guid? assigneeId, string what, Guid taskId, CancellationToken cancellationToken)
        {
            if (assigneeId is not { } id || id == access.UserId)
            {
                return;
            }

            var actor = (await _work.GetUserNamesAsync([access.UserId], cancellationToken)).GetValueOrDefault(access.UserId, "Một thành viên");
            await _notifier.NotifyAsync(id, GroupNotificationTypes.Assigned, "Bạn được giao việc",
                $"{actor} giao cho bạn {what} trong nhóm \"{access.Group.Name}\".", access.Group.Id, taskId);
        }

        private async Task NotifyStatusChangedAsync(
            GroupAccess access, Guid? assigneeId, string what, string oldStatus, string newStatus, Guid taskId, CancellationToken cancellationToken)
        {
            // Người được giao chỉ cần biết khi người KHÁC đổi status việc của mình
            if (assigneeId is not { } id || id == access.UserId)
            {
                return;
            }

            var actor = (await _work.GetUserNamesAsync([access.UserId], cancellationToken)).GetValueOrDefault(access.UserId, "Một thành viên");
            await _notifier.NotifyAsync(id, GroupNotificationTypes.StatusChanged, "Status việc được thay đổi",
                $"{actor} đổi {what} trong nhóm \"{access.Group.Name}\" từ {oldStatus} sang {newStatus}.", access.Group.Id, taskId);
        }

        private static IEnumerable<Guid> Ids(Guid? id)
        {
            return id is { } value ? new[] { value } : Array.Empty<Guid>();
        }

        private static string? NameOf(Dictionary<Guid, string> names, Guid? id)
        {
            return id is { } value && names.TryGetValue(value, out var name) ? name : null;
        }

        private static void ValidateRequest(GroupTaskRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
            {
                throw new UserFriendlyException($"Tiêu đề việc không được để trống và tối đa {MaxTitleLength} ký tự.");
            }

            if (request.Description is { Length: > MaxDescriptionLength })
            {
                throw new UserFriendlyException($"Mô tả việc tối đa {MaxDescriptionLength} ký tự.");
            }

            if (!string.IsNullOrWhiteSpace(request.Priority) && !GroupPriorities.IsValid(request.Priority))
            {
                throw new UserFriendlyException("Độ ưu tiên không hợp lệ. Chọn một trong: Low, Medium, High.");
            }

            if (request.EstimatedMinutes is <= 0)
            {
                throw new UserFriendlyException("Số phút ước lượng phải lớn hơn 0.");
            }

            if ((request.FixedStart is null) != (request.FixedEnd is null))
            {
                throw new UserFriendlyException("Giờ cố định phải có cả giờ bắt đầu và giờ kết thúc.");
            }

            if (request.FixedStart is { } fixedStart && request.FixedEnd is { } fixedEnd && fixedEnd <= fixedStart)
            {
                throw new UserFriendlyException("Giờ kết thúc phải sau giờ bắt đầu.");
            }
        }

        private static DateTime? ToStorage(DateTimeOffset? value)
        {
            return value is { } v ? UtcTime.ToStorage(v) : null;
        }

        private static void ValidateSubTaskRequest(GroupSubTaskRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
            {
                throw new UserFriendlyException($"Tên việc con không được để trống và tối đa {MaxTitleLength} ký tự.");
            }

            if (request.EstimatedMinutes is <= 0)
            {
                throw new UserFriendlyException("Số phút ước lượng phải lớn hơn 0.");
            }

            if (request.OrderIndex is < 1)
            {
                throw new UserFriendlyException("Số thứ tự việc con phải từ 1 trở lên.");
            }
        }

        private static void ApplyRequest(GroupTask task, GroupTaskRequestDto request)
        {
            task.Title = request.Title.Trim();
            task.Description = request.Description;
            task.Priority = string.IsNullOrWhiteSpace(request.Priority) ? GroupPriorities.Medium : request.Priority;
            task.MilestoneId = request.MilestoneId;
            task.Deadline = ToStorage(request.Deadline);
            task.FixedStart = ToStorage(request.FixedStart);
            task.FixedEnd = ToStorage(request.FixedEnd);
            task.EstimatedMinutes = request.EstimatedMinutes;
        }

        private static GroupTaskDto ToDto(GroupTask task, string? assigneeName)
        {
            return new GroupTaskDto
            {
                Id = task.Id,
                GroupId = task.GroupId,
                MilestoneId = task.MilestoneId,
                AssigneeId = task.AssigneeId,
                AssigneeName = assigneeName,
                Title = task.Title,
                Description = task.Description,
                Priority = task.Priority,
                Status = task.Status,
                Deadline = UtcTime.ToApi(task.Deadline),
                FixedStart = UtcTime.ToApi(task.FixedStart),
                FixedEnd = UtcTime.ToApi(task.FixedEnd),
                EstimatedMinutes = task.EstimatedMinutes,
                SubTaskCount = task.SubTasks.Count,
                SubTaskDoneCount = task.SubTasks.Count(s => s.Status == GroupWorkStatuses.Done),
                CreatedAt = task.CreatedAt,
                CompletedAt = task.CompletedAt
            };
        }

        private static GroupSubTaskDto ToDto(GroupSubTask subTask, string? assigneeName)
        {
            return new GroupSubTaskDto
            {
                Id = subTask.Id,
                GroupTaskId = subTask.GroupTaskId,
                AssigneeId = subTask.AssigneeId,
                AssigneeName = assigneeName,
                Title = subTask.Title,
                EstimatedMinutes = subTask.EstimatedMinutes,
                OrderIndex = subTask.OrderIndex,
                Status = subTask.Status,
                CompletedById = subTask.CompletedById,
                CompletedAt = subTask.CompletedAt
            };
        }
    }
}
