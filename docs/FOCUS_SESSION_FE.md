# Focus session: tài liệu cho Frontend (Start / Stop cho Task và Subtask)

Dành cho FE dựng màn hình **bấm giờ làm việc**, **nhắc việc** và **thông báo**.
Mọi hành vi trong tài liệu này đã được **kiểm tra bằng chạy thật** trên bản code hiện tại (mục 11 liệt kê kết quả).
Phía backend giải thích thêm ở `docs/FOCUS_SESSION_GUIDE.md`. **Luật chuyển trạng thái của Milestone, Task, Subtask, Slot và ý nghĩa `StopReason` / `CaptureMethod` nằm ở [STATUS_WORKFLOW.md](STATUS_WORKFLOW.md).** Trang mẫu chạy được: `/focus-test.html` (chế độ Development).

---

## 1. Tóm tắt (xác nhận)

| Câu hỏi | Trả lời |
|---|---|
| Có Start/Stop cho **task** không? | **Có**, đầy đủ. Chỉ cần gửi `taskId` |
| Có Start/Stop cho **subtask** không? | **Có**. Gửi `taskId` (của task cha) **kèm** `subTaskId`. API Stop hoàn toàn giống nhau |
| Hai loại dùng chung API không? | **Có**: cùng `POST /api/focus/start` và `POST /api/focus/{id}/stop`. Khác nhau chỉ là có gửi `subTaskId` hay không |
| Subtask có đồng hồ riêng không? | Không. Đồng hồ là của **phiên** (một phiên đang chạy một lúc), phiên được gắn nhãn task hoặc subtask |
| Start/Stop có tự đổi trạng thái task hay subtask không? | **Có.** Start làm task `InProgress`. Stop `Completed` trên phiên của subtask làm subtask `Done`; mọi subtask xong thì task tự `Done`. Chi tiết ở mục 5.2 và 6.2 |
| Tổng thời gian tính theo subtask không? | **Chưa.** Tổng luôn là của **cả task** (cộng mọi phiên, mọi subtask). Chưa có số liệu theo từng subtask |

Điểm FE hay hiểu sai nhất: **trạng thái nhiều bảng đổi theo một lần Start/Stop** (slot, subtask, task, giai đoạn). Sau mỗi Start, Stop, tạo hoặc hủy slot, FE nên **tải lại** các bảng đó thay vì tự tính. Stop `Blocked` hoặc `Other` **bắt buộc có `note`**.

---

## 2. Khái niệm

- **Task**: công việc. **Subtask**: việc nhỏ thuộc một task (`ToDo`, `Done`, `Skipped`).
- **Slot** (`ScheduleSlot`): một khung giờ trên lịch. Slot luôn thuộc một **task** (`taskId`), và **có thể** gắn thêm một subtask (`subTaskId`).
- **Phiên làm việc** (`FocusSession`): một lần bấm giờ, từ Start đến Stop. Mỗi phiên có `taskId` (bắt buộc) và `subTaskId` (tùy chọn). Thời gian làm = `kết thúc − bắt đầu`, tính theo giờ server.
- Có thể làm việc **có slot** (theo lịch) hoặc **không slot** (ngoài lịch).
- **Một người chỉ có một phiên chạy một lúc.** Kể cả khác task hay khác subtask.
- **Không có "pause/resume".** Tạm dừng là Stop với `Interrupted`, làm tiếp là Start phiên mới.

---

## 3. Quy ước chung

- Base URL: backend. Mọi API cần `Authorization: Bearer <accessToken>`; hết hạn (401) thì `POST /api/auth/refresh` như các API khác.
- JSON **camelCase**. Enum là **chuỗi** (`"Timer"`, `"Completed"`), cả khi gửi lên lẫn nhận về.
- Thời gian là ISO 8601 UTC. Hiển thị theo múi giờ người dùng ở FE.
- **FE không gửi giờ nào** (trừ `self-report`). Đồng hồ vẽ theo giờ server (mục 8).
- Đường dẫn gốc `/api/focus` (đường cũ `/api/FocusSession` vẫn chạy).

| HTTP | Thân | Ý nghĩa |
|---|---|---|
| 400 | `{ "message": "..." }` | Dữ liệu sai hoặc trạng thái không cho phép. Hiện `message` |
| 401 | rỗng | Hết phiên |
| 403 | `{ "message", "code": "FOCUS_SESSION_FORBIDDEN" }` | Phiên của người khác |
| 404 | `{ "message" }` | Không tìm thấy, hoặc không phải của bạn |
| 409 | `{ "message", "code": "FOCUS_SESSION_CONFLICT", "details": ... }` | Xung đột, xem từng API |

---

## 4. Trạng thái slot (quyết định nút Start/Stop hiển thị)

| `status` của slot | Nghĩa | FE hiển thị |
|---|---|---|
| `Proposed` | Chưa duyệt | Nút **Duyệt**. **Chưa Start được** (400) |
| `Approved` | Đã duyệt, chưa tới giờ | Start sớm được (xem cảnh báo ở 5.1) |
| `Due` | Tới giờ, chờ bấm Start | **Nút Start nổi bật** |
| `InProgress` | Đang làm | Đồng hồ chạy, nút **Stop** |
| `Overrun` | Quá giờ dự kiến mà vẫn đang làm | Đồng hồ **đỏ**, nút Stop |
| `Done` | Xong | Hoàn thành |
| `Missed` | Hết giờ mà chưa Start | "Đã bỏ lỡ", vẫn cho Start làm bù |
| `Postponed` | Đang chờ (ví dụ Stop `Blocked`), hết nhắc "bấm Start" | Hiện "Đang chờ"; cho Start làm tiếp hoặc xếp lại thành `Approved` |
| `Skipped`, `Cancelled` | Bỏ qua, hủy | Ẩn hoặc làm mờ. **Trạng thái chốt**, không đổi tiếp |

Hệ thống **không tự bấm giờ**: tới giờ slot chỉ chuyển `Due` và nhắc, chỉ khi người dùng bấm Start mới có thời gian làm việc.
Start được khi slot ở: `Approved`, `Due`, `InProgress`, `Overrun`, `Missed`, `Postponed`. Slot `Proposed`, `Done`, `Skipped`, `Cancelled` thì 400.

---

## 5. START / STOP cho TASK

### 5.1 Start task

`POST /api/focus/start`

| Trường | Bắt buộc | Ý nghĩa |
|---|---|---|
| `taskId` | **Có** | Task đang làm |
| `scheduleSlotId` | Không | Gắn vào một slot trên lịch (làm theo lịch) |
| `focusTrackId` | Không | Bài nhạc tập trung |

Hai cách dùng:

**a) Làm task ngoài lịch (không slot)**
```json
{ "taskId": "<taskId>" }
```
**b) Làm task theo slot của task (slot không gắn subtask)**
```json
{ "taskId": "<taskId>", "scheduleSlotId": "<slotId>" }
```

**201**:
```json
{
  "sessionId": "…",
  "startedAt": "2026-10-28T10:00:00+00:00",
  "captureMethod": "Timer",
  "plannedMinutes": 60,
  "expectedEndAt": "2026-10-28T11:00:00+00:00",
  "startedEarlyMinutes": 0,
  "warning": null
}
```
- Không gắn slot thì `plannedMinutes = 0` và `expectedEndAt = startedAt`.
- Gắn slot: slot đổi sang `InProgress`, `plannedMinutes` lấy từ độ dài slot.
- `startedEarlyMinutes > 0`: bắt đầu sớm hơn giờ slot. `warning = "STARTED_VERY_EARLY"` khi sớm **hơn 2 giờ**: phiên **đã được tạo**, hãy hiện hộp thoại "Bạn đang bắt đầu sớm hơn lịch X phút, đúng không?"; nếu người dùng bấm "Không" thì gọi Stop ngay.

Lỗi:

| HTTP | Khi nào | FE làm gì |
|---|---|---|
| **409** | Đã có phiên đang chạy. `details` là phiên đó (cùng dạng `GET /current`) | Không báo lỗi đỏ, chuyển sang màn hình phiên đang chạy |
| 400 | Slot chưa duyệt / đã `Done` / `Cancelled`; slot không thuộc task; bài nhạc không tồn tại hoặc đã tắt | Hiện `message` |
| 404 | Task hoặc slot không phải của bạn | |

### 5.2 Stop task

`POST /api/focus/{sessionId}/stop` (**giống hệt cho subtask**, xem 6.2)

```json
{ "stopReason": "Completed", "note": "Đã gửi file cho khách" }
```
`stopReason`: `Completed` (xong), `Interrupted` (tạm dừng, gián đoạn), `Blocked` (bị kẹt, ví dụ chờ khách), `Other` (lý do khác). `note` **bắt buộc khi `Blocked` hoặc `Other`** (thiếu thì 400 và phiên vẫn chạy), còn lại tùy chọn.

**200**:
```json
{ "sessionId": "…", "endedAt": "…", "sessionMinutes": 25, "taskTotalMinutes": 35, "checkInRequired": true }
```
- `sessionMinutes`: phiên vừa dừng. `taskTotalMinutes`: tổng mọi phiên **của cả task** (xem mục 7).
- `checkInRequired`: `true` với `Completed` và `Other` (nên hỏi "xong chưa / thấy thế nào"), `false` với `Interrupted`, `Blocked` (người dùng sẽ quay lại).
- Tác động của từng lý do (slot áp dụng khi phiên có gắn slot):

| `stopReason` | Slot | Subtask của phiên | Task |
|---|---|---|---|
| `Completed` | `Done` | **`Done`** (nếu phiên có `subTaskId`) | Suy lại; mọi subtask xong thì tự `Done` |
| `Interrupted` | `Due` (hiện lại nút Start) | không đổi | `InProgress` |
| `Blocked` | **`Postponed`**, hủy các nhắc của slot | không đổi | `InProgress` |
| `Other` | `Due` | không đổi | `InProgress` |

Stop `Completed` trên task **không có subtask** chỉ đóng slot; người dùng tự bấm `Done` cho task (`PUT /api/tasks/{id}`).

| HTTP | Ý nghĩa |
|---|---|
| 400 | `Blocked` hoặc `Other` mà thiếu `note`. **Phiên vẫn đang chạy**, hiện `message` và cho nhập ghi chú |
| 409 | Phiên đã dừng trước đó (bấm hai lần, hai tab). `details.endedAt` là giờ đã dừng. Coi như thành công, tải lại `/current` |
| 403 / 404 | Phiên của người khác / không tồn tại |

---

## 6. START / STOP cho SUBTASK

### 6.1 Start subtask

Cùng API với task, chỉ **thêm `subTaskId`**. `taskId` vẫn **bắt buộc** (là task cha của subtask).

**a) Làm subtask ngoài lịch (không slot)**
```json
{ "taskId": "<task cha>", "subTaskId": "<subtaskId>" }
```
**b) Làm subtask theo slot của subtask** (slot được tạo với `subTaskId`)
```json
{ "taskId": "<task cha>", "subTaskId": "<subtaskId>", "scheduleSlotId": "<slotId>" }
```
Phản hồi 201 y hệt mục 5.1.

Quy tắc đã xác nhận:

| Quy tắc | Kết quả |
|---|---|
| `subTaskId` phải thuộc đúng `taskId` | Subtask của task khác, hoặc không tồn tại → **400** "Subtask không tồn tại trong task này" |
| Phiên lưu `subTaskId` | Có. Phiên được gắn nhãn subtask |
| Đang chạy một phiên (task hay subtask) mà Start phiên khác | **409**, kể cả khác subtask |
| Slot gắn subtask nhưng **không gửi** `subTaskId` khi Start | Phiên **không** có subtask. Backend **không tự lấy** `subTaskId` từ slot |
| Slot của subtask A nhưng gửi `subTaskId = B` (lệch) | Vẫn **thành công (201)**, backend không đối chiếu. FE **không được** gửi lệch |

> **FE phải tự lấy `subTaskId` của slot và gửi kèm.** Item của `GET /api/calendar` có sẵn `subTaskId` và `subTaskTitle` cho slot gắn subtask. Quy tắc cho nút Start trên một slot: nếu slot có `subTaskId` thì gửi nó; nếu không thì không gửi.

### 6.2 Stop subtask

**Giống hệt Stop task** (`POST /api/focus/{sessionId}/stop`, cùng body, cùng phản hồi). Backend nhận ra phiên là của subtask nào nhờ `sessionId`, FE không gửi `subTaskId` khi Stop.

Hành vi đã xác nhận:
- `Completed` → **slot** của phiên thành `Done` **và subtask của phiên thành `Done`**. Slot khác của cùng task **không đổi**. Subtask cuối cùng xong thì **task tự `Done`**.
- `sessionMinutes`: thời gian phiên này. `taskTotalMinutes`: **tổng cả task** (phiên task-only và mọi subtask cộng chung), **không** tách theo subtask.
- Các lý do khác (`Interrupted`, `Blocked`, `Other`) **không đổi subtask**; task vẫn `InProgress`.

### 6.3 Đánh dấu thủ công và việc hệ thống tự làm

Stop `Completed` đã tự đóng **slot** và **subtask của phiên**. Các trường hợp còn lại FE gọi API riêng (cả hai đều **thay thế toàn bộ**, nên gửi đủ các trường; riêng `status` nếu **không gửi** thì giữ nguyên trạng thái hiện có):

**Subtask:** `PUT /api/subtasks/{subTaskId}`
```json
{ "title": "Subtask 1", "estimatedMinutes": 20, "orderIndex": 1, "status": "Done" }
```
**Task:** `PUT /api/tasks/{taskId}`: lấy task bằng `GET /api/tasks/{taskId}`, gửi lại **đủ** các trường rồi đổi `status` (xem [STATUS_WORKFLOW.md](STATUS_WORKFLOW.md) cho các bước hợp lệ).

Hệ thống tự làm (FE chỉ cần tải lại):

| Việc | Hệ quả |
|---|---|
| Mọi subtask `Done` (hoặc `Skipped`, ít nhất một `Done`) | Task tự `Done`, ghi `completedAt` |
| Mở lại một subtask (`→ ToDo`) của task `Done` | Task mở lại (`InProgress`), xóa `completedAt` |
| Đặt task `Done` hoặc `Skipped` | Subtask còn `ToDo` thành `Skipped`; slot chưa diễn ra thành `Cancelled`. Đang có phiên chạy thì **400**, phải Stop trước |
| Start | Task `InProgress`; giai đoạn `Planned` tự lên `InProgress` |
| Tạo slot `Approved` (hoặc duyệt `Proposed`) | Task `Scheduled` |

Bước chuyển sai (ví dụ task `Done` → `Skipped`, subtask `Done` → `Skipped`) trả **400**; `message` liệt kê các bước hợp lệ.

---

## 7. So sánh nhanh Task và Subtask

| | **Task** | **Subtask** |
|---|---|---|
| API Start | `POST /api/focus/start` | **Cùng API** |
| Body Start | `{ taskId, scheduleSlotId? }` | `{ taskId, subTaskId, scheduleSlotId? }` |
| API Stop | `POST /api/focus/{sessionId}/stop` | **Cùng API** |
| Slot trên lịch | Slot không có `subTaskId` | Slot có `subTaskId` (cùng `taskId` cha) |
| Phiên lưu | `taskId` | `taskId` **và** `subTaskId` |
| Tổng thời gian trả về | Cả task | **Cả task** (không tách riêng subtask) |
| Đổi trạng thái tự động | Start → task `InProgress`; mọi subtask xong → task `Done` | Stop `Completed` → subtask `Done` |
| Đánh dấu xong | Tự động khi mọi subtask xong, hoặc `PUT /api/tasks/{id}` (`Done`) | Stop `Completed`, hoặc `PUT /api/subtasks/{id}` (`Done`) |
| Giới hạn một phiên một lúc | Có | Có (chung với task) |

Bảng cho FE: tùy tình huống, body Start là gì.

| Người dùng bấm Start ở đâu | Body gửi lên |
|---|---|
| Task, không lịch | `{ taskId }` |
| Slot của task (không subtask) | `{ taskId, scheduleSlotId }` |
| Subtask, không lịch | `{ taskId, subTaskId }` |
| Slot của subtask | `{ taskId, subTaskId, scheduleSlotId }` |

Mẫu TypeScript:
```ts
type FocusStartBody = { taskId: string; scheduleSlotId?: string; subTaskId?: string; focusTrackId?: string };

function buildStartBody(task: { id: string }, opts?: { subTaskId?: string | null; slot?: { id: string; subTaskId?: string | null } }): FocusStartBody {
  const body: FocusStartBody = { taskId: task.id };
  if (opts?.slot) body.scheduleSlotId = opts.slot.id;
  // slot gắn subtask thì gửi đúng subtask đó; backend không tự lấy
  const sub = opts?.slot?.subTaskId ?? opts?.subTaskId;
  if (sub) body.subTaskId = sub;
  return body;
}
```

---

## 8. Các API còn lại (dùng chung cho task và subtask)

### 8.1 `GET /api/focus/current`: tôi đang làm gì?
**200**:
```json
{
  "sessionId": "…", "taskId": "…", "taskTitle": "Viết báo cáo",
  "scheduleSlotId": "…", "startedAt": "…",
  "elapsedSeconds": 754, "serverTime": "2026-10-28T10:12:34+00:00", "isOverrun": false
}
```
**204** (không có thân): không có phiên nào chạy. Gọi khi mở app, tải lại trang, đổi thiết bị, tab được focus lại, và sau mỗi thông báo SignalR.

> **Chú ý subtask:** phản hồi **không có `subTaskId`** (đã kiểm tra). Muốn hiện "đang làm subtask nào", FE tự nhớ subtask của phiên vừa Start (lưu cùng `sessionId`). Khi người dùng mở ở thiết bị khác thì không biết được.

### 8.2 `GET /api/focus/{slotId}/time-summary`
(cũng gọi được ở `GET /api/slots/{slotId}/time-summary`)
```json
{
  "plannedMinutes": 60, "actualMinutes": 65.5, "sessionCount": 2,
  "sessions": [
    { "sessionId": "…", "startedAt": "…", "endedAt": "…", "minutes": 25, "captureMethod": "Timer", "stopReason": "Interrupted" }
  ]
}
```
Tính theo **slot** (chỉ các phiên gắn slot đó). `actualMinutes` chỉ cộng phiên đáng tin (không tính phiên bị tự ngắt chưa xác nhận). Mỗi phiên có `sessionId` nhưng **không có `subTaskId`**.

### 8.3 `GET /api/focus/pending-confirmation`: phiên cần xác nhận
Phiên mà **hệ thống tự ngắt** (người dùng quên Stop, chạy quá 2 lần thời gian dự kiến) và chưa xác nhận. Mảng, mới nhất trước, rỗng nếu không có:
```json
[{ "sessionId": "…", "taskId": "…", "taskTitle": "…", "scheduleSlotId": "…", "startedAt": "…", "endedAt": "…", "systemMinutes": 60 }]
```
Gọi khi mở app và khi nhận thông báo `AUTO_TIMEOUT`. Không có `subTaskId`.

### 8.4 `POST /api/focus/{sessionId}/confirm`
| Người dùng chọn | Body |
|---|---|
| "Đúng, đã làm đủ" | `{ "confirmed": true }` |
| "Thực ra tôi làm 45 phút" | `{ "confirmed": true, "correctedMinutes": 45 }` |
| "Bỏ phiên này" | `{ "confirmed": false }` |

**200**. Chỉ dùng cho phiên từ `pending-confirmation` (phiên khác trả 400). Phiên chưa xác nhận hoặc bị loại bỏ **không được tính** vào thời gian thực tế và không dùng để học. `correctedMinutes` từ 1 đến 1440, và không được làm giờ kết thúc ở tương lai.

### 8.5 `POST /api/focus/self-report`: quên bấm Start
```json
{ "taskId": "…", "scheduleSlotId": null, "startedAt": "2026-10-28T08:00:00Z", "endedAt": "2026-10-28T09:15:00Z", "stopReason": "Completed", "note": null }
```
**201** `{ "sessionId": "…" }`. Hợp lệ khi `endedAt > startedAt`, cả hai không ở tương lai, dài tối đa 24 giờ. Gắn `scheduleSlotId` và `stopReason = "Completed"` thì slot thành `Done`.

> **Chú ý subtask:** self-report **bỏ qua** `subTaskId` nếu bạn gửi (đã kiểm tra: gửi kèm vẫn trả 201 nhưng phiên không có subtask). Phiên khai báo chỉ gắn với task.

> **Ghi chú lý do dừng:** `stopReason = Blocked` hoặc `Other` cũng **bắt buộc `note`** (thiếu thì 400). Phiên khai báo là `SelfReported`, đã xác nhận, được tính. Task có phiên đáng tin thì lên `InProgress`.

### 8.6 `GET /api/focus/task/{taskId}/sessions`: mọi phiên của một task

Mảng sắp theo thời gian bắt đầu (cũ trước, mới sau), gồm cả phiên chưa xác nhận và đã loại bỏ (khác `time-summary`):
```json
[{ "sessionId": "…", "subTaskId": null, "scheduleSlotId": "…", "startedAt": "…", "endedAt": "…", "minutes": 25,
   "captureMethod": "Timer", "stopReason": "Completed", "isConfirmed": true, "note": null, "state": "Stopped" }]
```
`state` suy ra từ dữ liệu: `Running`, `Stopped`, `PendingConfirmation`, `Confirmed`, `Discarded` (xem [STATUS_WORKFLOW.md](STATUS_WORKFLOW.md) mục 6). Phiên có `subTaskId`, nên đây là nơi duy nhất cho biết phiên thuộc subtask nào sau khi tải lại trang. `404` nếu task không phải của bạn.

---

## 9. Cách vẽ đồng hồ (quan trọng)

Người dùng có thể đổi giờ máy, nên **không** tính `Date.now() - startedAt`.

```ts
// Khi nhận /current (hoặc phản hồi 409 của /start):
const baseElapsed = res.elapsedSeconds;     // server tính
const receivedAt = performance.now();       // đồng hồ đơn điệu, không bị đổi giờ máy ảnh hưởng

// Mỗi giây:
const elapsed = baseElapsed + (performance.now() - receivedAt) / 1000;
const remaining = plannedMinutes * 60 - elapsed;   // âm nghĩa là đã quá giờ
```
- Đồng bộ lại bằng `GET /current` mỗi 15 đến 30 giây và khi tab được focus (`visibilitychange`).
- `isOverrun = true` hoặc `remaining < 0`: đổi sang màu cảnh báo.
- `plannedMinutes` lấy từ phản hồi `start`, hoặc `proposedMinutes` của slot / item calendar.

---

## 10. Thông báo

Ba đường, nên làm đủ:

| Đường | Khi nào | Dùng để |
|---|---|---|
| **SignalR** `/hubs/notifications` | Đang mở app | Toast realtime, cập nhật trạng thái slot ngay |
| **Web Push** | Kể cả khi đóng tab | Thông báo hệ điều hành |
| **Hộp thư** `GET /api/notifications` | Luôn | Xem lại thông báo đã bỏ lỡ, huy hiệu chưa đọc |

Thông báo chỉ nhắc **tên task** (không nhắc tên subtask). Thông báo gắn với **slot**, không gắn với phiên.

### 10.1 SignalR
```ts
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API}/hubs/notifications`, { accessTokenFactory: () => getAccessToken() })
  .withAutomaticReconnect()
  .build();

connection.on("ReceiveNotification", (message: string, actionType: string, notificationId: string) => {
  // hiện toast; rồi refetch /current, trạng thái slot, hộp thư
});
await connection.start();
```
- Phải gửi JWT qua `accessTokenFactory`. Token hết hạn thì `stop()` rồi tạo kết nối mới với token mới.
- Domain FE phải nằm trong `Frontend:AllowedOrigins` của backend.

| `actionType` | Khi nào | FE làm gì |
|---|---|---|
| `PRE_START_REMINDER` | Trước giờ bắt đầu đúng **số phút nhắc trước** của người dùng (`reminderLeadMinutes` trong `PUT /api/me/settings`, mặc định 15; đặt `0` thì không nhắc trước) | Toast "Sắp đến giờ" (nội dung nêu số phút còn lại thật) |
| `START_PROMPT` | Tới giờ, slot thành `Due` | Toast kèm nút **Start** |
| `NOT_STARTED` | 20 phút sau giờ mà chưa Start | Nhắc lại |
| `NEAR_END` | Còn 10 phút hết giờ dự kiến | Toast "Sắp hết giờ" |
| `OVERRUN` | Quá giờ 15 phút mà phiên vẫn chạy | Toast "Nhớ bấm Stop" |
| `AUTO_TIMEOUT` | Hệ thống tự ngắt phiên | Gọi `pending-confirmation`, mở hộp thoại xác nhận |
| `CHECK_IN` | Hết giờ, đã làm dở nhưng chưa xong | Hỏi "Công việc đã xong chưa?" |
| `MISSED_SLOT` | Hết giờ mà chưa Start | Hiện "Đã bỏ lỡ" |

### 10.2 Web Push
1. `GET /api/notifications/vapid-public-key` lấy `{ publicKey }`.
2. Đăng ký service worker, rồi `registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(publicKey) })`.
3. Gửi `subscription.toJSON()` lên `POST /api/notifications/subscribe`:
   ```json
   { "endpoint": "https://fcm.googleapis.com/...", "keys": { "p256dh": "…", "auth": "…" } }
   ```
4. Hủy: `POST /api/notifications/unsubscribe` cùng body, rồi `subscription.unsubscribe()`.

Server gửi payload `{ "title", "body", "action" }` (`action` là một `actionType` ở bảng trên). Service worker mẫu: `wwwroot/sw.js` của backend. Chỉ chạy trên **https** (hoặc localhost). Một người có thể đăng ký nhiều thiết bị.

### 10.3 Hộp thư
| API | Việc làm |
|---|---|
| `GET /api/notifications?unreadOnly=false&take=50` | Danh sách, mới nhất trước (tối đa 100) |
| `POST /api/notifications/{id}/read` | Đánh dấu đã đọc (204) |
| `POST /api/notifications/read-all` | Đánh dấu hết, trả `{ "updated": n }` |

Mỗi phần tử: `id`, `type`, `title`, `message`, `scheduleSlotId`, `sentAt`, `isRead`, `readAt`.
**Lưu ý:** `type` trong hộp thư chỉ là nhóm lớn `SlotReminder` hoặc `MissedSlot` (database giới hạn). Muốn biết loại nào thì dựa vào `title`. Loại chi tiết (`actionType`) chỉ có ở SignalR và Web Push.

---

## 11. Kết quả đã kiểm tra (bản code hiện tại)

Chạy thật cho một task có 2 subtask và 3 slot (một slot task, mỗi subtask một slot). Đây là những gì quan sát được:

| # | Tình huống | Kết quả |
|---|---|---|
| 1 | Start `{ taskId }` | 201, phiên **không** có subtask. Stop `Interrupted` sau 10 phút: `sessionMinutes = 10`, `taskTotalMinutes = 10`, `checkInRequired = false` |
| 2 | Start `{ taskId, subTaskId = S1 }` | 201, phiên **lưu `subTaskId = S1`** |
| 3 | `GET /current` khi đang chạy phiên subtask | **Không có** field `subTaskId` (chỉ có `sessionId, taskId, taskTitle, scheduleSlotId, startedAt, elapsedSeconds, serverTime, isOverrun`) |
| 4 | Đang chạy S1, Start S2, rồi Start `{ taskId }` | Cả hai đều **409** |
| 5 | Stop S1 `Completed` sau 25 phút | `sessionMinutes = 25`, `taskTotalMinutes = 35` (cộng cả phiên task-only 10 phút) |
| 6 | Sau Stop `Completed` của S1 | Subtask S1 `Done` (tự động), task `InProgress` (S2 còn `ToDo`). Stop `Completed` của S2 thì task tự `Done` |
| 7 | Start với subtask của **task khác**, hoặc subtask **không tồn tại** | Đều **400** |
| 8 | Slot của S1 + Start `{ taskId, subTaskId = S1, scheduleSlotId }` | 201, `plannedMinutes` = độ dài slot, phiên lưu S1. Stop `Completed`: slot của S1 `Done`, **slot của S2 vẫn `Approved`** |
| 9 | Slot của S2, Start **không** gửi `subTaskId` | 201 nhưng phiên **không có subtask** |
| 10 | Slot của S2, Start gửi `subTaskId = S1` (lệch) | **201** (không báo lỗi) |
| 11 | `time-summary` | Mỗi phiên có `sessionId`, **không có** `subTaskId` |
| 12 | `GET /api/calendar`, item slot của S1 | Có `subTaskId`, `subTaskTitle`, `status` |
| 13 | `self-report` gửi kèm `subTaskId` | 201, nhưng phiên **không có subtask** |

> Bảng trên ghi lại lần kiểm tra trước khi có luồng trạng thái liền mạch; riêng dòng 6 đã cập nhật. Các hành vi mới được kiểm bằng `METANOIA.Test/StatusWorkflowTests.cs` và trang `/focus-test.html` (mục 8: kịch bản tự động).

---

## 12. Luồng màn hình gợi ý

**A. Khi mở app (và khi tab được focus lại)**
1. `GET /api/focus/current`: 200 thì hiện thanh phiên đang chạy với đồng hồ.
2. `GET /api/focus/pending-confirmation`: có phần tử thì hiện hộp thoại xác nhận.
3. `GET /api/notifications?unreadOnly=true`: huy hiệu số chưa đọc.
4. Kết nối SignalR.

**B. Màn lịch** (`GET /api/calendar?from=…&to=…`)
Item `source = "Slot"` có `id` (chính là `scheduleSlotId`), `status`, `taskId`, `title` (tên task), `subTaskId`, `subTaskTitle`, `proposedMinutes`, `start`, `end`. Hiển thị tên subtask nếu có. Dựa vào `status` (mục 4) để quyết định nút. Item `source = "Google"` chỉ để xem, không có nút Start.

**C. Bấm Start** (xem bảng "Người dùng bấm Start ở đâu" ở mục 7)
`POST /api/focus/start` với body đúng loại. Xử lý `warning`, vẽ đồng hồ (mục 9). Gặp 409 thì chuyển sang phiên đang chạy. **Tự lưu `subTaskId` đã gửi** cùng `sessionId` để hiển thị.

**D. Bấm Stop hoặc "Tạm dừng"**
Hộp thoại chọn lý do và ghi chú, rồi `POST /{id}/stop`. Dựa vào `checkInRequired` để hỏi thêm hay không. Sau đó refetch lịch để thấy trạng thái slot mới.

**E. Khi người dùng nói "xong hẳn"**
Stop `Completed` trên phiên của **subtask** đã tự đánh dấu subtask `Done`; subtask cuối xong thì **task tự `Done`**. Với task **không có subtask**, Stop `Completed` chỉ đóng slot nên cần hỏi "Đánh dấu task này là xong?" rồi gọi `PUT /api/tasks/{id}` (`Done`). Sau mỗi thao tác hãy tải lại task, subtask, slot.

**F. Quên bấm Start**
Nút "Thêm thủ công": chọn task, giờ bắt đầu và kết thúc trong quá khứ, `self-report`. (Chưa gắn được subtask.)

**G. Chi tiết slot**
`GET /api/focus/{slotId}/time-summary`: dự kiến so với thực tế và danh sách các phiên.

---

## 13. Việc cần thử khi build

- Start task, rồi Start subtask khi chưa Stop: phải nhận 409 và hiện phiên đang chạy, không báo lỗi đỏ.
- Mở hai tab, Start ở tab 1 rồi tab 2: tab 2 nhận 409.
- Đóng tab giữa chừng rồi mở lại: đồng hồ chạy tiếp đúng giờ (nhờ `/current`).
- Đổi giờ máy: đồng hồ không được nhảy.
- Bấm Stop hai lần: lần hai trả 409, không báo lỗi đỏ.
- Slot `Proposed` bấm Start: 400, nên ẩn hoặc vô hiệu nút.
- Slot gắn subtask: Start phải gửi kèm `subTaskId` của slot. Kiểm tra bằng cách xem phiên có nhãn subtask ở UI của bạn.
- Stop `Completed` một subtask rồi `GET` subtask: phải `Done`; subtask cuối xong thì `GET` task phải `Done`.
- Stop `Blocked` hoặc `Other` không điền ghi chú: phải nhận 400 và phiên vẫn chạy.
- Tải lại trang giữa lúc đang làm subtask: lấy `subTaskId` từ `GET /api/focus/task/{taskId}/sessions` (phiên `Running`).
- Tạo slot `Approved` bắt đầu sau 1 phút, dài 3 phút để thấy chuỗi `Due`, `Overrun`, tự ngắt (khoảng 6 phút).

---

## 14. Chưa có (FE đừng chờ)

- **Tổng thời gian theo từng subtask** (hiện chỉ theo task, và theo slot).
- `subTaskId` trong `GET /current`, `time-summary`, `pending-confirmation`; `self-report` chưa nhận `subTaskId`.
- Backend không tự lấy `subTaskId` từ slot khi Start, và không kiểm tra slot với subtask có khớp nhau không.
- API **check-in** (lưu câu trả lời "thấy thế nào"): `checkInRequired` chỉ là gợi ý hiển thị.
- API đổi nhanh trạng thái task hoặc subtask (dùng `PUT` đủ trường).
- `GET /api/notifications` chưa có phân trang theo trang, chỉ có `take`.
- Thông báo nhắc tên subtask (hiện chỉ nhắc tên task).
