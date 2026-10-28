# Focus session: hướng dẫn chạy, test và hiểu luồng

Tài liệu cho FE nằm ở [FOCUS_SESSION_FE.md](FOCUS_SESSION_FE.md). Tài liệu này giải thích tính năng bấm giờ làm việc (Start / Stop), nhắc việc bằng Hangfire và thông báo,
kèm trình tự chạy thử bằng trang `focus-test.html`. Mọi API cần JWT (`Authorization: Bearer <accessToken>`).

## 1. Ý tưởng

Hệ thống học xem người dùng hay ước lượng lệch bao nhiêu bằng cách so **thời gian ước lượng** (`T_est`) với
**thời gian làm thật** (`T_act`). Các API Start/Stop tạo ra `T_act`, nên chất lượng dữ liệu ở đây quyết định việc học có đúng không.

Ba nguyên tắc:

1. **Không tự bấm giờ.** Đến giờ của slot, hệ thống chỉ đổi trạng thái và nhắc "bấm Start". Giờ đồng hồ trôi qua không chứng minh người dùng đang làm.
2. **Không có Pause/Resume.** Tạm dừng là Stop với lý do `Interrupted`, quay lại thì Start phiên mới. Một slot có nhiều phiên; `T_act` = tổng `EndedAt − StartedAt` của các phiên.
3. **Mọi thời gian tính theo giờ server.** FE không gửi giờ nào (trừ `self-report`), đồng hồ vẽ từ `elapsedSeconds` và `serverTime`.

Độ tin cậy của một phiên (`captureMethod`), dùng làm trọng số khi học:

| `captureMethod` | Là gì | Trọng số học |
|---|---|---|
| `Timer` | Người dùng tự bấm Start và Stop | 0.30 |
| `AutoTimeout` đã xác nhận | Hệ thống ép dừng, người dùng xác nhận | 0.15 |
| `SelfReported` | Người dùng tự khai báo giờ | 0.10 |
| `AutoTimeout` chưa xác nhận | Hệ thống ép dừng, chưa ai xác nhận | loại, không dùng để học |

Backend chỉ **ghi** `captureMethod` và `isConfirmed`; việc tính trọng số và cập nhật `Kbias` do dịch vụ AI làm khi task chuyển sang `Done`.

## 2. Đường đi của một request

```
FocusSessionController  (/api/focus/..., giữ cả /api/FocusSession/... cũ)
   └─ FocusSessionService          luật nghiệp vụ
        ├─ FocusSessionRepository / ScheduleSlotRepository   truy vấn
        ├─ ScheduleJobService      hẹn và hủy job Hangfire
        └─ UnitOfWork.SaveChangesAsync

Hangfire (nền)  ─▶  SlotBackgroundJobs  ─▶  NotificationService
                                              ├─ ghi bảng Notification        (hộp thư)
                                              ├─ SignalR  /hubs/notifications (realtime khi đang mở app)
                                              └─ Web Push                     (kể cả khi đóng tab)
```

Ba bảng mới và vai trò:

| Bảng | Vai trò |
|---|---|
| `HangfireJobTrackers` | Sổ ghi "slot nào đang có job nào, `HangfireJobId` là gì" để hủy đúng job khi slot đổi giờ hoặc bị hủy. Duy nhất `(ScheduleSlotId, JobType)` |
| `UserPushSubscriptions` | Địa chỉ gửi Web Push của từng trình duyệt (`Endpoint`, `P256dh`, `Auth`). Một người có nhiều dòng |
| `Notification` | Hộp thư: mọi thông báo đều được lưu trước rồi mới đẩy qua SignalR và Web Push |

## 3. API

### Focus

| API | Việc làm |
|---|---|
| `POST /api/focus/start` | Body `{ taskId, subTaskId?, scheduleSlotId?, focusTrackId? }`. Đang có phiên chạy thì **409** kèm phiên đó (`details`). Tạo phiên `Timer`. Gắn slot thì slot thành `InProgress`. Bắt đầu sớm hơn 2 giờ thì có `warning: "STARTED_VERY_EARLY"`. Trả 201 |
| `POST /api/focus/{sessionId}/stop` | Body `{ stopReason, note? }` (`Completed`, `Interrupted`, `Blocked`, `Other`). Trả `sessionMinutes`, `taskTotalMinutes`, `checkInRequired`. Dừng lần hai trả **409**. Người khác trả **403** |
| `GET /api/focus/current` | Phiên đang chạy: `elapsedSeconds`, `serverTime`, `isOverrun`. Không có thì **204** |
| `GET /api/focus/pending-confirmation` | Các phiên bị hệ thống tự ngắt (AutoTimeout) chưa xác nhận hoặc loại bỏ, để FE biết `sessionId` cần gọi `/confirm` |
| `POST /api/focus/{sessionId}/confirm` | Body `{ confirmed, correctedMinutes? }`. Chỉ cho phiên `AutoTimeout` chưa xác nhận |
| `POST /api/focus/self-report` | Body `{ taskId, scheduleSlotId?, startedAt, endedAt, stopReason, note? }`. Cho người quên bấm Start |
| `GET /api/focus/{slotId}/time-summary` | Cũng có ở `GET /api/slots/{slotId}/time-summary`. Trả `plannedMinutes`, `actualMinutes`, `sessionCount`, danh sách phiên |

### Thông báo

Loại thông báo: SignalR và Web Push nhận loại **chi tiết** (`START_PROMPT`, `PRE_START_REMINDER`, `NOT_STARTED`, `NEAR_END`, `OVERRUN`, `AUTO_TIMEOUT`, `CHECK_IN`, `MISSED_SLOT`). Bảng `Notification` có CHECK nên chỉ lưu nhóm lớn: `SlotReminder` (mọi nhắc việc) hoặc `MissedSlot` (bỏ lỡ slot). `GET /api/notifications` vì thế trả `type` là nhóm lớn; muốn phân biệt trong hộp thư hãy dựa vào `title`.

| API | Việc làm |
|---|---|
| `GET /api/notifications?unreadOnly=&take=` | Hộp thư, mới nhất trước (tối đa 100) |
| `POST /api/notifications/{id}/read` | Đánh dấu đã đọc (204) |
| `POST /api/notifications/read-all` | Đánh dấu hết, trả `{ updated }` |
| `GET /api/notifications/vapid-public-key` | Khóa công khai để trình duyệt đăng ký Web Push |
| `POST /api/notifications/subscribe`, `/unsubscribe` | Lưu hoặc xóa địa chỉ Web Push của trình duyệt |
| SignalR `/hubs/notifications` | Sự kiện `ReceiveNotification(message, actionType, notificationId)`. Gửi JWT bằng `accessTokenFactory` (server đọc `access_token` trên query) |

## 4. Trạng thái slot và các job nền

```
Approved ──(đến giờ)──▶ Due ──(bấm Start)──▶ InProgress ──(Stop: Completed)──▶ Done
                         │  ▲                     │  │
   (hết giờ, chưa Start) │  └─(Stop: Interrupted, Blocked, Other)
                         ▼                        │  └─(hết giờ dự kiến mà còn chạy)─▶ Overrun ─(2× dự kiến)─▶ AutoTimeout ─▶ Done
                       Missed                     └─ Stop sớm vẫn được
```

Job được hẹn khi slot ở trạng thái **Approved** (tạo slot Approved, sửa slot sang Approved, hoặc duyệt đề xuất AI). Mỗi job tự kiểm tra slot còn đúng trạng thái rồi mới làm:

| Job | Giờ chạy | Điều kiện | Việc làm |
|---|---|---|---|
| `PreStartReminder` | Start − 15 phút | `Approved` | Gửi nhắc |
| `SlotDue` | Start | `Approved` | Chuyển `Due`, nhắc bấm Start |
| `NotStartedNudge` | Start + 20 phút | `Due` và chưa phiên nào | Nhắc lại |
| `NearEndReminder` | End − 10 phút | `InProgress` | Nhắc sắp hết giờ |
| `SlotEnded` | End | tùy trạng thái | `InProgress` → `Overrun`. `Due` chưa có phiên nào → `Missed`. `Due` đã có phiên → gửi `CHECK_IN` |
| `OverrunReminder` | End + 15 phút | `Overrun` và còn phiên chạy | Nhắc bấm Stop |
| `CheckInRetry` | End + 30 phút | | Chưa làm (chưa có nơi lưu câu trả lời check-in) |
| `AutoTimeout` | Start + 2 × dự kiến | `InProgress` hoặc `Overrun`, còn phiên chạy | Ép dừng, `AutoTimeout`, `IsConfirmed = false`, slot `Done` |
| `MarkUnknown` | End + 24 giờ | | Chỉ ghi log (phiên chưa xác nhận đã tự bị loại khỏi việc học) |

**Bấm Start** hẹn lại các job phía cuối (`NearEnd`, `SlotEnded`, `Overrun`, `CheckInRetry`, `AutoTimeout`, `MarkUnknown`) tính từ **giờ bắt đầu thực tế**, không sửa giờ trên lịch của slot.
**Đổi giờ slot** (kéo thả, đồng bộ Google, lập lại lịch) hủy mọi job cũ và hẹn lại theo giờ mới. **Hủy slot** hủy hết job.

## 5. Trình tự chạy thử với `focus-test.html`

Chuẩn bị:

1. Chạy các câu lệnh trong `docs/sql/003_focus_session.sql`, theo từng bước trong file (bước 1 chỉ đọc, bước 2 chỉ chạy nếu cần).
2. `appsettings.json` có `VapidSettings` (`Subject`, `PublicKey`, `PrivateKey`) nếu muốn thử Web Push. Tạo khóa bằng `npx web-push generate-vapid-keys`. `Subject` dạng `mailto:ban@example.com`.
3. Chạy backend bằng profile **https** của Visual Studio (Development). Lần đầu Hangfire tự tạo schema `hangfire` trong database.
4. Mở `https://localhost:7007/focus-test.html`. Trang chỉ có khi chạy Development hoặc `Swagger:Enabled = true`.

Các bước:

| Bước | Làm gì | Kết quả mong đợi |
|---|---|---|
| 1 | Mục 1: dán Google Client ID, đăng nhập (hoặc dán `accessToken`) | Hiện "Đã đăng nhập", trang tự tải dự án, domain, hộp thư |
| 2 | Mục 5: **Kết nối SignalR**, **Cho phép thông báo trình duyệt** (tùy chọn: **Đăng ký Web Push**) | "SignalR: đã kết nối" |
| 3 | Mục 2: chọn dự án (hoặc tạo "Focus test"), chọn Domain, giữ mặc định (bắt đầu sau 1 phút, dài 3 phút, Approved), bấm **Tạo task + slot thử** | Slot `Approved`, các job đã được hẹn |
| 4 | Chờ khoảng 1 phút | Slot thành `Due`, thông báo "Đến giờ rồi!" xuất hiện (SignalR, hộp thư, Web Push) |
| 5 | Mục 3: bấm **Start (gắn slot đã chọn)** | Slot `InProgress`, đồng hồ chạy |
| 6a | Bấm **Stop** với `Interrupted`, chờ rồi Start lại, rồi Stop với `Completed` | Mỗi lần có `sessionMinutes` và `taskTotalMinutes` cộng dồn. Slot về `Due` rồi `Done` |
| 6b | Hoặc không bấm Stop, chờ hết 3 phút | Slot thành `Overrun` (đồng hồ đỏ) |
| 6c | Chờ tới 6 phút sau khi Start | Hệ thống tự ngắt, thông báo "Phiên bị tự ngắt", slot `Done` |
| 7 | Mục 6: dán Session Id (lấy ở khung kết quả khi Start), nhập số phút thực tế, bấm **Xác nhận** | Phiên thành `SelfReported`, được tính vào tổng kết |
| 8 | Mục 4: **Tổng kết slot** | `actualMinutes` là tổng các phiên đáng tin |

Hangfire quét job hẹn giờ mỗi 5 giây nên thông báo có thể trễ vài giây.
Dashboard `/hangfire` chỉ mở được khi truy cập từ chính máy chạy server.

## 6. Gặp lỗi thì xem

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| Bấm Start trả 500 | CHECK của `ScheduleSlot.Status` chưa có `InProgress`: chạy bước 2 của `003_focus_session.sql` |
| Không có thông báo nào, slot không tự sang `Due` | Slot đang `Proposed` (chưa duyệt) nên không có job. Hoặc Hangfire chưa chạy: xem log lúc khởi động, hoặc `Hangfire:Enabled = false` |
| Server không khởi động, lỗi kết nối Hangfire | Connection string Supabase: dùng cổng session (5432), không dùng transaction pooler (6543) |
| Lỗi `max clients reached` | Giảm `Hangfire:WorkerCount` (mặc định 2) |
| SignalR báo 401 | Chưa đăng nhập hoặc token hết hạn (60 phút) |
| FE khác domain: SignalR lỗi CORS | Thêm domain FE vào `Frontend:AllowedOrigins`. Backend đã bật `AllowCredentials` cho các origin này |
| Đăng ký Web Push lỗi | Chỉ chạy trên https hoặc localhost; VAPID public key phải khớp private key |
| Hộp thư rỗng, job `SlotDue` Failed với lỗi `Notification_Type_check` | Cột `Type` chỉ nhận `SlotReminder`, `MissedSlot`, `DeadlineRisk`. Code đã map về các giá trị này; nếu vẫn lỗi thì bạn đang chạy bản code cũ |
| Job được hẹn nhưng không chạy, hoặc thông báo không tới trình duyệt | Máy khác (server Azure, máy đồng đội) cùng dùng schema Hangfire và lấy mất job. Mở `/hangfire`, tab Servers: chỉ nên thấy server của bạn. Đặt `Hangfire:SchemaName` riêng cho máy local |

## 7. Cấu hình

| Key | Mặc định | Ý nghĩa |
|---|---|---|
| `Hangfire:Enabled` | `true` | `false` thì không dựng Hangfire, không hẹn job (dùng cho test) |
| `Hangfire:WorkerCount` | `2` | Số worker, mỗi worker giữ một kết nối Postgres |
| `Hangfire:SchemaName` | `hangfire` | Schema Postgres chứa job. **Mọi server cùng schema thì chia nhau job.** Máy local dùng chung database với server deploy nên đặt tên khác (ví dụ `hangfire_dev`). Hangfire tự tạo schema mới ở lần chạy đầu |
| `VapidSettings:Subject`, `PublicKey`, `PrivateKey` | trống | Thiếu thì bỏ qua Web Push, SignalR và hộp thư vẫn chạy |

## 8. Chưa làm

- Check-in: `CheckInRetry` chưa làm gì vì chưa có nơi lưu câu trả lời.
- Hai lần Start cùng lúc từ hai máy có thể cùng tạo phiên (không có ràng buộc duy nhất ở database); trường hợp bình thường được chặn ở service.
- `confirm` với `confirmed = false` đánh dấu phiên bị loại bằng ghi chú trong `Note` (bảng `FocusSession` chưa có cột riêng).
