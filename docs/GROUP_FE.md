# Nhánh Group: hướng dẫn cho FE (đợt 1)

Thiết kế và luật: [GROUP_FEATURE.md](GROUP_FEATURE.md), business rule BR-130 đến BR-143 trong [REPO_GUIDE.md](REPO_GUIDE.md). Nhánh cá nhân (`/api/projects`, `/api/tasks`...) **không đổi**. Mọi API cần đăng nhập (`Authorization: Bearer ...`). Đợt 2 (bấm giờ, kiểm tra lịch khi giao việc, đề xuất đổi giờ) ở các mục 7 đến 10 bên dưới; chưa có AI cho việc của nhóm.

## 1. Mã lỗi cần xử lý

| HTTP | Khi nào | Body |
|---|---|---|
| `400` | Dữ liệu sai, vi phạm luật (giao việc cho Viewer, chuyển status sai, nhóm đã lưu trữ...) | `{ "message": "..." }` |
| `403` | Đã trong nhóm nhưng vai trò không đủ quyền | `{ "message": "...", "code": "GROUP_FORBIDDEN" }` |
| `404` | Không tìm thấy, **hoặc người gọi không thuộc nhóm** (không phân biệt) | `{ "message": "..." }` |
| `409` | Chuyển giai đoạn sang Delivered khi còn việc chưa xong | `{ "code": "MILESTONE_HAS_OPEN_TASKS", "details": { "openTaskCount", "openSubTaskCount", "tasks": [...] } }` |

Ẩn nút theo vai trò để người dùng khỏi thấy `403`: lấy `myRole` từ `GET /api/groups/{id}`.

| Việc | Leader | Member | Viewer |
|---|---|---|---|
| Xem nhóm, việc, việc con, bình luận, tiến độ | có | có | có |
| Sửa, lưu trữ nhóm; mời, đổi vai trò, xóa thành viên | có | | |
| Giai đoạn: tạo, sửa, xóa | có | | |
| Việc và việc con: tạo, sửa, xóa, **giao việc** | có | | |
| Đổi status | mọi việc | việc (không có việc con) hoặc việc con **được giao cho mình** | |
| Bình luận | có | có | |
| Nhật ký hoạt động | có | có | |
| Rời nhóm | có (trừ Leader cuối) | có | có |

## 2. Nhóm và thành viên

| API | Ghi chú |
|---|---|
| `GET /api/groups?includeArchived=false` | Nhóm của tôi: `id, name, description, ownerId, startDate, deadline, status, myRole, memberCount` |
| `POST /api/groups` | Body `{ name, description?, startDate?, deadline? }`. Người tạo thành Leader. `201` |
| `GET /api/groups/{id}`, `PUT /api/groups/{id}` | Sửa: Leader |
| `POST /api/groups/{id}/archive`, `/restore` | Nhóm lưu trữ chỉ xem được (mọi thao tác ghi trả `400`) |
| `GET /api/groups/{id}/members` | `userId, fullName, email, role, joinedAt` |
| `PUT /api/groups/{id}/members/{userId}` | Body `{ "role": "Leader" \| "Member" \| "Viewer" }`. Hạ Leader cuối: `400` |
| `DELETE /api/groups/{id}/members/{userId}` | Leader xóa. Việc đang giao cho người đó về chưa giao. `204` |
| `POST /api/groups/{id}/leave` | Tự rời. Leader cuối không rời được: `400`. `204` |

## 3. Lời mời

Chưa có email: Leader nhận `acceptToken` (chỉ trả **một lần**) để gửi liên kết; người được mời cũng thấy lời mời gửi tới email của mình trong danh sách.

| API | Ghi chú |
|---|---|
| `POST /api/groups/{id}/invitations` | Leader. Body `{ email, role? }` (mặc định `Member`). Trả `{ id, email, role, status, expiresAt, acceptToken, ... }`. Hạn 7 ngày. `400` nếu đã là thành viên hoặc đã có lời mời chờ |
| `GET /api/groups/{id}/invitations` | Leader. `status` hết hạn hiện là `Expired` |
| `DELETE /api/groups/{id}/invitations/{invId}` | Thu hồi. `204` |
| `GET /api/group-invitations` | Lời mời đang chờ gửi tới **tôi** (theo email tài khoản) |
| `POST /api/group-invitations/{id}/accept` | Trả `GroupDto` (có `myRole`). Email không khớp: `404` |
| `POST /api/group-invitations/accept-by-token` | Body `{ "token": "..." }`. Cùng luật như trên |
| `POST /api/group-invitations/{id}/decline` | `204` |

## 4. Giai đoạn, việc, việc con

Status việc và việc con: `ToDo, InProgress, Done, Skipped`. Giai đoạn: `Planned, InProgress, Delivered, InRevision, Approved`. Độ ưu tiên: `Low, Medium, High`.

| API | Ghi chú |
|---|---|
| `GET/POST /api/groups/{id}/milestones`, `GET/PUT/DELETE /api/group-milestones/{id}` | Body `{ name, orderIndex, dueDate?, status?, confirmOpenTasks? }`. `orderIndex` duy nhất trong nhóm. Delivered khi còn việc mở: `409`, gửi lại kèm `confirmOpenTasks: true` |
| `GET /api/groups/{id}/tasks?status=&assigneeId=&milestoneId=` | Trả kèm `subTaskCount`, `subTaskDoneCount`, `assigneeName` |
| `POST /api/groups/{id}/tasks` | Leader. Body `{ title, description?, priority?, milestoneId?, assigneeId?, deadline?, estimatedMinutes? }` |
| `GET/PUT/DELETE /api/group-tasks/{id}` | Sửa và xóa: Leader. PUT **không** đổi status và người được giao |
| `PUT /api/group-tasks/{id}/status` | Body `{ "status": "..." }`. Việc có việc con thì status **tự suy** từ việc con, Member không đổi tay được (`403`) |
| `PUT /api/group-tasks/{id}/assignee` | Leader. Body `{ "assigneeId": "guid" \| null }`. Chỉ giao cho Leader hoặc Member của nhóm |
| `GET/POST /api/group-tasks/{id}/subtasks`, `GET/PUT/DELETE /api/group-subtasks/{id}` | Body `{ title, estimatedMinutes?, orderIndex?, assigneeId? }`; bỏ `orderIndex` thì xếp cuối |
| `PUT /api/group-subtasks/{id}/status` | Sau khi đổi, **gọi lại việc cha** (hoặc đọc `GET /api/group-tasks/{id}`) vì status việc cha có thể đổi theo |
| `PUT /api/group-subtasks/{id}/assignee` | Leader |

Status tự đổi theo việc con: có việc con InProgress hoặc Done thì việc **InProgress**; mọi việc con đã xử lý (Done hoặc Skipped) và có ít nhất một Done thì việc tự **Done**; việc đã Done mà có việc con mới hoặc mở lại thì được mở lại; việc **Skipped** không tự đổi. Đóng việc bằng tay (Done, Skipped) thì việc con còn dở thành Skipped. Giai đoạn Planned tự lên InProgress khi có việc bắt đầu.

## 5. Bình luận, nhật ký, tiến độ, việc của tôi

| API | Ghi chú |
|---|---|
| `GET/POST /api/group-tasks/{id}/comments` | Body `{ "content": "..." }` (tối đa 4000 ký tự). Nhắc tên: viết `@email` của thành viên, người đó nhận thông báo `GroupMention` |
| `PUT /api/group-comments/{id}` | Chỉ tác giả |
| `DELETE /api/group-comments/{id}` | Tác giả hoặc Leader |
| `GET /api/groups/{id}/activity?skip=0&take=50` | Mới nhất trước: `actorName, entityType, action, oldValue, newValue, createdAt`. Leader và Member |
| `GET /api/groups/{id}/progress` | `totalTasks, tasksByStatus{ToDo,InProgress,Done,Skipped}, remainingTasks, unassignedTasks, completedPercent, members[...], milestones[...]` |
| `GET /api/my-group-tasks` | `{ tasks: [...], subTasks: [...] }` được giao cho tôi, qua mọi nhóm đang hoạt động (không gồm nhóm đã lưu trữ) |

`action` trong nhật ký: `Created, Updated, Deleted, StatusChanged, Assigned, RoleChanged, MemberJoined, Removed, Left, Invited, InvitationRevoked, InvitationDeclined, Archived, Restored`.

## 6. Thông báo

Dùng chung hộp thư `GET /api/notifications` và SignalR/Web Push như nhánh cá nhân. `type` của thông báo nhóm:

| type | Gửi cho | Khi |
|---|---|---|
| `GroupInvited` | người được mời (nếu đã có tài khoản) | Leader mời |
| `GroupAssigned` | người được giao | Việc hoặc việc con được giao cho họ |
| `GroupStatusChanged` | người được giao | Người **khác** đổi status việc hoặc việc con của họ |
| `GroupMention` | người được nhắc | Bình luận có `@email` của họ |

Mỗi thông báo nhóm có **liên kết** để bấm mở thẳng: `GET /api/notifications` (và `NotificationDto`) trả thêm `groupId` và `groupTaskId`.

| type | `groupId` | `groupTaskId` | FE mở |
|---|---|---|---|
| `GroupInvited` | nhóm được mời | rỗng | danh sách lời mời, hoặc trang nhóm sau khi chấp nhận |
| `GroupAssigned`, `GroupStatusChanged`, `GroupMention` | nhóm | việc (với việc con là **việc cha** của nó) | chi tiết việc `GET /api/group-tasks/{groupTaskId}` |

Thông báo nhóm đẩy qua Web Push cũng mang `groupId` và `groupTaskId` trong payload. Thông báo cũ và thông báo của nhánh cá nhân có `groupId` và `groupTaskId` là `null` (và vẫn dùng `scheduleSlotId`). Sự kiện SignalR `ReceiveNotification(message, type, notificationId)` **không đổi**: FE nhận id rồi lấy chi tiết từ hộp thư nếu cần liên kết. Xóa nhóm hoặc việc thì thông báo vẫn còn nhưng mất liên kết (`null`). Người tự thao tác không nhận thông báo về chính mình.

**SQL:** cần chạy bước 3 (thêm 2 cột, **bắt buộc** trước khi chạy backend mới vì mọi truy vấn thông báo đọc 2 cột này) và bước 4 (nới CHECK `Notification_Type_check`) của `docs/sql/005_group_tables.sql`. Chưa chạy bước 4 thì thao tác nhóm vẫn thành công, chỉ thiếu thông báo nhóm.

---

# Đợt 2

SQL: `docs/sql/006_group_phase2.sql`. **Bước 1 (thêm `FixedStart`, `FixedEnd` vào `GroupTask`) bắt buộc chạy trước khi chạy backend đợt 2**, vì backend đọc hai cột này ở mọi truy vấn việc của nhóm.

## 7. Bấm giờ làm việc (Start/Stop) của nhóm

Tách khỏi `/api/focus` của nhánh cá nhân. Phiên ghi giờ của **một người** trên một việc hoặc một việc con, nên báo cáo được giờ của từng người.

| API | Ghi chú |
|---|---|
| `POST /api/group-tasks/{taskId}/focus/start` | Body `{ "subTaskId": "guid" \| null, "scheduleSlotId": "guid" \| null }` (`scheduleSlotId` xem mục 11). `201` trả `{ sessionId, startedAt, taskStatus, subTaskStatus, scheduleSlotId }`. Việc có việc con thì **bắt buộc** gửi `subTaskId` (`400` nếu thiếu) |
| `POST /api/group-focus/{sessionId}/stop` | Body `{ "stopReason": "Completed" \| "Interrupted" \| "Blocked" \| "Other", "note": "..." }`. `note` bắt buộc với `Blocked` và `Other`. Trả `{ sessionMinutes, myTaskMinutes, taskTotalMinutes, taskStatus, subTaskStatus, wasCapped }` |
| `GET /api/group-focus/current` | Phiên đang chạy của tôi (kèm `groupName`, `taskTitle`, `subTaskTitle`, `elapsedSeconds`, `serverTime`, `estimatedMinutes`); `204` nếu không có |
| `GET /api/group-tasks/{taskId}/time` | Giờ làm thực tế: `totalMinutes`, `estimatedMinutes`, `byUser[]`, `bySubTask[]`, `sessions[]`. Mọi thành viên (kể cả Viewer) xem được |

Luật:
- **Chỉ người được giao** bấm được: việc con của mình, việc con chưa giao nếu mình được giao việc cha, hoặc việc (không có việc con) giao cho mình. Người khác `403`, người ngoài nhóm `404`.
- **Một người một phiên chạy một lúc**, tính cả phiên cá nhân. Đang có phiên: `409` `FOCUS_SESSION_CONFLICT`, `details.source` là `Group` (kèm `session`) hoặc `Personal` (kèm `sessionId`, `taskId`). Việc nhóm kiểm tra phiên cá nhân; chiều ngược lại (bắt đầu việc cá nhân khi đang chạy việc nhóm) **chưa được chặn**, FE nên hiển thị phiên nhóm đang chạy trước khi cho bấm Start cá nhân.
- **Status theo bấm giờ:** Start làm việc con `ToDo` thành `InProgress` (việc không có việc con: chính việc), việc cha tự suy lại, giai đoạn `Planned` lên `InProgress`. Stop `Completed` làm việc con (hoặc việc không có việc con) thành `Done`; việc cha tự `Done` khi mọi việc con đã xong. Stop `Interrupted`, `Blocked`, `Other` không đổi status (vẫn `InProgress`).
- Việc đã `Done`/`Skipped` hoặc việc con đã đóng thì `400`, phải mở lại trước.
- **Quên bấm Stop:** phiên dài hơn **12 giờ** bị cắt còn 12 giờ khi dừng (`wasCapped = true`, `note` được ghi chú).
- Nhóm đã lưu trữ: không Start được (`400`); phiên đang chạy vẫn Stop được, giờ vẫn ghi nhưng status không đổi.
- Rời nhóm hoặc bị xóa khỏi nhóm: phiên đang chạy của người đó trong nhóm tự dừng.
- `GET /api/groups/{id}/progress` có thêm `totalTrackedMinutes`, `totalEstimatedMinutes` và `members[].trackedMinutes` (phút, các phiên đã kết thúc).

## 8. Giờ cố định và kiểm tra lịch khi giao việc

Việc có thể có **giờ cố định** (họp, hẹn): `fixedStart` và `fixedEnd` trong `POST/PUT` việc (gửi cả hai hoặc không gửi cái nào, giờ kết thúc sau giờ bắt đầu); `GroupTaskDto` và `MyGroupTaskDto` trả lại hai trường này.

Khi **giao việc** (tạo việc có `assigneeId`, `PUT .../assignee`, hoặc sửa giờ, deadline, ước lượng của việc đã giao), hệ thống cảnh báo nếu người nhận:
- **Bận:** việc có giờ cố định trùng slot cá nhân đang hoạt động (`PersonalSchedule`), event Google đã lưu không phải cả ngày (`GoogleCalendar`) hoặc việc nhóm khác có giờ cố định (`GroupTask`). Chạm đầu mút không tính là trùng.
- **Quá tải:** việc có `deadline` và `estimatedMinutes`, tổng ước lượng của các việc nhóm chưa xong của người đó có deadline trong 7 ngày kết thúc ở deadline này vượt số phút làm việc một tuần (giờ làm theo cài đặt của người nhận, 5 hoặc 7 ngày).

Cảnh báo là `409` `GROUP_ASSIGNEE_BUSY`, **không chặn cứng**: hiện hộp thoại, Leader đồng ý thì gửi lại kèm `"confirmConflicts": true`.

```json
{ "code": "GROUP_ASSIGNEE_BUSY", "message": "...", "details": {
  "assigneeId": "guid", "isBusy": true,
  "conflicts": [ { "start": "...", "end": "...", "source": "PersonalSchedule" } ],
  "workload": { "assignedMinutes": 3500, "capacityMinutes": 3000, "isOverloaded": true } } }
```

**Xem trước (Leader):** `GET /api/group-tasks/{taskId}/assignee-check?assigneeId=...` trả `{ assigneeId, isBusy, conflicts[], workload, needsConfirmation }` để hiện dấu cảnh báo bên cạnh tên người trước khi giao. Chỉ có **bận hay rảnh**: khoảng thời gian và nguồn, **không** có tên dự án hay tiêu đề lịch cá nhân. Member gọi API này nhận `403`.

## 9. Đề xuất đổi giờ

Member không tự sửa giờ của việc đã giao. Người được giao việc (hoặc một việc con của việc) đề xuất, Leader duyệt hoặc từ chối.

| API | Ghi chú |
|---|---|
| `POST /api/group-tasks/{taskId}/change-requests` | Body `{ newFixedStart?, newFixedEnd?, newDeadline?, reason }`. Giờ cố định mới gửi cả hai đầu; phải có ít nhất giờ cố định mới hoặc deadline mới và khác giá trị hiện tại; `reason` 1 đến 1000 ký tự. Mỗi người một đề xuất `Pending` trên một việc (`400` nếu đã có). `201`. Người không được giao: `403` |
| `GET /api/groups/{groupId}/change-requests?status=Pending` | Leader thấy mọi đề xuất, Member chỉ thấy của mình, Viewer `403` |
| `GET /api/group-tasks/{taskId}/change-requests` | Như trên, theo việc |
| `POST /api/group-change-requests/{id}/approve` | Leader. Body `{ note?, confirmConflicts? }`. Áp dụng giá trị mới vào việc; giờ mới trùng lịch người nhận thì `409` `GROUP_ASSIGNEE_BUSY`, gửi lại kèm `confirmConflicts: true` |
| `POST /api/group-change-requests/{id}/reject` | Leader. Body `{ "note": "lý do" }` bắt buộc |
| `POST /api/group-change-requests/{id}/cancel` | Chỉ người đề xuất, khi còn `Pending` |

`GroupChangeRequestDto`: `id, groupTaskId, taskTitle, requestedById, requestedByName, currentFixedStart/End/Deadline` (giá trị hiện tại của việc), `newFixedStart/End/Deadline, reason, status` (`Pending, Approved, Rejected, Cancelled`)`, decidedById, decidedByName, decidedAt, decisionNote, createdAt`. Đề xuất đã xử lý hoặc đã rút thì duyệt, từ chối, rút lại đều `400`.

## 10. Thông báo mới của đợt 2

| type | Gửi cho | Khi | `groupId` | `groupTaskId` |
|---|---|---|---|---|
| `GroupChangeRequested` | mọi Leader (trừ người đề xuất) | Có đề xuất đổi giờ | nhóm | việc |
| `GroupChangeDecided` | người đề xuất | Leader duyệt hoặc từ chối (nội dung có ghi chú) | nhóm | việc |

Cột `Notification.Type` dài 20 ký tự: `GroupChangeRequested` đúng 20 ký tự. Cần chạy bước 4 của `006_group_phase2.sql` để nới CHECK; chưa chạy thì đề xuất vẫn hoạt động, chỉ thiếu thông báo. Nhật ký hoạt động có thêm `entityType` `FocusSession` (`Started`, `Stopped`) và `ChangeRequest` (`ChangeRequested`, `ChangeApproved`, `ChangeRejected`, `ChangeCancelled`).

---

# Đợt 3: lên lịch cho việc nhóm và tín hiệu tiến độ

SQL: `docs/sql/009_group_schedule_slot.sql`. **Chạy trước khi chạy backend mới** (backend đọc các cột mới của `ScheduleSlot` ở mọi truy vấn slot).

## 11. Member tự xếp lịch cho việc nhóm (dùng chung `ScheduleSlot`)

Leader giao việc, đặt deadline và ước lượng như cũ. **Người được giao tự xếp giờ làm vào lịch của mình.** Slot nhóm nằm cùng bảng với slot cá nhân nên dùng chung nhắc việc, đồng bộ Google Calendar, lịch gộp và luật không chồng giờ.

| API | Ghi chú |
|---|---|
| `POST /api/group-tasks/{taskId}/schedule-slots` | Body `{ "subTaskId": "guid" \| null, "startTime": "ISO", "endTime": "ISO", "explanation": "..." \| null, "status": "Approved" \| "Proposed" }` (`status` mặc định `Approved`). `201` trả `GroupSlotDto` |
| `GET /api/group-tasks/{taskId}/schedule-slots?includeCancelled=false` | Slot của việc và các việc con, kèm người xếp. **Mọi thành viên (kể cả Viewer) xem được** |
| `PUT /api/schedule-slots/{slotId}` | Sửa giờ hoặc status, dùng lại API slot cá nhân. Chỉ người xếp slot sửa được |
| `DELETE /api/schedule-slots/{slotId}` | Hủy slot (thành `Cancelled`) |
| `GET /api/calendar` | Lịch gộp của tôi **có cả slot nhóm** (xem bên dưới) |

Luật tạo slot:
- Chỉ **người được giao** (việc, hoặc việc con được giao cho mình, hoặc việc con chưa giao khi mình được giao việc cha). Người khác `403` `GROUP_FORBIDDEN`; người ngoài nhóm `404`; Viewer `403`.
- Việc có việc con thì **phải chọn `subTaskId`** (`400` nếu thiếu). Việc hoặc việc con đã `Done`/`Skipped` thì `400`, phải mở lại trước. Nhóm đã lưu trữ `400`.
- `endTime` phải lớn hơn `startTime` (`400`).
- **Không chồng giờ** với slot đang chiếm giờ khác của chính mình (kể cả slot cá nhân): `409` `SLOT_OVERLAP`, `details.overlaps[]` cùng dạng như slot cá nhân (có `groupTaskId` khi bên kia là slot nhóm). Slot `Proposed` không chặn.
- Không kiểm tra lịch Google khi tạo (giống slot cá nhân tạo tay).
- Slot nhóm **không đổi status của việc nhóm** (việc nhóm chỉ có ToDo/InProgress/Done/Skipped).

Slot trong `GET /api/calendar`: phần tử có `taskId = null`, `groupId`, `groupName`, `groupTaskId`, `groupSubTaskId`; các trường dự án, khách hàng, loại công việc là `null`. Dùng `groupName` và tiêu đề việc để hiển thị. Đồng bộ Google: tiêu đề là `Việc [- Việc con]`, mô tả `Nhóm: tên nhóm`. Nhắc việc gửi cho người đã xếp slot.

`ScheduleSlotDto` (mọi API slot) đổi: **`taskId` có thể `null`** (slot nhóm); thêm `groupTaskId`, `groupSubTaskId`, `userId`. FE phải chịu được `taskId = null`.

**Bấm giờ theo slot:** `scheduleSlotId` khi Start là tùy chọn. Slot phải của chính mình, đúng việc (và đúng việc con nếu slot có việc con) và đang `Approved`, `Due`, `InProgress`, `Missed` hoặc `Postponed`; slot sang `InProgress`. Sai thì `404` (không phải của mình) hoặc `400`. Stop: `Completed` làm slot `Done`; `Blocked` làm slot `Postponed`; lý do khác làm slot `Due` (làm tiếp được). Không gắn slot thì bấm giờ như cũ.

**Khi việc giao cho người khác hoặc người làm rời nhóm** (không còn là người được giao hiệu lực của việc hoặc việc con):
- Slot chưa bắt đầu của người cũ (`Proposed`, `Approved`, `Due`) tự thành `Cancelled`; slot đang chạy (`InProgress`, `Overrun`) thì phiên bấm giờ dừng với lý do `Other` (giờ đã làm vẫn tính cho người cũ), slot về `Due` rồi `Cancelled`. Slot đã `Done`, `Missed`, `Postponed`, `Skipped`, `Cancelled` giữ làm lịch sử.
- Người cũ nhận thông báo `GroupStatusChanged` (có `groupId`, `groupTaskId`) nói rõ việc đã giao cho ai và bao nhiêu slot bị hủy. Người mới không thừa hưởng slot, tự xếp lịch.
- Áp dụng khi: giao lại việc, giao lại việc con, xóa thành viên, hạ thành viên xuống Viewer, thành viên tự rời nhóm (rời thì không gửi thông báo). Xóa việc nhóm thì xóa luôn slot của việc.
- FE nên làm mới lịch khi nhận thông báo loại này.

## 12. Tín hiệu trong báo cáo tiến độ

`GET /api/groups/{id}/progress` có thêm `signals` và bốn bộ đếm trong mỗi `members[]`. Tính lúc xem, không lưu trong DB. Chỉ xét việc **đang mở** (`ToDo`, `InProgress`) và **đã có người được giao**; tính cho người được giao việc (hoặc việc con đang mở).

| Tín hiệu | Điều kiện |
|---|---|
| Quá hạn (`isOverdue`) | Có deadline và đã qua |
| Chưa có lịch (`hasSchedule = false`) | Không có slot nào đang sống (`Proposed`, `Approved`, `Due`, `InProgress`, `Overrun`) của việc hoặc các việc con |
| Nguy cơ trễ (`atRisk`) | Chưa quá hạn, deadline trong **3 ngày** tới, có ước lượng (của việc, rỗng thì cộng các việc con), và số phút còn phải làm (ước lượng trừ giờ đã làm) lớn hơn tổng phút slot chưa diễn ra kết thúc trước deadline |
| Đứng yên (`isStale`) | Lần hoạt động cuối (nhật ký nhóm, bình luận, phiên bấm giờ hoặc ngày tạo việc) cách đây quá **3 ngày** |

`signals.tasks[]` chỉ chứa việc có ít nhất một tín hiệu, sắp xếp: quá hạn, nguy cơ trễ, rồi theo deadline. Chỉ hiện số phút và cờ, **không lộ giờ hay tiêu đề lịch cá nhân**. Admin (`/api/admin/groups/{id}`) chỉ thấy bốn bộ đếm, `signals.tasks` luôn rỗng.

# Phụ lục: cấu trúc dữ liệu (DTO)

Sinh trực tiếp từ code (`METANOIA.Application/Dto/GroupDto.cs`, `GroupPhase2Dto.cs`, `NotificationDto.cs`) nên khớp 100% với API. Quy ước chung cho FE: JSON **camelCase**; id là uuid dạng chuỗi; thời gian là chuỗi ISO 8601 (UTC, có thể có hậu tố `Z` hoặc `+00:00`); ngày là `yyyy-MM-dd`; các giá trị liệt kê (status, vai trò, lý do dừng) là **chuỗi**. Trường có dấu **?** có thể là `null` (khi gửi lên có thể bỏ qua). Mọi danh sách trả về là mảng (rỗng nếu không có), **không phân trang** trừ nhật ký (`skip`, `take`). Cấu trúc chi tiết hơn và thử trực tiếp có ở Swagger (`/swagger`).

## Giá trị cho phép của các trường kiểu chuỗi

| Trường | Giá trị |
|---|---|
| `role`, `myRole` | `Leader`, `Member`, `Viewer` |
| Nhóm `status` | `Active`, `Archived` |
| Việc, việc con `status` | `ToDo`, `InProgress`, `Done`, `Skipped` |
| Giai đoạn `status` | `Planned`, `InProgress`, `Delivered`, `InRevision`, `Approved` |
| `priority` | `Low`, `Medium`, `High` |
| Lời mời `status` | `Pending`, `Accepted`, `Declined`, `Expired`, `Revoked` (`Expired` cũng hiện cho lời mời Pending đã quá 7 ngày) |
| Đề xuất đổi giờ `status` | `Pending`, `Approved`, `Rejected`, `Cancelled` |
| `stopReason` | `Completed`, `Interrupted`, `Blocked`, `Other` |
| Nhật ký `entityType` | `Group`, `Member`, `Invitation`, `Milestone`, `Task`, `SubTask`, `Comment`, `FocusSession`, `ChangeRequest` |
| Khoảng bận `source` | `PersonalSchedule`, `GoogleCalendar`, `GroupTask` |
| Thông báo `type` (nhóm) | `GroupInvited`, `GroupAssigned`, `GroupStatusChanged`, `GroupMention`, `GroupChangeRequested`, `GroupChangeDecided` |

## Dữ liệu gửi lên (request)

### GroupRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `name` | string |  |
| `description?` | string |  |
| `startDate?` | string (yyyy-MM-dd) |  |
| `deadline?` | string (yyyy-MM-dd) |  |

### GroupMemberRoleRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `role` | string |  |

### GroupInvitationRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `email` | string |  |
| `role?` | string | Bỏ trống thì là Member |

### GroupInvitationTokenRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `token` | string |  |

### GroupMilestoneRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `name` | string |  |
| `orderIndex` | number |  |
| `dueDate?` | string (yyyy-MM-dd) |  |
| `status?` | string | Bỏ trống thì giữ nguyên (khi sửa) hoặc Planned (khi tạo) |
| `confirmOpenTasks` | boolean | Vẫn chuyển sang Delivered dù còn việc chưa xong (sau khi đã nhận cảnh báo 409) |

### GroupTaskRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `title` | string |  |
| `description?` | string |  |
| `priority?` | string | Bỏ trống thì là Medium |
| `milestoneId?` | string (uuid) |  |
| `assigneeId?` | string (uuid) | Chỉ dùng khi tạo. Khi sửa, đổi người được giao qua PUT /api/group-tasks/{id}/assignee |
| `deadline?` | string (ISO 8601) |  |
| `estimatedMinutes?` | number |  |
| `fixedStart?` | string (ISO 8601) | Giờ cố định (họp, hẹn): gửi cả hai hoặc không gửi cái nào. Có giờ cố định thì khi giao việc sẽ kiểm tra trùng lịch người nhận |
| `fixedEnd?` | string (ISO 8601) |  |
| `confirmConflicts` | boolean | Vẫn giao dù người nhận bận hoặc quá tải (sau khi đã nhận cảnh báo 409 GROUP_ASSIGNEE_BUSY) |

### GroupTaskStatusRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `status` | string |  |

### GroupAssigneeRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `assigneeId?` | string (uuid) | Rỗng = bỏ giao việc |
| `confirmConflicts` | boolean | Vẫn giao dù người nhận bận hoặc quá tải (chỉ áp dụng cho việc, không áp dụng cho việc con) |

### GroupSubTaskRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `title` | string |  |
| `estimatedMinutes?` | number |  |
| `orderIndex?` | number | Bỏ trống thì xếp cuối danh sách |
| `assigneeId?` | string (uuid) | Chỉ dùng khi tạo. Khi sửa, đổi người được giao qua PUT /api/group-subtasks/{id}/assignee |

### GroupCommentRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `content` | string |  |

### GroupFocusStartRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `subTaskId?` | string (uuid) | Bắt buộc khi việc có việc con (giờ làm tính cho việc con đó). Bỏ trống khi bấm giờ cho cả việc không có việc con |
| `scheduleSlotId?` | string (uuid) | Gắn phiên với một slot của mình (xem mục 11). Bỏ trống thì bấm giờ không gắn slot |

### GroupSlotRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `subTaskId?` | string (uuid) | Bắt buộc khi việc có việc con |
| `startTime` | string (ISO 8601) |  |
| `endTime` | string (ISO 8601) | Lớn hơn `startTime` |
| `explanation?` | string | Tối đa 1000 ký tự |
| `status?` | string | `Approved` (mặc định) hoặc `Proposed` |

### GroupFocusStopRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `stopReason` | "Completed" | "Interrupted" | "Blocked" | "Other" |  |
| `note?` | string | Bắt buộc khi StopReason là Blocked hoặc Other |

### GroupChangeRequestCreateDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `newFixedStart?` | string (ISO 8601) | Giờ cố định mới: gửi cả hai hoặc không gửi cái nào |
| `newFixedEnd?` | string (ISO 8601) |  |
| `newDeadline?` | string (ISO 8601) |  |
| `reason` | string |  |

### GroupChangeDecisionDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `note?` | string | Bắt buộc khi từ chối |
| `confirmConflicts` | boolean | Khi duyệt: vẫn áp dụng dù giờ mới trùng lịch của người nhận (sau khi đã nhận cảnh báo 409) |

### GroupChangeRequestDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupTaskId` | string (uuid) |  |
| `taskTitle` | string |  |
| `requestedById` | string (uuid) |  |
| `requestedByName` | string |  |
| `currentFixedStart?` | string (ISO 8601) |  |
| `currentFixedEnd?` | string (ISO 8601) |  |
| `currentDeadline?` | string (ISO 8601) |  |
| `newFixedStart?` | string (ISO 8601) |  |
| `newFixedEnd?` | string (ISO 8601) |  |
| `newDeadline?` | string (ISO 8601) |  |
| `reason` | string |  |
| `status` | string |  |
| `decidedById?` | string (uuid) |  |
| `decidedByName?` | string |  |
| `decidedAt?` | string (ISO 8601) |  |
| `decisionNote?` | string |  |
| `createdAt` | string (ISO 8601, UTC) |  |

## Dữ liệu nhận về (response)

### GroupDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `name` | string |  |
| `description?` | string |  |
| `ownerId` | string (uuid) |  |
| `startDate?` | string (yyyy-MM-dd) |  |
| `deadline?` | string (yyyy-MM-dd) |  |
| `status` | string |  |
| `createdAt` | string (ISO 8601, UTC) |  |
| `myRole` | string | Vai trò của người đang gọi trong nhóm này |
| `memberCount` | number |  |

### GroupMemberDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `userId` | string (uuid) |  |
| `fullName` | string |  |
| `email` | string |  |
| `role` | string |  |
| `joinedAt` | string (ISO 8601, UTC) |  |

### GroupInvitationDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupId` | string (uuid) |  |
| `groupName` | string |  |
| `email` | string |  |
| `role` | string |  |
| `status` | string |  |
| `invitedById` | string (uuid) |  |
| `invitedByName` | string |  |
| `createdAt` | string (ISO 8601, UTC) |  |
| `expiresAt` | string (ISO 8601, UTC) |  |
| `respondedAt?` | string (ISO 8601, UTC) |  |

### GroupInvitationCreatedDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupId` | string (uuid) |  |
| `groupName` | string |  |
| `email` | string |  |
| `role` | string |  |
| `status` | string |  |
| `invitedById` | string (uuid) |  |
| `invitedByName` | string |  |
| `createdAt` | string (ISO 8601, UTC) |  |
| `expiresAt` | string (ISO 8601, UTC) |  |
| `respondedAt?` | string (ISO 8601, UTC) |  |
| `acceptToken` | string |  |

### GroupMilestoneDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupId` | string (uuid) |  |
| `name` | string |  |
| `orderIndex` | number |  |
| `dueDate?` | string (yyyy-MM-dd) |  |
| `status` | string |  |
| `deliveredAt?` | string (ISO 8601, UTC) |  |
| `revisionCount` | number |  |

### GroupOpenTaskDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `title` | string |  |
| `status` | string |  |
| `openSubTaskCount` | number |  |

### GroupTaskDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupId` | string (uuid) |  |
| `milestoneId?` | string (uuid) |  |
| `assigneeId?` | string (uuid) |  |
| `assigneeName?` | string |  |
| `title` | string |  |
| `description?` | string |  |
| `priority` | string |  |
| `status` | string |  |
| `deadline?` | string (ISO 8601) |  |
| `fixedStart?` | string (ISO 8601) |  |
| `fixedEnd?` | string (ISO 8601) |  |
| `estimatedMinutes?` | number |  |
| `subTaskCount` | number |  |
| `subTaskDoneCount` | number |  |
| `createdAt` | string (ISO 8601, UTC) |  |
| `completedAt?` | string (ISO 8601, UTC) |  |

### GroupSubTaskDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupTaskId` | string (uuid) |  |
| `assigneeId?` | string (uuid) |  |
| `assigneeName?` | string |  |
| `title` | string |  |
| `estimatedMinutes?` | number |  |
| `orderIndex` | number |  |
| `status` | string |  |
| `completedById?` | string (uuid) |  |
| `completedAt?` | string (ISO 8601, UTC) |  |

### GroupCommentDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupTaskId` | string (uuid) |  |
| `authorId` | string (uuid) |  |
| `authorName` | string |  |
| `content` | string |  |
| `createdAt` | string (ISO 8601, UTC) |  |
| `editedAt?` | string (ISO 8601, UTC) |  |

### GroupActivityDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `actorId` | string (uuid) |  |
| `actorName` | string |  |
| `entityType` | string |  |
| `entityId?` | string (uuid) |  |
| `action` | string |  |
| `oldValue?` | string |  |
| `newValue?` | string |  |
| `createdAt` | string (ISO 8601, UTC) |  |

### GroupProgressDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `groupId` | string (uuid) |  |
| `totalTasks` | number |  |
| `totalTrackedMinutes` | number | Tổng giờ làm thực tế của cả nhóm (phút, các phiên đã kết thúc) và tổng ước lượng của các việc có ước lượng |
| `totalEstimatedMinutes` | number |  |
| `tasksByStatus` | object (khóa → số) | ToDo, InProgress, Done, Skipped (đủ cả bốn khóa, kể cả số 0) |
| `remainingTasks` | number | Việc còn lại = chưa Done và chưa Skipped |
| `unassignedTasks` | number |  |
| `completedPercent` | number | Done trên (tổng trừ Skipped), làm tròn 1 chữ số thập phân; 0 khi chưa có việc |
| `members` | GroupMemberProgressDto[] |  |
| `milestones` | GroupMilestoneProgressDto[] |  |
| `signals` | GroupSignalsDto | Tín hiệu (mục 12) |

### GroupSignalsDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `overdueTasks` | number | Số việc quá hạn |
| `unscheduledTasks` | number | Số việc chưa có lịch |
| `atRiskTasks` | number | Số việc có nguy cơ trễ |
| `staleTasks` | number | Số việc đứng yên |
| `tasks` | GroupTaskSignalDto[] | Việc có ít nhất một tín hiệu |

### GroupTaskSignalDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `taskId` | string (uuid) |  |
| `title` | string |  |
| `assigneeId?` | string (uuid) |  |
| `assigneeName?` | string |  |
| `status` | string |  |
| `deadline?` | string (ISO 8601) |  |
| `isOverdue` | boolean |  |
| `hasSchedule` | boolean |  |
| `atRisk` | boolean |  |
| `isStale` | boolean |  |
| `scheduledMinutes` | number | Tổng phút slot chưa diễn ra, kết thúc trước deadline |
| `estimatedMinutes?` | number |  |
| `lastActivityAt?` | string (ISO 8601) |  |

### GroupMemberProgressDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `userId` | string (uuid) |  |
| `fullName` | string |  |
| `role` | string |  |
| `assignedTasks` | number |  |
| `doneTasks` | number |  |
| `assignedSubTasks` | number |  |
| `doneSubTasks` | number |  |
| `trackedMinutes` | number | Giờ làm thực tế của người này trong nhóm (phút, các phiên đã kết thúc) |
| `overdueTasks` | number | Số việc của người này quá hạn |
| `unscheduledTasks` | number | Số việc chưa có lịch |
| `atRiskTasks` | number | Số việc có nguy cơ trễ |
| `staleTasks` | number | Số việc đứng yên |

### GroupMilestoneProgressDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `milestoneId` | string (uuid) |  |
| `name` | string |  |
| `status` | string |  |
| `dueDate?` | string (yyyy-MM-dd) |  |
| `totalTasks` | number |  |
| `doneTasks` | number |  |

### MyGroupWorkDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `tasks` | MyGroupTaskDto[] |  |
| `subTasks` | MyGroupSubTaskDto[] |  |

### MyGroupTaskDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `taskId` | string (uuid) |  |
| `groupId` | string (uuid) |  |
| `groupName` | string |  |
| `title` | string |  |
| `status` | string |  |
| `priority` | string |  |
| `deadline?` | string (ISO 8601) |  |
| `fixedStart?` | string (ISO 8601) |  |
| `fixedEnd?` | string (ISO 8601) |  |

### MyGroupSubTaskDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `subTaskId` | string (uuid) |  |
| `taskId` | string (uuid) |  |
| `groupId` | string (uuid) |  |
| `groupName` | string |  |
| `taskTitle` | string |  |
| `title` | string |  |
| `status` | string |  |

### GroupFocusStartResponseDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `sessionId` | string (uuid) |  |
| `groupTaskId` | string (uuid) |  |
| `subTaskId?` | string (uuid) |  |
| `startedAt` | string (ISO 8601) |  |
| `taskStatus` | string | Status sau khi bắt đầu: việc con ToDo thành InProgress, việc cha tự suy lại |
| `subTaskStatus?` | string |  |
| `scheduleSlotId?` | string (uuid) | Slot đã gắn khi Start |

### GroupFocusStopResponseDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `sessionId` | string (uuid) |  |
| `endedAt` | string (ISO 8601) |  |
| `sessionMinutes` | number |  |
| `myTaskMinutes` | number | Tổng giờ của chính người bấm trên việc này và tổng của cả nhóm trên việc này |
| `taskTotalMinutes` | number |  |
| `taskStatus` | string |  |
| `subTaskStatus?` | string |  |
| `wasCapped` | boolean | True khi phiên bị cắt theo giới hạn độ dài (quên bấm Stop) |

### GroupFocusSessionDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `groupTaskId` | string (uuid) |  |
| `subTaskId?` | string (uuid) |  |
| `userId` | string (uuid) |  |
| `userName` | string |  |
| `startedAt` | string (ISO 8601) |  |
| `endedAt?` | string (ISO 8601) |  |
| `minutes` | number | Phiên đang chạy: tính tới giờ hiện tại |
| `isRunning` | boolean |  |
| `stopReason?` | string |  |
| `note?` | string |  |
| `scheduleSlotId?` | string (uuid) |  |

### GroupFocusCurrentDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `sessionId` | string (uuid) |  |
| `groupId` | string (uuid) |  |
| `groupName` | string |  |
| `groupTaskId` | string (uuid) |  |
| `taskTitle` | string |  |
| `subTaskId?` | string (uuid) |  |
| `subTaskTitle?` | string |  |
| `startedAt` | string (ISO 8601) |  |
| `elapsedSeconds` | number |  |
| `serverTime` | string (ISO 8601) |  |
| `estimatedMinutes?` | number | Ước lượng của việc, để FE hiện "đã làm / ước lượng" |
| `scheduleSlotId?` | string (uuid) | Slot đang gắn với phiên (nếu có) |

### GroupTaskTimeDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `groupTaskId` | string (uuid) |  |
| `estimatedMinutes?` | number |  |
| `totalMinutes` | number |  |
| `sessionCount` | number |  |
| `byUser` | GroupTimeByUserDto[] |  |
| `bySubTask` | GroupTimeBySubTaskDto[] |  |
| `sessions` | GroupFocusSessionDto[] |  |

### GroupTimeByUserDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `userId` | string (uuid) |  |
| `fullName` | string |  |
| `minutes` | number |  |
| `sessionCount` | number |  |

### GroupTimeBySubTaskDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `subTaskId?` | string (uuid) | Rỗng = giờ làm tính thẳng cho việc (việc không có việc con) |
| `title?` | string |  |
| `minutes` | number |  |

### GroupAssigneeAvailabilityDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `assigneeId` | string (uuid) |  |
| `isBusy` | boolean | Có giờ cố định trùng với lịch của người nhận |
| `conflicts` | GroupBusyPeriodDto[] |  |
| `workload?` | GroupWorkloadDto | Khối lượng việc nhóm trong tuần của deadline; null khi việc chưa có deadline hoặc ước lượng |
| `needsConfirmation` | boolean | IsBusy hoặc Workload.IsOverloaded: Leader cần xác nhận nếu vẫn giao |

### GroupBusyPeriodDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `start` | string (ISO 8601) |  |
| `end` | string (ISO 8601) |  |
| `source` | string | PersonalSchedule (slot cá nhân), GoogleCalendar, hoặc GroupTask (việc nhóm khác có giờ cố định) |

### GroupWorkloadDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `assignedMinutes` | number | Tổng phút ước lượng của các việc nhóm chưa xong của người này có deadline trong 7 ngày kết thúc ở deadline, cộng việc đang xét |
| `capacityMinutes` | number | Số phút làm việc của một tuần theo cài đặt giờ làm của người nhận |
| `isOverloaded` | boolean |  |

### GroupSlotDto

Gồm mọi trường của `ScheduleSlotDto` (với `taskId = null`, có `groupTaskId`, `groupSubTaskId`, `userId` là người xếp) và thêm:

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `groupId` | string (uuid) |  |
| `userName` | string | Người xếp slot |
| `taskTitle` | string |  |
| `subTaskTitle?` | string |  |

### NotificationDto

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (uuid) |  |
| `type` | string | Loại thông báo, ví dụ START_PROMPT, NEAR_END, OVERRUN, AUTO_TIMEOUT |
| `title` | string |  |
| `message` | string |  |
| `scheduleSlotId?` | string (uuid) |  |
| `groupId?` | string (uuid) | Thông báo nhóm: nhóm và việc liên quan (GroupTaskId rỗng với lời mời vào nhóm). Rỗng với thông báo khác |
| `groupTaskId?` | string (uuid) |  |
| `sentAt?` | string (ISO 8601) |  |
| `isRead` | boolean |  |
| `readAt?` | string (ISO 8601) |  |
