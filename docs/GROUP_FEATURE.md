# Nhánh Group (làm việc nhóm): thiết kế đợt 1

Trạng thái: **đợt 1 và đợt 2 (trừ AI gợi ý người nhận việc) đã có code và test** (toàn bộ 496 test đạt, gồm 9 test cho phần admin nhóm và 20 test cho lên lịch và tín hiệu). Trang thử trên trình duyệt: `wwwroot/group-test.html`, hướng dẫn từng bước và danh sách ca thử: [GROUP_TEST_GUIDE.md](GROUP_TEST_GUIDE.md). Kèm file SQL: [sql/005_group_tables.sql](sql/005_group_tables.sql) (đợt 1) và [sql/006_group_phase2.sql](sql/006_group_phase2.sql) (đợt 2), **bạn tự chạy**, chạy trước khi dùng bản backend mới. Hướng dẫn cho FE: [GROUP_FE.md](GROUP_FE.md). Business rule: BR-130 đến BR-157 trong [REPO_GUIDE.md](REPO_GUIDE.md).

## 1. Mục tiêu và nguyên tắc
- Nhiều người cùng làm một dự án, luật lấy theo Jira: vai trò, giao việc, trạng thái việc, bình luận, nhật ký hoạt động.
- **Nhánh cá nhân giữ nguyên**: không sửa bảng cũ, không sửa API cũ, không sửa service cũ. Nhóm là **một nhánh mới**, có bảng và API riêng (`/api/groups/...`).
- Hai nhánh chỉ chung `User` và `Role` (đăng nhập), thông báo (`Notification`, `UserPushSubscription`) và gói đăng ký.
- Đợt 1 **chưa có**: Start/Stop, lịch, AI, kiểm tra trùng lịch, đề xuất đổi giờ. Các phần đó thuộc đợt 2 và chỉ làm sau khi bạn xem đợt 1.

## 2. Vì sao không dùng lại Project, Task, SubTask
- Task cá nhân có Domain bắt buộc, loại Flexible/Fixed, status `Scheduled`, gắn với slot và phiên focus. Việc của nhóm không có những thứ đó.
- Mọi service cũ kiểm quyền bằng `Project.UserId`, nên thành viên vẫn phải đi qua service mới. Dùng chung bảng gần như không tiết kiệm code.
- Dùng chung còn làm công cụ admin đồng bộ status (`/api/admin/status-resync`) quét cả việc nhóm.
- Dùng lại nguyên trạng: `User`, `Role`, `Notification`, `UserPushSubscription`.

## 3. Bảng mới

Quy ước giống bảng cũ: `Id` Guid, thời gian UTC, trạng thái là chuỗi có CHECK.
Bảng chính tên **`WorkGroup`** (GROUP là từ khóa SQL); các cột tham chiếu vẫn là `GroupId`.

| Bảng | Thuộc tính | Ghi chú |
|---|---|---|
| **WorkGroup** | Id, Name, Description, OwnerId, StartDate, Deadline, Status (Active/Archived), CreatedAt | Đợt 1: một nhóm là một dự án chung. OwnerId là người tạo nhóm |
| **GroupMember** | Id, GroupId, UserId, Role (Leader/Member/Viewer), JoinedAt | Unique (GroupId, UserId) |
| **GroupInvitation** | Id, GroupId, Email, Role, TokenHash, InvitedById, Status (Pending/Accepted/Declined/Expired/Revoked), CreatedAt, ExpiresAt, RespondedAt | Chỉ lưu hash token như `RefreshToken`. Mỗi email chỉ một lời mời Pending trong một nhóm |
| **GroupMilestone** | Id, GroupId, Name, OrderIndex, DueDate, Status (Planned/InProgress/Delivered/InRevision/Approved), DeliveredAt, RevisionCount | Giống `Milestone`. Unique (GroupId, OrderIndex) |
| **GroupTask** | Id, GroupId, MilestoneId, AssigneeId, Title, Description, Priority (Low/Medium/High), Status (ToDo/InProgress/Done/Skipped), Deadline, EstimatedMinutes, CreatedAt, CompletedAt | Không có Scheduled. Người tạo ghi trong nhật ký, không có cột Reporter |
| **GroupSubTask** | Id, GroupTaskId, AssigneeId, Title, EstimatedMinutes, OrderIndex, Status (ToDo/InProgress/Done/Skipped), CompletedById, CompletedAt | Mỗi việc con một người được giao. Unique (GroupTaskId, OrderIndex) |
| **GroupTaskComment** | Id, GroupTaskId, AuthorId, Content (tối đa 4000), CreatedAt, EditedAt | |
| **GroupActivityLog** | Id, GroupId, ActorId, EntityType, EntityId, Action, OldValue, NewValue, CreatedAt | Ai đổi gì. EntityType: Group, Member, Invitation, Milestone, Task, SubTask, Comment |

Quan hệ: `WorkGroup` 1-n `GroupMember`, `GroupInvitation`, `GroupMilestone`, `GroupTask`, `GroupActivityLog`; `GroupMilestone` 1-n `GroupTask`; `GroupTask` 1-n `GroupSubTask`, `GroupTaskComment`. Xóa nhóm thì dữ liệu của nhóm xóa theo; xóa người được giao thì việc về chưa giao (`ON DELETE SET NULL`). Người dùng trong hệ thống chỉ bị xóa mềm nên điều này gần như không xảy ra.

Các luật mà một ràng buộc trong DB không diễn tả được (ví dụ "luôn còn ít nhất một Leader", "việc chỉ giao cho thành viên của nhóm", "giai đoạn của việc phải thuộc cùng nhóm") do **code** kiểm tra.

## 4. Vai trò và quyền

| Việc | Leader | Member | Viewer |
|---|---|---|---|
| Xem nhóm, giai đoạn, việc, bình luận, tiến độ | có | có | có |
| Sửa thông tin nhóm, lưu trữ nhóm | có | | |
| Mời thành viên, đổi vai trò, xóa thành viên | có | | |
| Rời nhóm | có (trừ Leader cuối) | có | có |
| Tạo, sửa, xóa giai đoạn | có | | |
| Chuyển giai đoạn sang Delivered hoặc Approved | có | | |
| Tạo, sửa, xóa việc và việc con, giao việc | có | | |
| Đổi status việc và việc con | mọi việc | **chỉ việc được giao cho mình** | |
| Bình luận (sửa, xóa bình luận của chính mình) | có | có | |
| Xem nhật ký hoạt động | có | có | |

Người không thuộc nhóm nhận **404** (không lộ nhóm có tồn tại). Người thuộc nhóm nhưng thiếu quyền nhận **403**.

## 5. Luật nghiệp vụ
1. **Luôn còn ít nhất một Leader.** Leader cuối không thể rời, bị hạ vai trò hay bị xóa (giống luật Admin cuối).
2. **Mời qua email:** lời mời có hạn (đề xuất 7 ngày); người nhận đăng nhập Google rồi chấp nhận. Email phải khớp tài khoản đang đăng nhập. Một người chỉ có một vai trò trong một nhóm; đã là thành viên thì không mời lại.
3. **Giao việc:** chỉ giao cho thành viên có vai trò Leader hoặc Member (không giao cho Viewer). Việc chưa giao có `AssigneeId` rỗng. Việc con có thể giao cho người khác với việc cha, nhờ vậy nhiều người làm chung một việc.
4. **Xóa thành viên:** việc và việc con đang giao cho họ về chưa giao. Bình luận và nhật ký của họ giữ lại.
5. **Status** dùng bảng chuyển riêng của nhóm (`GroupStatusRules`, theo mẫu `StatusRules`); đặt lại đúng trạng thái đang có luôn hợp lệ:
   - Việc con: ToDo → InProgress, Done, Skipped; InProgress → ToDo, Done, Skipped; Done → ToDo, InProgress; Skipped → ToDo.
   - Việc: ToDo → InProgress, Done, Skipped; InProgress → ToDo, Done, Skipped; Done → ToDo, InProgress; Skipped → ToDo, InProgress.
   - Giai đoạn: giống `Milestone` hiện có (Planned → InProgress → Delivered → InRevision/Approved).
6. **Status của việc tự suy từ việc con** (giống nhánh cá nhân, nhưng không có slot và phiên làm việc):
   - Có việc con InProgress hoặc Done → việc là **InProgress**.
   - Mọi việc con đã xử lý (Done hoặc Skipped) và ít nhất một Done → việc tự **Done** (`CompletedAt` được ghi).
   - Việc đã Done mà có việc con mới hoặc được mở lại (ToDo hoặc InProgress) → việc được mở lại.
   - Việc **Skipped** là trạng thái chốt do người dùng đặt, hệ thống không tự đổi.
   - Việc **không có việc con** thì người được giao hoặc Leader đổi tay.
   - Đóng tay việc (Done hoặc Skipped): các việc con còn ToDo hoặc InProgress chuyển Skipped.
7. **Giai đoạn:** Planned tự lên InProgress khi có việc bắt đầu. Chuyển Delivered khi còn việc chưa xong thì trả **409** kèm danh sách việc đó và cho xác nhận (`confirmOpenTasks`), giống nhánh cá nhân. Việc mới không được thêm vào giai đoạn đã Delivered hoặc Approved (phải chuyển InRevision trước).
8. **Nhật ký hoạt động:** ghi tạo, sửa, đổi status, giao việc, thêm hoặc xóa thành viên, đổi vai trò, mời, bình luận. Mỗi dòng có người làm, đối tượng, hành động, giá trị cũ và mới.
9. **Thông báo:** được giao việc, được mời vào nhóm, được nhắc tên (@mention trong bình luận), việc mình được giao bị đổi status bởi người khác. Qua `NotificationService` (SignalR và Web Push) và hộp thư `Notification`.
10. **Gói và hạn mức:** đợt 1 chưa giới hạn số nhóm hay số thành viên. Về sau tính theo gói của Leader tạo nhóm.

## 6. Báo cáo tiến độ nhóm (chỉ là truy vấn, không thêm bảng)
- Số việc theo từng status, số việc còn lại, % hoàn thành (việc Done trên tổng số việc, bỏ việc Skipped).
- Theo từng thành viên: số việc và việc con được giao, số đã xong.
- Theo từng giai đoạn: số việc theo status và ngày đến hạn.

## 7. API đề xuất (tiền tố `/api/groups`, đều cần đăng nhập)

| Nhóm API | Đường dẫn |
|---|---|
| Nhóm | `GET /api/groups` (nhóm của tôi), `POST /api/groups`, `GET/PUT /api/groups/{id}`, `POST /api/groups/{id}/archive`, `POST /api/groups/{id}/restore` |
| Thành viên | `GET /api/groups/{id}/members`, `PUT /api/groups/{id}/members/{userId}` (đổi vai trò), `DELETE /api/groups/{id}/members/{userId}`, `POST /api/groups/{id}/leave` |
| Lời mời | `POST /api/groups/{id}/invitations`, `GET /api/groups/{id}/invitations`, `DELETE /api/groups/{id}/invitations/{invId}` (thu hồi), `GET /api/group-invitations` (lời mời gửi cho tôi), `POST /api/group-invitations/{token}/accept`, `POST /api/group-invitations/{token}/decline` |
| Giai đoạn | `GET/POST /api/groups/{id}/milestones`, `GET/PUT/DELETE /api/group-milestones/{id}` |
| Việc | `GET/POST /api/groups/{id}/tasks`, `GET/PUT/DELETE /api/group-tasks/{id}`, `PUT /api/group-tasks/{id}/status`, `PUT /api/group-tasks/{id}/assignee` |
| Việc con | `GET/POST /api/group-tasks/{id}/subtasks`, `GET/PUT/DELETE /api/group-subtasks/{id}`, `PUT /api/group-subtasks/{id}/status`, `PUT /api/group-subtasks/{id}/assignee` |
| Bình luận | `GET/POST /api/group-tasks/{id}/comments`, `PUT/DELETE /api/group-comments/{id}` |
| Nhật ký | `GET /api/groups/{id}/activity` |
| Tiến độ | `GET /api/groups/{id}/progress` |
| Việc của tôi | `GET /api/my-group-tasks` (việc và việc con được giao cho tôi, qua mọi nhóm) |

## 8. Cấu trúc code (đều thêm mới, không sửa file cũ trừ khi nói rõ)
- **Domain:** entity và enum mới trong `METANOIA.Domain/Entities` và `Enums`.
- **Application:** `IGroupAccessService` (vai trò của user trong nhóm, một chỗ kiểm quyền duy nhất), `IGroupService`, `IGroupMemberService`, `IGroupInvitationService`, `IGroupMilestoneService`, `IGroupTaskService`, `IGroupSubTaskService`, `IGroupCommentService`, `IGroupProgressService`; repository interface và DTO tương ứng.
- **Infrastructure:** service, repository, `GroupStatusRules`, `GroupProgressRules` (suy status việc từ việc con). Đăng ký trong `Program.cs`. Dùng lại `UnitOfWork`, `NotificationService`.
- **Web:** controller mới trong `METANOIA/Controllers`. Không sửa controller cũ.
- **Sửa file cũ duy nhất:** `AppDbContext` (thêm `DbSet`) và `Program.cs` (đăng ký DI).

## 9. Thông báo: dùng chung bảng Notification (đã chốt)
Thông báo nhóm dùng lại bảng `Notification` với `Type` mới (`GroupAssigned`, `GroupMention`, `GroupStatusChanged`, `GroupInvited`), nên người dùng có **một hộp thư chung** (`GET /api/notifications`, SignalR, Web Push). Đây là chỗ duy nhất đợt 1 sửa một bảng đang có, gồm hai thay đổi trong file SQL:
- **Bước 3 (bắt buộc): thêm 2 cột nullable `GroupId`, `GroupTaskId`** (khóa ngoại `ON DELETE SET NULL`) để thông báo mang liên kết tới nhóm và việc, FE bấm mở thẳng. Thông báo cũ và của nhánh cá nhân để `NULL`. Bắt buộc vì backend mới đọc hai cột này ở mọi truy vấn `Notification`.
- **Bước 4: nới CHECK `Notification_Type_check`** (code ghi chú rằng nó chỉ cho `SlotReminder`, `MissedSlot`, `DeadlineRisk`) thêm 4 giá trị nhóm. Thông báo gửi trong scope riêng và bắt lỗi: chưa nới CHECK thì thao tác nhóm vẫn thành công, chỉ thiếu thông báo (lỗi ghi log).

Liên kết: `GroupInvited` chỉ có `groupId`; `GroupAssigned`, `GroupStatusChanged`, `GroupMention` có cả `groupTaskId` (với việc con là việc cha của nó). Web Push mang cùng hai trường; sự kiện SignalR `ReceiveNotification` giữ nguyên chữ ký (FE lấy chi tiết từ hộp thư theo id).

## 10. Kiểm thử dự kiến
- Quyền theo vai trò (Leader, Member, Viewer); người ngoài nhóm nhận 404.
- Luật Leader cuối: không rời, không hạ vai trò, không xóa.
- Mời: hết hạn, email không khớp, mời lại người đã là thành viên.
- Giao việc cho người ngoài nhóm hoặc cho Viewer bị từ chối; xóa thành viên làm việc về chưa giao.
- Status: việc con Done hết thì việc tự Done; thêm việc con mới thì việc Done được mở lại; Skipped không tự đổi; Member chỉ đổi được việc của mình.
- Giai đoạn Delivered khi còn việc mở trả 409, xác nhận thì cho qua.
- Toàn bộ test hiện có (422) vẫn phải đạt như cũ.

## 11. Đợt 2 (đã làm, trừ AI)
SQL: [sql/006_group_phase2.sql](sql/006_group_phase2.sql). Hướng dẫn FE: [GROUP_FE.md](GROUP_FE.md) mục 7 đến 10. Business rule: BR-144 đến BR-154 trong [REPO_GUIDE.md](REPO_GUIDE.md).

- **`GroupFocusSession`** (bảng riêng, không đụng `FocusSession` của nhánh cá nhân): Start/Stop trên việc hoặc việc con được giao, giờ làm ghi theo từng người. Báo cáo giờ thực tế theo người và theo việc con (`GET /api/group-tasks/{id}/time`) và trong tiến độ nhóm. Một người một phiên chạy một lúc (có chỉ mục duy nhất trong DB); việc nhóm kiểm tra thêm phiên cá nhân đang chạy bằng cách đọc `IFocusSessionRepository`. Phiên dài quá 12 giờ bị cắt khi dừng.
- **Giờ cố định và kiểm tra lịch khi giao việc**: cột `FixedStart`, `FixedEnd` trên `GroupTask`. Khi giao hoặc đổi giờ, so với slot cá nhân đang hoạt động, event Google đã lưu và việc nhóm khác có giờ cố định của người nhận; việc có deadline và ước lượng thì cảnh báo khối lượng tuần. Cảnh báo là `409` `GROUP_ASSIGNEE_BUSY` để Leader xác nhận (`confirmConflicts`), không chặn cứng. Chỉ đọc dữ liệu nhánh cá nhân (`ICalendarRepository`); Leader chỉ thấy bận hoặc rảnh, không có tên dự án hay tiêu đề lịch cá nhân.
- **`GroupTaskChangeRequest`**: người được giao đề xuất đổi giờ cố định hoặc deadline, Leader duyệt (áp dụng, có kiểm tra lịch) hoặc từ chối (bắt buộc lý do), người đề xuất rút lại được. Thông báo `GroupChangeRequested` và `GroupChangeDecided` qua `Notification` kèm liên kết.
- **Chưa làm: AI gợi ý người nhận việc** (chỉ Leader dùng, lượt gọi tính vào gói của Leader). Cần dịch vụ AI (Python) có API gợi ý người nhận việc; backend chưa có gì cho phần này.

Điểm hở đã biết: bắt đầu việc **cá nhân** khi đang chạy việc nhóm chưa bị chặn (chặn thì phải sửa `FocusSessionService` của nhánh cá nhân, trái nguyên tắc không đụng nhánh cá nhân). Việc nhóm thì đã chặn khi đang chạy việc cá nhân.

## 12. Điểm thiết kế đã áp dụng trong đợt 1
1. Tên bảng chính `WorkGroup` (thay vì `Group`).
2. Việc con có thêm trạng thái `InProgress` (nhánh cá nhân chỉ có ToDo, Done, Skipped) để Member báo "đang làm" khi chưa có Start/Stop.
3. Thông báo dùng chung bảng `Notification` (mục 9).
4. Hạn lời mời 7 ngày. Chưa có dịch vụ gửi email: Leader nhận `acceptToken` khi mời (chỉ trả một lần) và người được mời thấy lời mời gửi tới email của mình trong `GET /api/group-invitations`.
5. Mỗi nhóm là một dự án chung; nhiều dự án trong một nhóm để sau.
6. Hạ một thành viên xuống Viewer cũng bỏ giao việc của họ (Viewer không nhận việc).
7. Bình luận: Leader và Member viết; tác giả sửa; tác giả hoặc Leader xóa. `@email` của thành viên trong nội dung gửi thông báo nhắc tên.

## 13. Admin quản trị nhóm (đã làm: xem, thống kê, lưu trữ, khôi phục)

API (chỉ role Admin) dưới `/api/admin/groups`: `GET` (danh sách có lọc và phân trang), `GET stats`, `GET {id}`, `GET {id}/activity`, `POST {id}/archive`, `POST {id}/restore`. Hai thao tác ghi bắt buộc header `X-Admin-Reason`.

- Không đổi database. Bảng `WorkGroup` được thêm vào danh sách bảng mà `AdminAuditSaveChangesInterceptor` ghi, nên mỗi lần Admin đổi trạng thái có một dòng `AdminActionLog` (`WorkGroup.Update`) kèm lý do và giá trị cũ/mới.
- Nhật ký của nhóm có thêm dòng `AdminArchived` hoặc `AdminRestored` (người thực hiện là Admin, giá trị mới ghi trạng thái và lý do). Mọi Leader nhận thông báo `GroupStatusChanged` có liên kết tới nhóm.
- Quyền riêng tư: Admin chỉ thấy thông tin cấp nhóm (chủ nhóm, thành viên, báo cáo tiến độ, nhật ký). Không có nội dung bình luận, tiêu đề việc hay lịch cá nhân.
- **Chưa làm:** đổi Leader bằng quyền Admin, xóa hẳn nhóm.
- Mã: `AdminGroupsController` (trong `AdminController.cs`), `AdminGroupService`, `AdminGroupRepository`, `AdminGroupDto.cs`. Test: `AdminGroupTests.cs` (9 test).

## 14. Đợt 3: lên lịch cho việc nhóm và tín hiệu tiến độ (đã làm)

SQL: [sql/009_group_schedule_slot.sql](sql/009_group_schedule_slot.sql) (chạy trước backend mới). Hướng dẫn FE: [GROUP_FE.md](GROUP_FE.md) mục 11 và 12.

**Cách làm: Leader giao việc, member tự xếp giờ vào lịch của mình** (không làm Leader xếp hộ, không AI gợi ý, không tự nhận việc, chưa có "trả việc").

- **Dùng chung `ScheduleSlot`.** Thêm `UserId` (chỉ slot nhóm, **cho phép NULL** vì dịch vụ AI Python ghi slot cá nhân thẳng vào DB và không gửi `UserId`), `GroupTaskId`, `GroupSubTaskId`; `TaskId` cho phép NULL. CHECK trong DB: đúng một trong `TaskId` hoặc `GroupTaskId`; việc con đi đúng loại; slot nhóm bắt buộc có `UserId`. `GroupFocusSession` thêm `ScheduleSlotId`.
- **Chủ slot:** slot cá nhân vẫn suy qua `Task.Project.UserId`; slot nhóm lưu `UserId`. Một hàm dùng chung `ScheduleSlotQueries.OwnedBy(userId)` gom cả hai đường và thay cho các truy vấn cũ (repository slot, lịch, lịch sử đổi giờ, kiểm tra chồng giờ, nhắc việc).
- **Luật tạo slot:** chỉ người được giao, việc có việc con phải chọn việc con, việc chưa đóng, nhóm đang hoạt động, không chồng giờ (`409 SLOT_OVERLAP`, tính cả slot cá nhân). Slot nhóm không đổi status việc nhóm.
- **Nhắc việc và Google:** job nhắc gửi cho người xếp slot; bỏ qua khi nhóm lưu trữ hoặc việc không còn giao cho người đó; slot nhóm không có AutoTimeout. Sự kiện Google có tiêu đề việc và mô tả `Nhóm: ...`.
- **Bấm giờ theo slot** (tùy chọn): Start gắn slot sang `InProgress`; Stop đổi slot theo lý do dừng.
- **Giao lại việc, hạ xuống Viewer, xóa hoặc rời nhóm (phương án A):** `GroupSlotCleanup` dừng phiên đang chạy của người cũ (lý do `Other`, giờ vẫn tính), hủy slot chưa bắt đầu (qua `Due` nếu đang chạy), ghi `ScheduleChangeLog`, hủy job nhắc, gửi thông báo cho người cũ. Slot đã xong hoặc đã lỡ giữ làm lịch sử. Người mới tự xếp lịch.
- **Tín hiệu (không đổi DB):** `GroupProgressService` tính quá hạn, chưa có lịch, nguy cơ trễ (deadline trong 3 ngày và phút còn lại lớn hơn phút đã xếp), đứng yên (3 ngày không hoạt động). Ngưỡng là hằng số trong `GroupRules`. Admin chỉ thấy số đếm.
- **Chưa làm:** AI gợi ý giờ cho việc nhóm, tự nhận việc, "trả việc" có Leader xác nhận (đã thiết kế, cần bảng `GroupTaskReleaseRequest`), chặn bắt đầu việc cá nhân khi đang chạy việc nhóm.
- Mã mới: `GroupScheduleService`, `GroupSlotCleanup`, `SlotMapper`, `SlotOverlap`, `ScheduleSlotQueries`, `GroupScheduleDto.cs`. Test: `GroupScheduleTests.cs` (20 test).
