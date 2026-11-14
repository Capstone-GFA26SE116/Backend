# Duyệt lịch (FR-11): sửa, từ chối từng slot và kiểm Google Calendar (cho Frontend)

Sau khi AI xếp lịch, người dùng xem một **đề xuất** (`ScheduleProposal` trạng thái `Pending`) gồm nhiều **slot** trạng thái `Proposed`. Trước khi duyệt, người dùng có thể **sửa giờ** hoặc **từ chối từng slot**, và hệ thống **kiểm lại Google Calendar** lúc duyệt. Mọi API dưới đây cần đăng nhập và chỉ thao tác trên đề xuất của chính người dùng (đề xuất của người khác: `404`).

## 1. Luồng màn hình gợi ý

1. `GET /api/schedule/proposals/pending` lấy đề xuất đang chờ (`204` nếu không có). Chỉ trả các slot còn `Proposed`.
2. (Tùy chọn) `GET /api/schedule/proposals/{id}/conflicts` để gắn cảnh báo "trùng lịch Google" lên từng slot.
3. Người dùng **kéo thả hoặc đổi giờ** một slot: `PUT .../slots/{slotId}`. Phản hồi cho biết ngay giờ mới có trùng Google không.
4. Người dùng **bỏ** một slot: `POST .../slots/{slotId}/reject`. Slot biến khỏi danh sách.
5. Bấm **Duyệt**: `POST /api/schedule/proposals/{id}/approve`.
   - Có slot trùng Google: nhận `409` kèm danh sách. Hiện hộp thoại: sửa giờ / từ chối các slot đó rồi duyệt lại, hoặc **"Bỏ các slot trùng và duyệt phần còn lại"** (gọi lại với `?skipConflicts=true`).
   - Không trùng: `200`, các slot `Approved`.

## 2. API

### Sửa giờ một slot
`PUT /api/schedule/proposals/{proposalId}/slots/{slotId}`
```json
{ "startTime": "2026-11-20T03:30:00Z", "endTime": "2026-11-20T04:30:00Z" }
```
**200:**
```json
{
  "slot": { "id": "…", "taskId": "…", "startTime": "2026-11-20T03:30:00+00:00", "endTime": "2026-11-20T04:30:00+00:00",
            "proposedMinutes": 60, "status": "Proposed", "isManualOverride": true, "originalStartTime": "2026-11-20T03:00:00+00:00" },
  "googleConflicts": [ { "slotId": "…", "taskId": "…", "taskTitle": "Viết báo cáo", "start": "…", "end": "…",
                         "event": { "title": "Họp khách", "start": "…", "end": "…" } } ]
}
```
- `googleConflicts` rỗng nếu không trùng hoặc chưa kết nối Google. Chỉ so với sự kiện Google **đã được lưu** (không gọi Google), để phản hồi nhanh khi kéo thả.
- Số phút của slot (`proposedMinutes`) được tính lại theo giờ mới. Việc sửa **không** duyệt, không hẹn nhắc việc và không đổi trạng thái task.
- Lỗi `400`: kết thúc không sau bắt đầu; giờ bắt đầu ở quá khứ; slot không còn `Proposed` (đã từ chối hoặc đã duyệt); đề xuất không còn `Pending` (ví dụ đã `Superseded` vì người dùng xếp lại, hoặc đã `Approved`). `404`: không phải của bạn, hoặc slot không thuộc đề xuất này.

### Từ chối một slot
`POST /api/schedule/proposals/{proposalId}/slots/{slotId}/reject` (không có body)
- **200**: slot trả về với `status = "Cancelled"`. Slot này sẽ **không được ghi** khi duyệt và biến khỏi `GET /proposals/pending`.
- Task không đổi trạng thái. Từ chối lần hai hoặc slot không còn `Proposed`: `400`.
- Từ chối hết slot: đề xuất vẫn `Pending` (người dùng còn có thể xếp lại), nhưng **duyệt khi không còn slot nào** trả `400`.

### Xem trước xung đột Google
`GET /api/schedule/proposals/{proposalId}/conflicts`
```json
{ "googleCheck": "Cached", "conflicts": [ { "slotId": "…", "taskId": "…", "taskTitle": "…", "start": "…", "end": "…", "event": { "title": "…", "start": "…", "end": "…" } } ] }
```
`googleCheck`: `NotConnected` (chưa kết nối Google, không kiểm) hoặc `Cached` (kiểm theo dữ liệu Google đã lưu, chưa đồng bộ lại lúc này). Chỉ đọc.

### Duyệt
`POST /api/schedule/proposals/{proposalId}/approve?skipConflicts=false`

Mặc định `skipConflicts=false` (hành vi cũ khi không có xung đột). Khi duyệt, backend **đồng bộ lại Google Calendar ngay** rồi mới kiểm.

**200** (`ScheduleProposalDto` như cũ, thêm hai trường):
```json
{ "id": "…", "status": "Approved", "slots": [ … ], "googleCheck": "Checked", "skippedConflicts": [] }
```
- `googleCheck`: `Checked` (đã đồng bộ rồi kiểm), `Stale` (đồng bộ Google lỗi nên kiểm theo dữ liệu đã lưu; vẫn duyệt được, có thể hiện cảnh báo "chưa kiểm được lịch Google mới nhất"), `NotConnected`.
- `skippedConflicts`: các slot bị bỏ vì trùng (chỉ khác rỗng khi gọi với `skipConflicts=true`).

**409** khi có slot trùng và `skipConflicts=false` (**không đổi gì**):
```json
{ "message": "1 slot trùng với sự kiện trên Google Calendar. ...", "code": "SCHEDULE_CONFLICT",
  "details": { "conflicts": [ { "slotId": "…", "taskId": "…", "taskTitle": "Viết báo cáo", "start": "…", "end": "…",
                                "event": { "title": "Họp khách", "start": "…", "end": "…" } } ] } }
```
**`skipConflicts=true`:** các slot trùng chuyển `Cancelled` (không ghi), các slot còn lại `Approved` như bình thường (hẹn nhắc việc, task lên `Scheduled`). Nếu **mọi** slot đều trùng thì `400` (không còn gì để duyệt).

Lỗi khác: `400` đề xuất không còn `Pending`, hoặc không còn slot nào để duyệt; `404` không tìm thấy.

## 3. "Trùng Google" nghĩa là gì
- Giờ slot **giao nhau** với một sự kiện Google (chạm đầu mút không tính: sự kiện kết thúc đúng lúc slot bắt đầu thì không trùng).
- **Không tính:** sự kiện cả ngày, sự kiện đã hủy, và sự kiện do METANOIA tự đẩy lên Google (để slot không "trùng" với chính nó).
- Các luật khác (ngoài giờ làm, đè giờ nghỉ trưa, trùng task Fixed, sau deadline) **không** nằm ở các API này. Nhóm AI dự kiến cung cấp một endpoint chỉ đọc để kiểm; khi có, BE sẽ chuyển tiếp để FE chỉ gọi một nơi.

## 4. Lưu ý
- Mỗi lần người dùng bấm "Xếp lịch" lại, đề xuất `Pending` cũ chuyển sang `Superseded` và **những chỗ đã sửa hoặc từ chối sẽ mất**. FE nên hỏi lại người dùng trước khi xếp lại. Sau khi xếp lại, mọi API sửa, từ chối, duyệt trên đề xuất cũ trả `400`.
- `PUT /api/schedule-slots/{id}` và `DELETE /api/schedule-slots/{id}` cũ vẫn dùng được cho slot bất kỳ, nhưng không kiểm đề xuất còn `Pending`. Với màn hình duyệt lịch nên dùng các API ở tài liệu này.
- Sau khi duyệt hãy tải lại lịch (`GET /api/calendar`) vì trạng thái slot và task đã đổi.
- Nếu người dùng chưa kết nối Google Calendar thì việc kiểm bị bỏ qua (`googleCheck = "NotConnected"`), duyệt như bình thường.

## 5. Việc cần thử khi build
| Làm | Kết quả đúng |
|---|---|
| Sửa giờ slot sang giờ khác | `200`, `proposedMinutes` đổi, `isManualOverride = true` |
| Sửa slot đã từ chối | `400` |
| Từ chối một slot rồi duyệt | Slot đó vẫn `Cancelled`, các slot còn lại `Approved` |
| Tạo sự kiện Google trùng một slot rồi duyệt | `409`, danh sách có đúng slot đó, slot vẫn `Proposed` |
| Duyệt lại với `?skipConflicts=true` | `200`, slot trùng `Cancelled`, `skippedConflicts` có slot đó |
| Duyệt khi chưa kết nối Google | `200`, `googleCheck = "NotConnected"` |
| Xếp lịch lại rồi sửa slot của đề xuất cũ | `400` (đề xuất đã `Superseded`) |
