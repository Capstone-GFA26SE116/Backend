using System.Security.Cryptography;
using System.Text;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using METANOIA.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    // Lời mời vào nhóm. Chưa có dịch vụ gửi email: Leader nhận AcceptToken khi tạo lời mời để gửi liên kết;
    // người được mời cũng thấy lời mời gửi tới email của mình trong GET /api/group-invitations.
    public class GroupInvitationService : IGroupInvitationService
    {
        private const int InvitationDays = 7;
        private const int MaxEmailLength = 254;

        private readonly IGroupRepository _groups;
        private readonly IGroupWorkRepository _work;
        private readonly IGroupAccessService _access;
        private readonly IUserRepository _users;
        private readonly IGroupNotifier _notifier;
        private readonly IUnitOfWork _unitOfWork;
        private readonly TimeProvider _timeProvider;

        public GroupInvitationService(
            IGroupRepository groups,
            IGroupWorkRepository work,
            IGroupAccessService access,
            IUserRepository users,
            IGroupNotifier notifier,
            IUnitOfWork unitOfWork,
            TimeProvider timeProvider)
        {
            _groups = groups;
            _work = work;
            _access = access;
            _users = users;
            _notifier = notifier;
            _unitOfWork = unitOfWork;
            _timeProvider = timeProvider;
        }

        public async Task<GroupInvitationCreatedDto> CreateInvitationAsync(
            Guid userId, Guid groupId, GroupInvitationRequestDto request, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();
            access.RequireActive();

            var email = NormalizeEmail(request.Email);
            var role = string.IsNullOrWhiteSpace(request.Role) ? GroupRoles.Member : request.Role.Trim();
            if (!GroupRoles.IsValid(role))
            {
                throw new UserFriendlyException("Vai trò không hợp lệ. Chọn một trong: Leader, Member, Viewer.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var invitee = await _users.GetByEmailAsync(email, cancellationToken);
            if (invitee is not null && await _groups.GetMemberAsync(groupId, invitee.Id, cancellationToken) is not null)
            {
                throw new UserFriendlyException("Người này đã là thành viên của nhóm.");
            }

            var pending = await _groups.GetPendingInvitationAsync(groupId, email, cancellationToken);
            if (pending is not null)
            {
                if (pending.ExpiresAt > now)
                {
                    throw new UserFriendlyException("Đã có một lời mời đang chờ cho email này. Hãy thu hồi lời mời đó nếu muốn gửi lại.");
                }

                // Lời mời cũ đã quá hạn: đánh dấu rồi cho phép mời lại. Lưu NGAY, trước khi thêm lời mời mới: database có chỉ mục duy nhất
                // "một lời mời Pending cho mỗi email trong một nhóm" (UX_GroupInvitation_Pending) mà EF không biết, nên để chung một lần lưu
                // thì dòng mới có thể được chèn trước khi dòng cũ đổi sang Expired.
                pending.Status = GroupInvitationStatuses.Expired;
                pending.RespondedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var token = GenerateToken();
            var invitation = new GroupInvitation
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Email = email,
                Role = role,
                TokenHash = Hash(token),
                InvitedById = userId,
                Status = GroupInvitationStatuses.Pending,
                CreatedAt = now,
                ExpiresAt = now.AddDays(InvitationDays)
            };

            await _groups.AddInvitationAsync(invitation, cancellationToken);
            await _work.AddActivityAsync(GroupRules.Log(groupId, userId, GroupEntityTypes.Invitation, invitation.Id, "Invited", null, $"{email} ({role})"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var inviter = await _users.GetByIdAsync(userId, cancellationToken);
            var inviterName = inviter?.FullName ?? string.Empty;

            if (invitee is not null)
            {
                await _notifier.NotifyAsync(invitee.Id, GroupNotificationTypes.Invited, "Lời mời vào nhóm",
                    $"{inviterName} mời bạn vào nhóm \"{access.Group.Name}\" với vai trò {role}.", groupId);
            }

            var dto = ToDto<GroupInvitationCreatedDto>(invitation, access.Group.Name, inviterName, now);
            dto.AcceptToken = token;
            return dto;
        }

        public async Task<IReadOnlyList<GroupInvitationDto>> GetInvitationsOfGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var rows = await _groups.GetInvitationsOfGroupAsync(groupId, cancellationToken);
            return rows.Select(i => ToDto<GroupInvitationDto>(i, i.Group.Name, i.InvitedBy.FullName, now)).ToList();
        }

        public async Task RevokeInvitationAsync(Guid userId, Guid groupId, Guid invitationId, CancellationToken cancellationToken = default)
        {
            var access = await _access.GetAccessAsync(groupId, userId, cancellationToken);
            access.RequireLeader();

            var invitation = await _groups.GetInvitationAsync(invitationId, cancellationToken);
            if (invitation is null || invitation.GroupId != groupId)
            {
                throw new NotFoundException("Không tìm thấy lời mời.");
            }

            if (invitation.Status != GroupInvitationStatuses.Pending)
            {
                throw new UserFriendlyException("Lời mời này đã được trả lời hoặc đã bị thu hồi.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            invitation.Status = GroupInvitationStatuses.Revoked;
            invitation.RespondedAt = now;
            await _work.AddActivityAsync(GroupRules.Log(groupId, userId, GroupEntityTypes.Invitation, invitation.Id, "InvitationRevoked", invitation.Email, null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GroupInvitationDto>> GetMyInvitationsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await GetUserAsync(userId, cancellationToken);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var rows = await _groups.GetPendingInvitationsForEmailAsync(user.Email.ToLowerInvariant(), now, cancellationToken);
            return rows.Select(i => ToDto<GroupInvitationDto>(i, i.Group.Name, i.InvitedBy.FullName, now)).ToList();
        }

        public async Task<GroupDto> AcceptInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
        {
            var invitation = await _groups.GetInvitationAsync(invitationId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy lời mời.");
            return await AcceptCoreAsync(userId, invitation, cancellationToken);
        }

        public async Task<GroupDto> AcceptInvitationByTokenAsync(Guid userId, string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new UserFriendlyException("Token lời mời không được để trống.");
            }

            var invitation = await _groups.GetInvitationByTokenHashAsync(Hash(token.Trim()), cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy lời mời.");
            return await AcceptCoreAsync(userId, invitation, cancellationToken);
        }

        public async Task DeclineInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
        {
            var user = await GetUserAsync(userId, cancellationToken);
            var invitation = await _groups.GetInvitationAsync(invitationId, cancellationToken);
            if (invitation is null || invitation.Email != user.Email.ToLowerInvariant())
            {
                throw new NotFoundException("Không tìm thấy lời mời.");
            }

            if (invitation.Status != GroupInvitationStatuses.Pending)
            {
                throw new UserFriendlyException("Lời mời này đã được trả lời hoặc đã bị thu hồi.");
            }

            invitation.Status = GroupInvitationStatuses.Declined;
            invitation.RespondedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _work.AddActivityAsync(GroupRules.Log(invitation.GroupId, userId, GroupEntityTypes.Invitation, invitation.Id, "InvitationDeclined", invitation.Email, null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<GroupDto> AcceptCoreAsync(Guid userId, GroupInvitation invitation, CancellationToken cancellationToken)
        {
            var user = await GetUserAsync(userId, cancellationToken);

            // Lời mời gửi cho email khác: coi như không tồn tại, không lộ nội dung
            if (invitation.Email != user.Email.ToLowerInvariant())
            {
                throw new NotFoundException("Không tìm thấy lời mời.");
            }

            if (invitation.Status != GroupInvitationStatuses.Pending)
            {
                throw new UserFriendlyException("Lời mời này đã được trả lời hoặc đã bị thu hồi.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (invitation.ExpiresAt <= now)
            {
                invitation.Status = GroupInvitationStatuses.Expired;
                invitation.RespondedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new UserFriendlyException("Lời mời đã hết hạn. Hãy nhờ Leader gửi lại.");
            }

            if (invitation.Group.Status == GroupStatuses.Archived)
            {
                throw new UserFriendlyException("Nhóm đã được lưu trữ nên chưa nhận thành viên mới.");
            }

            if (await _groups.GetMemberAsync(invitation.GroupId, userId, cancellationToken) is not null)
            {
                throw new UserFriendlyException("Bạn đã là thành viên của nhóm này.");
            }

            await _groups.AddMemberAsync(new GroupMember
            {
                Id = Guid.NewGuid(),
                GroupId = invitation.GroupId,
                UserId = userId,
                Role = invitation.Role,
                JoinedAt = now
            }, cancellationToken);
            invitation.Status = GroupInvitationStatuses.Accepted;
            invitation.RespondedAt = now;
            await _work.AddActivityAsync(GroupRules.Log(invitation.GroupId, userId, GroupEntityTypes.Member, userId, "MemberJoined", null, invitation.Role), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var group = invitation.Group;
            return new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                OwnerId = group.OwnerId,
                StartDate = group.StartDate,
                Deadline = group.Deadline,
                Status = group.Status,
                CreatedAt = group.CreatedAt,
                MyRole = invitation.Role,
                MemberCount = await _groups.CountMembersAsync(group.Id, cancellationToken)
            };
        }

        private async Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _users.GetByIdAsync(userId, cancellationToken);
            if (user is null || user.DeletedAt is not null)
            {
                throw new NotFoundException("Không tìm thấy người dùng.");
            }

            return user;
        }

        private static string NormalizeEmail(string? value)
        {
            var email = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (email.Length == 0 || email.Length > MaxEmailLength || !email.Contains('@') || email.Contains(' '))
            {
                throw new UserFriendlyException("Email không hợp lệ.");
            }

            return email;
        }

        // 32 byte ngẫu nhiên mã hóa base64 an toàn cho URL (giống refresh token)
        private static string GenerateToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static string Hash(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        }

        // Lời mời còn Pending nhưng đã quá hạn thì hiển thị là Expired (chưa ghi vào DB cho tới khi có người đụng tới)
        private static T ToDto<T>(GroupInvitation invitation, string groupName, string invitedByName, DateTime now) where T : GroupInvitationDto, new()
        {
            var status = invitation.Status == GroupInvitationStatuses.Pending && invitation.ExpiresAt <= now
                ? GroupInvitationStatuses.Expired
                : invitation.Status;

            return new T
            {
                Id = invitation.Id,
                GroupId = invitation.GroupId,
                GroupName = groupName,
                Email = invitation.Email,
                Role = invitation.Role,
                Status = status,
                InvitedById = invitation.InvitedById,
                InvitedByName = invitedByName,
                CreatedAt = invitation.CreatedAt,
                ExpiresAt = invitation.ExpiresAt,
                RespondedAt = invitation.RespondedAt
            };
        }
    }
}
