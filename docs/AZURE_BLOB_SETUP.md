# Lưu file tài liệu dự án trên Azure Blob Storage

File tải lên ở chức năng **Project documents** trước đây lưu vào thư mục `uploads` trên ổ đĩa của server, có thể mất khi deploy lại.
Giờ backend tự chọn nơi lưu theo cấu hình:

| Cấu hình | File lưu ở đâu |
|---|---|
| Có `Storage:BlobConnectionString` | **Azure Blob Storage** (dùng khi deploy) |
| Không có | Thư mục trên ổ đĩa (`Storage:RootPath`, mặc định `uploads`), dùng khi chạy local |

API và FE **không đổi** (`/api/projects/{id}/documents/files`, `/api/documents/{id}/download`...). Database không đổi: cột `StoragePath`
giờ chứa tên blob (mã ngẫu nhiên cộng đuôi file, ví dụ `3f9c...a1.pdf`). Container luôn **riêng tư**, người ngoài không tải được
nếu không qua API (API đã kiểm tra chủ sở hữu).

## 1. Tạo Storage account trên Azure Portal (làm một lần)

1. Vào [portal.azure.com](https://portal.azure.com), chọn **Create a resource**, tìm **Storage account**, bấm **Create**.
2. Điền:
   - **Subscription**: gói có 100$ của bạn.
   - **Resource group**: chọn cùng nhóm với App Service `metanoia-backend-core`.
   - **Storage account name**: chữ thường và số, 3 đến 24 ký tự, duy nhất toàn cầu (ví dụ `metanoiadocs2026`).
   - **Region**: **Japan East** (cùng vùng với backend, truy cập nhanh).
   - **Performance**: Standard. **Redundancy**: **LRS** (rẻ nhất, đủ cho đồ án).
3. Các tab còn lại giữ mặc định (đừng bật "Allow anonymous access"). Bấm **Review + create**, rồi **Create**.
4. (Khuyên) Vào **Cost Management, Budgets** tạo cảnh báo ngân sách, để không bị bất ngờ khi hết credit.

## 2. Tạo container (tùy chọn, ứng dụng cũng tự tạo)

Storage account, mục **Data storage, Containers**, **+ Container**:
- Tên: `project-documents`
- **Public access level: Private (no anonymous access)**

Nếu bỏ qua bước này, backend tự tạo container (riêng tư) ở lần tải file đầu tiên.

## 3. Lấy connection string

Storage account, mục **Security + networking, Access keys**, bấm **Show** ở **key1**, copy toàn bộ **Connection string**
(dạng `DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net`).

> **Đây là bí mật.** Không dán vào code, không commit lên git, không gửi trong chat. Ai có chuỗi này đọc và xóa được mọi file.

## 4. Đặt cấu hình cho backend trên Azure

App Service `metanoia-backend-core`, mục **Settings, Environment variables**, thẻ **App settings**, **+ Add**:

| Name | Value |
|---|---|
| `Storage__BlobConnectionString` | Connection string ở bước 3 |
| `Storage__BlobContainer` | `project-documents` (tùy chọn, đây là giá trị mặc định) |

- Tên dùng **hai gạch dưới** `__` (Azure đổi thành `:` khi đọc cấu hình).
- Bấm **Apply**, xác nhận khởi động lại app.
- Cài đặt này **giữ nguyên qua các lần deploy** của GitHub Actions (CI chỉ đẩy code, không đụng App settings).

Máy local của bạn: để trống `Storage:BlobConnectionString` trong `appsettings.json` thì file vẫn lưu vào thư mục `uploads` như cũ.
Muốn thử Blob ngay trên máy mà không dùng Azure thật, dùng trình giả lập (xem mục 8).

## 5. Thử API

Cần: JWT (đăng nhập bằng `fe-test.html` hoặc Swagger, lấy `accessToken`) và `projectId` của một dự án của bạn
(`GET /api/projects`). `API` bên dưới là URL backend, ví dụ `https://metanoia-backend-core-fseaahdbayg8eygy.japaneast-01.azurewebsites.net`.

### Cách 1: Swagger (`<API>/swagger`, cần `Swagger:Enabled = true`)
1. Bấm **Authorize**, dán `accessToken`.
2. `POST /api/projects/{projectId}/documents/files`, **Try it out**, điền `projectId`, chọn file ở ô `File`, có thể điền `Name` và `Tag` (`Brief`, `Contract`, `Design`, `Other`), **Execute**. Kết quả 201.
3. `GET /api/projects/{projectId}/documents`: thấy tài liệu vừa tải, ghi lại `id`.
4. `GET /api/documents/{documentId}/download`: tải file về, mở ra phải đúng file gốc.
5. `DELETE /api/documents/{documentId}`: trả 204.

### Cách 2: curl (trên Windows dùng `curl.exe`)
```
curl.exe -X POST "$API/api/projects/$PROJECT_ID/documents/files" ^
  -H "Authorization: Bearer $TOKEN" ^
  -F "File=@C:\duong\dan\hop-dong.pdf" -F "Tag=Contract" -F "Name=Hop dong.pdf"

curl.exe "$API/api/projects/$PROJECT_ID/documents" -H "Authorization: Bearer $TOKEN"
curl.exe "$API/api/documents/$DOC_ID/download" -H "Authorization: Bearer $TOKEN" -o tai-ve.pdf
curl.exe -X DELETE "$API/api/documents/$DOC_ID" -H "Authorization: Bearer $TOKEN"
```
(Trong `cmd` dùng `%API%`, trong PowerShell dùng `$env:API`; ký tự `^` nối dòng của `cmd`, PowerShell dùng dấu huyền `` ` ``.)

## 6. Kiểm tra file thật sự nằm trên Azure

1. Sau khi upload, vào Storage account, mục **Data storage, Containers**, mở `project-documents`: phải thấy một blob tên dạng `3f9c...a1.pdf`.
2. Mở blob đó: **Content type** là `application/pdf` (hoặc loại tương ứng). Thử mở URL của blob trên trình duyệt: phải bị từ chối (container riêng tư), chỉ tải được qua API.
3. **Thử độ bền (điểm mấu chốt):** deploy lại backend (push `develop`, hoặc **Restart** App Service), rồi tải lại tài liệu cũ bằng `download`. Vẫn tải được là đúng.
4. Xóa tài liệu bằng API: blob tương ứng biến mất khỏi container.

## 7. Lỗi thường gặp

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| Upload trả 500, log có `AuthorizationFailure`, `403` hoặc `Server failed to authenticate` | Connection string sai, thiếu phần nào, hoặc key đã bị đổi (rotate). Copy lại từ Access keys, dán đủ cả chuỗi, không thêm dấu nháy |
| Upload trả 500, log có lỗi phân giải tên hoặc không kết nối được | Sai `AccountName`, hoặc Storage account đã bị xóa |
| Upload xong nhưng không thấy blob, file vẫn ở thư mục `uploads` | Chưa đặt `Storage__BlobConnectionString` (nhầm tên, dùng `:` thay `__`) hoặc app chưa khởi động lại |
| `download` trả 404 "File không còn trong kho lưu trữ" | Blob không còn. Thường là file tải lên **trước khi đổi sang Blob** (khi đó file nằm trên ổ đĩa server và không được chuyển sang). Tải file lên lại |
| Upload trả 400 "File phải có dung lượng từ 1 byte đến 20 MB" | File lớn hơn 20 MB (giới hạn của chức năng) |
| Upload trả 413 | Vượt giới hạn dung lượng request (21 MB) |

## 8. Thử Blob ngay trên máy, không cần Azure thật

Cài một lần: `npm install -g azurite`. Chạy trình giả lập (một cửa sổ riêng): `azurite --silent --location %TEMP%\azurite`.
Trong `appsettings.json` của máy bạn đặt:
```json
"Storage": { "BlobConnectionString": "UseDevelopmentStorage=true", "BlobContainer": "project-documents" }
```
Chạy backend, upload như mục 5. File nằm trong Azurite thay vì Azure thật. Xong việc, **xóa dòng này** (đặt lại chuỗi rỗng) để quay về lưu ổ đĩa.

## 9. Lưu ý

- **File cũ không tự chuyển sang Blob.** Tài liệu tải lên trước khi đổi vẫn còn dòng trong database nhưng không còn file, `download` sẽ trả 404; tải lên lại là được.
- **Xóa tài liệu** xóa dòng database trước, rồi xóa blob. Nếu xóa blob lỗi (mạng), blob thành "mồ côi" trong container (có ghi log cảnh báo), không ảnh hưởng người dùng, dọn tay trong portal khi cần.
- **Chi phí:** Blob LRS Standard tính theo dung lượng lưu (khoảng vài cent mỗi GB mỗi tháng) và số thao tác, rất nhỏ với đồ án. Xem bảng giá hiện hành của Azure.
- **Đổi key:** nếu lộ connection string, vào Access keys bấm **Rotate key** rồi cập nhật lại App setting.
- **Hạn mức dung lượng theo gói** (`StorageLimitMb`) vẫn **chưa được kiểm tra** khi tải file lên, chỉ có giới hạn 20 MB mỗi file.
