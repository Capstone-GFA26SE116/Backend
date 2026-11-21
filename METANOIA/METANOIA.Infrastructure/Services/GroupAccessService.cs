using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class GroupAccessService : IGroupAccessService
    {
        private readonly IGroupRepository _groups;

        public GroupAccessService(IGroupRepository groups)
        {
            _groups = groups;
        }

        public async Task<GroupAccess> GetAccessAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
        {
            var member = await _groups.GetMemberAsync(groupId, userId, cancellationToken);
            var group = member is null ? null : await _groups.GetGroupAsync(groupId, cancellationToken);
            if (member is null || group is null)
            {
                // Người ngoài nhóm cũng nhận 404, không lộ nhóm có tồn tại hay không
                throw new NotFoundException("Không tìm thấy nhóm.");
            }

            return new GroupAccess(group, userId, member.Role);
        }
    }

    // Thông báo nhóm đi qua NotificationService (cùng hộp thư Notification, SignalR, Web Push).
    // Chạy trong scope riêng (DbContext riêng): nếu việc ghi thông báo lỗi thì không làm hỏng DbContext của request đang xử lý.
    public sealed class GroupNotifier : IGroupNotifier
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GroupNotifier> _logger;

        public GroupNotifier(IServiceScopeFactory scopeFactory, ILogger<GroupNotifier> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task NotifyAsync(Guid userId, string type, string title, string message, Guid groupId, Guid? groupTaskId = null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notifications.SendAsync(userId, title, message, type, groupId: groupId, groupTaskId: groupTaskId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không gửi được thông báo nhóm {Type} cho user {UserId}.", type, userId);
            }
        }
    }
}
