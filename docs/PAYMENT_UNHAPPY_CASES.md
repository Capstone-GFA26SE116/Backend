# Thanh toán PayOS: các tình huống lỗi, cách test và cách lấy key

Tài liệu này mô tả những gì đã code cho luồng thanh toán PayOS (ngoài CRUD `PaymentTransaction`),
cách test từng bước, và cách lấy các key cần điền vào `appsettings.json`.

- [1. Chạy SQL tạo bảng hoàn tiền](#1-chạy-sql-tạo-bảng-hoàn-tiền-bắt-buộc)
- [2. Lấy key PayOS và điền appsettings](#2-lấy-key-payos-và-điền-appsettings)
- [3. Tiền nằm ở đâu khi có sự cố](#3-tiền-nằm-ở-đâu-khi-có-sự-cố)
- [4. Danh sách tình huống đã xử lý](#4-danh-sách-tình-huống-đã-xử-lý)
- [5. Hoàn tiền](#5-hoàn-tiền)
- [6. Trình tự test](#6-trình-tự-test)
- [7. Những điều chưa được xác minh](#7-những-điều-chưa-được-xác-minh-với-payos-thật)

---

> **Cập nhật: hoàn tiền đang TẮT mặc định** (`Payments:RefundsEnabled = false`). Khi tắt, luồng thanh toán (checkout, webhook, sync,
> đối soát, tạo gói và hóa đơn) **không cần bảng `PaymentRefund`**, nên có thể bỏ qua mục 1 và mục 5 cho đến khi làm hoàn tiền.
> Lúc đó, tiền về sai số tiền hoặc sau khi đã hủy sẽ **không** tự mở yêu cầu hoàn tiền: hệ thống ghi log lỗi và webhook trả
> `ReviewRequired`. Admin tìm các giao dịch này bằng cách lọc `GET /api/payment-transactions` có `webhookReceivedAt` nhưng trạng thái
> không phải `Succeeded`, rồi hoàn tiền thủ công ngoài hệ thống. Các API `/api/refunds` và `POST /api/payments/{id}/refunds` trả 400
> "Chức năng hoàn tiền chưa được bật" cho đến khi bạn chạy SQL ở mục 1 và đặt `RefundsEnabled = true`.

## 1. Chạy SQL tạo bảng hoàn tiền (chỉ khi bật hoàn tiền)

PayOS **không có API hoàn tiền cho merchant** (tài liệu API không nhắc tới), và `PaymentTransaction.Status`
bị `CHECK` chỉ cho 5 giá trị nên không thêm được `Refunded`. Vì vậy hoàn tiền được lưu ở bảng mới `PaymentRefund`.

1. Mở Supabase → **SQL Editor**.
2. Dán nội dung [docs/sql/001_payment_refund.sql](sql/001_payment_refund.sql) → **Run**.
3. Chưa chạy bước này thì các API hoàn tiền, xoá giao dịch và các luồng cần mở yêu cầu hoàn tiền sẽ lỗi 500.

Tài khoản **Admin** không tự có (người đăng nhập Google mới đều là `Freelancer`). Gán bằng SQL:

```sql
UPDATE "User"
SET "RoleId" = (SELECT "Id" FROM "Role" WHERE "Name" = 'Admin')
WHERE "Email" = 'email-cua-ban@gmail.com';
```

Sau đó đăng nhập lại để lấy token mới có role Admin.

---

## 2. Lấy key PayOS và điền appsettings

### 2.1 Lấy 3 key

1. Vào **https://my.payos.vn**, đăng ký / đăng nhập và hoàn tất xác thực danh tính (cá nhân hoặc doanh nghiệp).
2. Tạo một **Kênh thanh toán** và liên kết tài khoản ngân hàng nhận tiền.
3. Mở kênh đó, vào phần thông tin tích hợp (API keys) và copy:

| Khoá trong `appsettings.json` | Lấy ở PayOS | Ghi chú |
|---|---|---|
| `PayOS:ClientId` | Client ID | Gửi kèm mỗi request (header `x-client-id`) |
| `PayOS:ApiKey` | API Key | Gửi kèm mỗi request (header `x-api-key`) |
| `PayOS:ChecksumKey` | Checksum Key | **Bí mật nhất**: dùng ký request và xác thực webhook |

Tên nhãn trên giao diện PayOS có thể khác chút; ba giá trị này luôn nằm trong trang cài đặt của kênh thanh toán.

### 2.2 Điền appsettings.json

File `METANOIA/METANOIA/appsettings.json` (đã được git bỏ qua) đã có sẵn khung, bạn chỉ thay các giá trị `YOUR_...`:

```json
"PayOS": {
  "ClientId": "<Client ID>",
  "ApiKey": "<API Key>",
  "ChecksumKey": "<Checksum Key>",
  "ReturnUrl": "http://localhost:5173/payment/success",
  "CancelUrl": "http://localhost:5173/payment/cancel",
  "WebhookUrl": "https://<tên-miền-công-khai>/api/payos/webhook",
  "Description": "METANOIA"
},
"Payments": {
  "LinkExpiryMinutes": 15,
  "ExpiryGraceSeconds": 60,
  "RefundWindowDays": 7,
  "ReconciliationEnabled": true,
  "ReconciliationIntervalSeconds": 60,
  "PendingStaleAfterSeconds": 120
}
```

| Khoá | Ý nghĩa |
|---|---|
| `ReturnUrl` / `CancelUrl` | Trang **frontend** PayOS đưa khách về sau khi trả tiền / bấm huỷ. Frontend phải gọi `POST /api/payments/{id}/sync` ở trang này (xem mục 4, case 6) |
| `WebhookUrl` | URL **công khai** PayOS gọi khi có tiền về. `localhost` không dùng được (xem 2.3) |
| `Description` | Nội dung chuyển khoản. PayOS giới hạn độ dài (tài liệu ghi tối đa 9 ký tự với tài khoản ngân hàng chưa liên kết qua payOS), nên giữ ngắn |
| `LinkExpiryMinutes` | Link thanh toán sống bao lâu |
| `RefundWindowDays` | Khách tự yêu cầu hoàn tiền được trong bao nhiêu ngày |
| `ReconciliationEnabled` | Bật/tắt worker đối soát nền |
| `PendingStaleAfterSeconds` | Đơn Pending cũ hơn mức này mới bị worker hỏi lại PayOS |

Nếu còn giá trị `YOUR_...`, API thanh toán trả **503** với thông báo "Cổng thanh toán PayOS chưa được cấu hình"
(không crash). File mẫu cho người khác: `appsettings.example.json`.

> Sau khi bạn `git pull` sang nhánh có commit bỏ theo dõi `appsettings.json`, git **xoá file này khỏi máy**.
> Việc này đã xảy ra với bạn: tôi khôi phục từ lịch sử git (connection string và Google ClientId) và tạo `Jwt:Key` mới.
> **Google `ClientSecret` cũ không còn trong lịch sử**, bạn cần copy lại từ Google Cloud Console →
> APIs & Services → Credentials → OAuth client, thay vào `Google:ClientSecret`.

### 2.3 Cho PayOS gọi được webhook khi chạy máy local

PayOS cần một URL HTTPS công khai. Chọn một trong hai:

**ngrok** (https://ngrok.com, tạo tài khoản miễn phí và `ngrok config add-authtoken ...`):

```
ngrok http https://localhost:7007
```

**cloudflared** (không cần tài khoản):

```
cloudflared tunnel --url https://localhost:7007 --no-tls-verify
```

Lệnh in ra URL dạng `https://abc123.ngrok-free.app`. Khi đó:

1. Đặt `PayOS:WebhookUrl` = `https://abc123.ngrok-free.app/api/payos/webhook`.
2. Chạy backend, đăng nhập Admin, gọi `POST /api/payos/confirm-webhook` (Swagger). Hoặc dán URL này vào kênh thanh toán trên my.payos.vn.
3. PayOS sẽ gửi thử một webhook; endpoint trả 200 nên đăng ký thành công.

URL ngrok miễn phí đổi mỗi lần chạy lại, nên mỗi lần phải đăng ký lại.

---

## 3. Tiền nằm ở đâu khi có sự cố

PayOS là dịch vụ đối soát VietQR: khách chuyển khoản qua ngân hàng vào **tài khoản ngân hàng bạn đã liên kết với
kênh thanh toán**; PayOS chỉ nhận biết giao dịch rồi báo cho hệ thống bạn qua webhook. Vì vậy (xác nhận lại trong
dashboard PayOS của bạn):

| Tình huống | Tiền nằm ở đâu | Hệ thống làm gì |
|---|---|---|
| Khách chưa chuyển | Còn ở khách | Đơn `Pending`, hết hạn thì `Expired` |
| Khách chuyển xong nhưng **mạng khách đứt**, không về trang return | **Đã vào tài khoản ngân hàng của bạn** | Webhook vẫn tới server và kích hoạt gói; nếu webhook cũng lỡ, worker đối soát hỏi PayOS và kích hoạt |
| **Server bạn tắt / mạng yếu** lúc PayOS gọi webhook | Đã vào tài khoản ngân hàng của bạn | Đơn vẫn `Pending` đến khi server bật lại, worker hỏi PayOS thấy `PAID` và kích hoạt |
| Tiền về nhưng đơn đã huỷ / sai số tiền | Đã vào tài khoản ngân hàng của bạn | Hệ thống tự mở yêu cầu hoàn tiền `Source = System` cho admin xử lý |

Tức là tiền không "mất" ở đâu: nó luôn ở tài khoản ngân hàng của bạn, vấn đề chỉ là **trạng thái trong database
có khớp hay không**. Các cơ chế bên dưới đảm bảo chúng khớp lại.

---

## 4. Danh sách tình huống đã xử lý

Tất cả đều có test tự động trong [PaymentFlowTests.cs](../METANOIA/METANOIA.Test/PaymentFlowTests.cs) và
[RefundTests.cs](../METANOIA/METANOIA.Test/RefundTests.cs) (dùng PayOS giả, không gọi mạng thật).

### Tạo link thanh toán (`POST /api/payments/checkout`)

| # | Tình huống | Cách xử lý |
|---|---|---|
| 1 | **PayOS lỗi mạng / timeout / 5xx / 429** khi tạo link | Đơn đã lưu `Pending` được chuyển `Failed`, trả **503** `PAYMENT_GATEWAY_UNAVAILABLE`. Khách chưa nhận link nên không trả tiền được; bấm lại sẽ tạo đơn mới. Không có đơn `Pending` treo |
| 2 | **Bấm thanh toán 2 lần / tải lại trang** | Hệ thống hỏi PayOS về đơn `Pending` còn hạn của cùng gói và trả lại **link cũ** (`reused: true`), không tạo đơn thứ hai |
| 3 | Link cũ **đã được trả tiền** giữa chừng | Trả `status: Succeeded`, kích hoạt gói, không bắt khách trả lần nữa |
| 4 | Gói miễn phí, gói không tồn tại, gói ngừng bán | 400 |
| 5 | PayOS từ chối khoá (401/403) hoặc chưa cấu hình | 503 với thông báo rõ, ghi log lỗi cấu hình |

### Webhook (`POST /api/payos/webhook`, không cần JWT)

| # | Tình huống | Cách xử lý |
|---|---|---|
| 6 | **Khách trả tiền xong nhưng không về trang return** (mạng yếu) | Webhook là nguồn sự thật, không phụ thuộc trang return. Trang return/cancel của frontend nên gọi `POST /api/payments/{id}/sync` để lấy trạng thái thật, **không tin tham số trên URL** |
| 7 | **Webhook gửi lại nhiều lần** (PayOS retry) | Idempotent: lần sau trả `Duplicate`, **không kích hoạt gói hai lần** (ngoài ra còn unique index `PaymentTransactionId` ở DB để chặn webhook song song) |
| 8 | **Webhook giả** (sai chữ ký) | Kiểm tra HMAC-SHA256 bằng `ChecksumKey`, sai thì 400 và không đổi gì |
| 9 | Webhook cho `orderCode` lạ (kể cả webhook thử của PayOS khi đăng ký URL) | Trả 200 `Ignored` để PayOS ngừng gửi lại |
| 10 | **Tiền về sai số tiền** | Đơn chuyển `Failed`, **không kích hoạt gói**, tự mở yêu cầu hoàn tiền đúng số tiền thực nhận |
| 11 | **Tiền về sau khi khách đã huỷ đơn** | Không kích hoạt, tự mở yêu cầu hoàn tiền. Webhook lặp lại không mở thêm yêu cầu thứ hai |
| 12 | **Tiền về muộn sau khi đơn `Expired`** (sát giờ hết hạn) | Chấp nhận vì tiền đã nhận đủ: đơn thành `Succeeded` và kích hoạt gói |
| 13 | Mua gói khi đang có gói còn hạn | Gói mới xếp hàng `Scheduled`, bắt đầu đúng lúc gói cũ hết hạn |
| 14 | Server lỗi 500 khi xử lý webhook | PayOS tự gửi lại; vì idempotent nên an toàn |

### Đối soát (`POST /api/payments/{id}/sync` và worker nền)

| # | Tình huống | Cách xử lý |
|---|---|---|
| 15 | **Webhook không bao giờ tới** (server tắt, URL sai, PayOS lỗi) | `sync` và worker hỏi PayOS: `PAID` thì kích hoạt, `CANCELLED` thì `Cancelled`, `EXPIRED` thì `Expired` |
| 16 | **Mạng yếu lúc đối soát** | Ném 503 và **giữ nguyên `Pending`**; không đoán trạng thái. Worker dừng lượt đó, một phút sau thử lại |
| 17 | Đơn `Pending` đã quá `ExpiresAt` (+ ân hạn) mà PayOS vẫn `PENDING` | Đóng link ở PayOS rồi mới `Expired`, để khách không trả vào đơn đã bị coi là hết hạn. Nếu đóng link báo "không huỷ được" (có thể vừa được trả) thì **không** đánh dấu Expired, lượt sau sẽ thấy `PAID` |
| 18 | Giao dịch `Succeeded` nhưng **chưa có gói** (crash giữa hai lần lưu) | Worker tự vá: tạo gói cho giao dịch đó |
| 19 | Gói `Active` quá `EndAt` / gói `Scheduled` đến ngày | Worker chuyển `Expired` / `Active` |

### Huỷ (`POST /api/payments/{id}/cancel`)

| # | Tình huống | Cách xử lý |
|---|---|---|
| 20 | Huỷ lúc **mạng yếu** (PayOS không phản hồi) | 503, **giữ nguyên `Pending`** vì link PayOS vẫn còn sống, không được tự đánh dấu `Cancelled` |
| 21 | Huỷ đúng lúc **khách vừa trả tiền** | PayOS báo không huỷ được → hệ thống đối soát, trả `Succeeded` thay vì `Cancelled` |
| 22 | Huỷ giao dịch đã `Succeeded` | 400, hướng dẫn dùng yêu cầu hoàn tiền |
| 23 | Huỷ lại đơn đã `Cancelled`/`Expired`/`Failed` | Idempotent, trả lại trạng thái hiện tại |
| 24 | Huỷ / sync đơn của người khác | 404 |

---

## 5. Hoàn tiền

**Quy trình** (admin chuyển khoản tay vì PayOS không có API hoàn tiền):

```
Requested ──approve──▶ Approved ──complete (nhập mã GD ngân hàng)──▶ Completed
    └────reject (bắt buộc ghi lý do)──▶ Rejected  (khách được gửi yêu cầu mới)
```

| API | Ai | Việc làm |
|---|---|---|
| `POST /api/payments/{paymentId}/refunds` `{ "reason": "..." }` | Chủ giao dịch | Tạo yêu cầu `Requested` |
| `GET /api/refunds?status=Requested` | User thấy của mình, Admin thấy tất cả | Danh sách |
| `GET /api/refunds/{id}` | Như trên | Chi tiết |
| `POST /api/refunds/{id}/approve` `{ "note": "..." }` | Admin | `Approved` và **thu hồi gói đăng ký** ngay |
| `POST /api/refunds/{id}/reject` `{ "note": "lý do" }` | Admin | `Rejected` |
| `POST /api/refunds/{id}/complete` `{ "bankReference": "FT...", "note": "" }` | Admin | `Completed` sau khi đã chuyển khoản |

**Điều kiện khách tự yêu cầu** (không đạt thì 400): giao dịch `Succeeded`, thuộc về mình, trong
`RefundWindowDays` ngày kể từ lúc thanh toán, chưa có yêu cầu hoàn tiền đang sống, và gói chưa dùng AI
(`AiCallsUsed = 0`). Quá điều kiện thì khách liên hệ hỗ trợ.

**Yêu cầu do hệ thống tự mở** (`Source = System`): tiền về sai số tiền hoặc sau khi đơn đã huỷ (case 10, 11).
Admin xử lý cùng quy trình.

**Cách biết giao dịch đã hoàn tiền:** có dòng `PaymentRefund` `Status = Completed` cho giao dịch đó.
Trạng thái của `PaymentTransaction` vẫn là `Succeeded` do `CHECK` của bảng.
Giao dịch đã có yêu cầu hoàn tiền không xoá được.

---

## 6. Trình tự test

### 6.1 Test tự động (không cần key, không cần mạng)

```
dotnet test METANOIA/METANOIA.Test
```

Phải ra `Passed! - Failed: 0, Passed: 109` (hoặc hơn).

### 6.2 Test thủ công với PayOS thật

**Chuẩn bị (làm một lần):** chạy SQL (mục 1), gán Admin, điền key (mục 2), mở tunnel và đăng ký webhook (2.3),
chạy backend (`https://localhost:7007/swagger`).

**Lấy token:** mở `https://localhost:7007/login-test.html` (trang test đăng nhập Google có sẵn trong repo), đăng nhập,
copy `accessToken`, bấm **Authorize** trên Swagger và dán vào.
Cần hai tài khoản: một Admin, một người dùng thường.

Dùng gói có giá nhỏ để test (tạo bằng `POST /api/subscription-plans` với tài khoản Admin, ví dụ `Price = 2000`).
Mức tối thiểu PayOS cho phép chưa được xác minh; nếu bị từ chối, tăng giá lên.

| Bước | Thao tác | Kết quả mong đợi |
|---|---|---|
| **A. Happy path** | User: `POST /api/payments/checkout` `{ "planId": "..." }` → mở `checkoutUrl`, quét QR và chuyển khoản đúng số tiền | Vài giây sau `GET /api/payment-transactions` thấy `Succeeded`; `GET /api/user-subscriptions` thấy gói `Active` |
| **B. Bấm 2 lần** | Gọi `checkout` hai lần liên tiếp cho cùng gói | Lần 2 trả cùng `paymentId` và `reused: true` |
| **C. Khách bỏ ngang** | `checkout`, mở link nhưng không trả, gọi `POST /api/payments/{id}/cancel` | `Cancelled`. Mở lại link cũ: PayOS báo đã huỷ |
| **D. Hết hạn** | Đặt `Payments:LinkExpiryMinutes` = 2, `checkout`, đừng trả, chờ ~4 phút | Worker chuyển đơn sang `Expired` (xem log `PaymentReconciliationWorker`) |
| **E. Webhook bị lỡ** | Tắt tunnel/ngrok, `checkout`, trả tiền thật, rồi bật lại. Chờ ~2-3 phút (hoặc gọi `POST /api/payments/{id}/sync`) | Đơn thành `Succeeded` dù webhook không tới |
| **F. Server tắt khi trả tiền** | `checkout`, tắt backend, trả tiền, bật lại backend | Sau ≤ 3 phút worker kích hoạt gói |
| **G. Mạng yếu** | Bỏ `ClientId` thành sai hoặc rút mạng rồi gọi `checkout` | 503 `PAYMENT_GATEWAY_UNAVAILABLE`; đơn `Failed`; bật lại thì `checkout` chạy bình thường |
| **H. Webhook giả** | `curl -X POST https://localhost:7007/api/payos/webhook -H "Content-Type: application/json" -d '{"code":"00","desc":"success","success":true,"data":{"orderCode":123,"amount":2000},"signature":"abc"}' -k` | 400 "Chữ ký webhook không hợp lệ" |
| **I. Mua khi đang có gói** | Sau A, mua tiếp cùng gói | Gói thứ hai có `status: Scheduled`, `startAt` = `endAt` của gói đầu |
| **J. Hoàn tiền** | User: `POST /api/payments/{id}/refunds` → Admin: `approve` (gói chuyển `Cancelled`) → chuyển khoản tay cho khách → `complete` với `bankReference` | `Completed` |
| **K. Trả sai số tiền** | `checkout`, trả số tiền khác (PayOS cho phép thiếu/dư tuỳ cấu hình kênh) | Đơn `Failed`, xuất hiện yêu cầu hoàn tiền `Source = System` ở `GET /api/refunds` |

**Cách đọc log:** các dòng quan trọng nằm ở console khi chạy backend: `Webhook PayOS xử lý xong: Processed|Duplicate|RefundOpened`,
`Mở yêu cầu hoàn tiền...`, `Đối soát giao dịch ... dừng vì PayOS không phản hồi`.

**Lần đầu chạy với PayOS thật, kiểm tra ngay:** nếu webhook thật luôn bị log "Webhook PayOS có chữ ký không khớp"
thì thuật toán chữ ký trong [PayOSClient.cs](../METANOIA/METANOIA.Infrastructure/Services/PayOSClient.cs)
(`VerifyWebhook`) cần chỉnh theo PayOS thật (xem mục 7). Đơn vẫn không mất vì worker đối soát sẽ kích hoạt gói.

### 6.3 Frontend cần làm gì

1. Nút mua → `POST /api/payments/checkout` → chuyển hướng tới `checkoutUrl`.
2. Trang `ReturnUrl` và `CancelUrl` → gọi `POST /api/payments/{paymentId}/sync` (lưu `paymentId` từ bước 1 trong
   `localStorage`), hiển thị theo `status` trả về. **Không dùng tham số trên URL để quyết định thanh toán thành công.**
3. Nếu sync trả 503 → hiển thị "Đang xác nhận thanh toán, nếu bạn đã chuyển tiền hệ thống sẽ tự cập nhật trong vài phút"
   rồi thử lại sau vài giây. Đừng báo thất bại.

---

## 7. Những điều chưa được xác minh với PayOS thật

Tôi không có key PayOS nên **chưa gọi PayOS thật lần nào**; toàn bộ test dùng PayOS giả. Các điểm sau dựa trên
tài liệu `payos.vn/docs/api` và quy ước SDK chính thức, cần bạn xác nhận khi chạy thật:

- **Thuật toán chữ ký webhook** (sắp xếp key của `data` theo chữ cái, nối `key=value&...`, `null` thành rỗng,
  HMAC-SHA256 hex bằng `ChecksumKey`): tài liệu tôi đọc được chỉ nói "dùng HMAC-SHA256 với checksum key" và không
  mô tả chi tiết, nên phần này lấy theo cách các SDK PayOS làm. Chữ ký **tạo link** thì có công thức rõ trong tài liệu.
- **Chuỗi nhận diện "đơn không tồn tại"** (`"không tồn tại"`) và việc PayOS trả HTTP 200 kèm `code` khác `"00"` cho lỗi
  nghiệp vụ: lấy từ bảng lỗi trong tài liệu. Nếu PayOS dùng HTTP 4xx thì hành vi sẽ khác một chút.
- **Mã trạng thái** `PENDING, PROCESSING, PAID, EXPIRED, UNDERPAID, CANCELLED`: theo tài liệu.
- **Giới hạn mô tả, số tiền tối thiểu, có môi trường sandbox hay không**: tài liệu tôi đọc không nêu.
- **PayOS không có API hoàn tiền**: chỉ là "tài liệu API không nhắc tới", chưa hỏi bên PayOS. Nếu họ có tính năng
  hoàn tiền trên dashboard, admin vẫn dùng quy trình ở mục 5 và nhập mã giao dịch.
- Chưa chạy SQL trên Supabase thật, mới chạy trên EF InMemory. Index "mỗi giao dịch một gói" và "mỗi user một gói
  Active" là rào chắn cuối ở database cho các tình huống song song.
