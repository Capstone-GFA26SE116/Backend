# Hướng dẫn thử nhánh Group bằng trang group-test.html

Trang thử: `METANOIA/METANOIA/wwwroot/group-test.html` (kèm `group-test-scenarios.js`, bộ kịch bản tự động). Trang gọi **toàn bộ API của nhánh Group** (đợt 1 và đợt 2) bằng nhiều tài khoản, hiển thị kết quả trực quan và có sẵn 25 kịch bản tự động. Thiết kế: [GROUP_FEATURE.md](GROUP_FEATURE.md). API cho FE: [GROUP_FE.md](GROUP_FE.md). Luật: BR-130 đến BR-154 trong [REPO_GUIDE.md](REPO_GUIDE.md).

Bộ kịch bản tự động đã được chạy thử trên backend thật (database InMemory) và đạt toàn bộ **242 bước kiểm tra**; trang chưa được mở thử bằng trình duyệt trong lúc viết, nên phần giao diện nếu có lỗi nhỏ bạn báo lại.

---

## A. Chuẩn bị (làm một lần)

### A1. Chạy SQL (đã chạy thì bỏ qua) và kiểm tra
Đã chạy `docs/sql/005_group_tables.sql` và `docs/sql/006_group_phase2.sql` (đủ các bước). Kiểm tra nhanh trên Supabase (chỉ đọc):

```sql
SELECT 'bảng nhóm' AS muc, count(*) AS so FROM information_schema.tables
WHERE table_name IN ('WorkGroup','GroupMember','GroupInvitation','GroupMilestone','GroupTask','GroupSubTask',
                     'GroupTaskComment','GroupActivityLog','GroupFocusSession','GroupTaskChangeRequest')
UNION ALL SELECT 'cột Notification (GroupId, GroupTaskId)', count(*) FROM information_schema.columns
WHERE table_name = 'Notification' AND column_name IN ('GroupId','GroupTaskId')
UNION ALL SELECT 'cột GroupTask (FixedStart, FixedEnd)', count(*) FROM information_schema.columns
WHERE table_name = 'GroupTask' AND column_name IN ('FixedStart','FixedEnd')
UNION ALL SELECT 'CHECK Notification có GroupChangeRequested', count(*) FROM pg_constraint
WHERE conname = 'Notification_Type_check' AND pg_get_constraintdef(oid) LIKE '%GroupChangeRequested%';
```

Kết quả đúng: **10, 2, 2, 1**.

### A2. Chạy backend
Mở terminal:

```powershell
cd D:\Capstone\META\Backend\METANOIA\METANOIA
dotnet run --launch-profile https
```

Lần đầu trên máy, tin cậy chứng chỉ dev để trình duyệt không chặn: `dotnet dev-certs https --trust` (chấp nhận hộp thoại, rồi đóng hẳn và mở lại trình duyệt).

Mở trình duyệt: **https://localhost:7007/group-test.html**. Các trang thử chỉ được phục vụ khi chạy `Development` (profile `http`/`https` đã đặt sẵn) hoặc khi `Swagger:Enabled = true`. Nếu cổng khác, dùng cổng trong dòng `Now listening on:` của terminal.

### A3. Chuẩn bị tài khoản Google
Trang có **5 ô tài khoản**, mỗi ô một người thật:

| Ô | Vai trò dự kiến trong các ca | Bắt buộc? |
|---|---|---|
| **Leader** | tạo nhóm, quản lý | có |
| **Member 1** | thành viên làm việc | có |
| **Member 2** | thành viên thứ hai | nên có (nhiều ca cần hai người cùng làm) |
| **Viewer** | chỉ xem | nên có (ca phân quyền) |
| **Người ngoài** | không thuộc nhóm (ca 404) và người nhận lời mời | nên có |

Tối thiểu 2 tài khoản (Leader + Member 1) thử được phần lớn ca; **đủ 5 thì chạy hết được 25 kịch bản tự động**. Kịch bản thiếu tài khoản tự bị bỏ qua (ghi rõ thiếu ai).

Mỗi tài khoản đăng nhập lần đầu sẽ tự tạo user (gói Miễn phí). Origin của trang (`https://localhost:7007`) phải có trong **Authorized JavaScript origins** của OAuth Client, giống khi dùng `fe-test.html`.

### A4. Đăng nhập từng ô (mục 1 của trang)
1. Dán **Google Client ID** (cùng ID trong appsettings), bấm **Tải nút đăng nhập Google**.
2. Ở ô "Gán tài khoản vào ô" chọn **Leader**, bấm nút Google, chọn tài khoản. Thanh đen trên cùng hiện tên tài khoản.
3. Chọn ô **Member 1**, bấm lại nút Google, chọn **"Use another account"** (hoặc "Dùng tài khoản khác") để chọn Google thứ hai. Lặp cho các ô còn lại.

Cách khác nếu Google cứ tự chọn lại tài khoản cũ: mỗi tài khoản một cửa sổ ẩn danh, lấy ID token ở `login-test.html`, rồi mở "Cách khác" ở mục 1 và **dán ID token** vào đúng ô. Token lưu trong trình duyệt này (localStorage), tự làm mới khi hết hạn; đóng trang mở lại vẫn còn.

> Từ giờ, **thanh đen trên cùng** là "tôi đang là ai". Bấm tên một ô để đổi tài khoản; mọi thao tác trên trang đều dùng tài khoản đang chọn. Dòng ngay dưới thanh cho biết vai trò của tài khoản trong nhóm đang chọn và được làm gì.

---

## B. Cách 1: chạy kịch bản tự động (khoảng 1 phút)

Kéo xuống **mục 12**, bấm **Chạy tất cả**. Mỗi kịch bản tạo một nhóm `[Thử] ...` riêng, gọi API bằng các tài khoản, kiểm tra từng luật và in ✓ hoặc ✗ (kèm HTTP thật nếu sai), rồi **lưu trữ nhóm đó** để lần sau không bị ảnh hưởng.

Kết quả mong đợi: dòng tổng ghi **"Tất cả bước đều đúng"**. Nếu có dòng ✗, chụp dòng đó (và mục 13 "Nhật ký gọi API") gửi tôi. Có thể bấm **Chạy riêng** từng kịch bản hoặc tick một số kịch bản rồi **Chỉ chạy các kịch bản đã tick**.

| Kịch bản | Cần | Kiểm tra |
|---|---|---|
| roles | Leader, M1, Viewer | ma trận quyền Leader/Member/Viewer |
| outsider | Leader, Người ngoài | người ngoài nhóm nhận 404 ở mọi nơi |
| invitations | Leader, M1, Người ngoài | mời đúng email, token, dùng lại, trùng, thu hồi, từ chối |
| lastleader | Leader, M1 | luôn còn ít nhất một Leader |
| assign | Leader, M1, Viewer, Người ngoài | giao việc chỉ cho Leader/Member của nhóm |
| removal | Leader, M1, M2 | xóa thành viên hoặc hạ xuống Viewer thì việc về chưa giao |
| status | Leader, M1 | Member chỉ đổi việc của mình |
| derive | Leader, M1, M2 | việc con tự suy status việc cha, mở lại việc Done |
| close | Leader | đóng việc bằng tay, xóa việc con cuối, thứ tự việc con |
| milestone | Leader | 409 khi còn việc mở, thứ tự, chuyển status, giai đoạn Delivered |
| progress | Leader, M1 | số liệu tiến độ, %, nhật ký |
| comments | Leader, M1, M2 | quyền sửa, xóa bình luận |
| archive | Leader | nhóm lưu trữ chỉ xem |
| notify | Leader, M1 | thông báo kèm liên kết, không tự báo chính mình |
| focus | Leader, M1 | Start/Stop điều khiển status việc con, việc cha, giai đoạn |
| focusrules | cả 5 | ai được bấm, một phiên một lúc, luật Stop |
| timereport | Leader, M1, M2, Viewer | giờ làm theo người và theo việc con |
| focusremove | Leader, M1 | bị xóa khỏi nhóm thì phiên đang chạy tự dừng |
| focusarchive | Leader, M1 | nhóm lưu trữ: không Start, vẫn Stop được |
| fixedvalidate | Leader | giờ cố định hợp lệ |
| busy | Leader, M1, M2 | kiểm tra trùng lịch khi giao, xác nhận |
| workload | Leader, M1, M2 | cảnh báo khối lượng tuần |
| changereq | Leader, M1, M2, Viewer | đề xuất đổi giờ: gửi, duyệt, từ chối, rút lại |
| changebusy | Leader, M1 | duyệt vào giờ đang bận: 409 rồi xác nhận |
| personal | Leader, M1 (+ có Domain) | slot cá nhân và phiên cá nhân ảnh hưởng việc nhóm |

Ghi chú: kịch bản **personal** tự tạo dự án và slot cá nhân thử của Member 1 rồi lưu trữ; cần Member 1 còn hạn mức dự án và hệ thống có ít nhất một Domain đang bật (nếu không, kịch bản tự ghi "Bỏ qua").

---

## C. Cách 2: thử tay một vòng đầy đủ (để hiểu giao diện)

Đăng nhập Leader và Member 1 (tối thiểu). Mỗi bước ghi **ai làm**, **làm gì**, **thấy gì**.

| # | Ai | Làm gì (mục của trang) | Kết quả đúng |
|---|---|---|---|
| 1 | Leader | Mục 2: nhập tên, bấm **Tạo nhóm** | Thanh hiện nhóm mới, dòng vai trò ghi "bạn là Leader", 1 thành viên |
| 2 | Leader | Mục 3: ô email chọn email của Member 1, vai trò Member, bấm **Gửi lời mời** | Hiện khung có `acceptToken` (chỉ một lần); bảng "Lời mời của nhóm" có dòng Pending |
| 3 | Member 1 | Bấm tên **Member 1** trên thanh. Mục 3, bảng **Lời mời gửi tới tôi** | Có lời mời; mục 11 có thông báo `GroupInvited` |
| 4 | Member 1 | Bấm **Chấp nhận** | Nhóm xuất hiện trong ô "Nhóm của tôi", vai trò Member |
| 5 | Leader | Mục 4: tạo giai đoạn "Giai đoạn 1" | Bảng giai đoạn: status Planned |
| 6 | Leader | Mục 5: tạo việc "Làm báo cáo" gắn giai đoạn, ước lượng 120 | Bảng việc có dòng ToDo |
| 7 | Leader | Chọn việc đó, mục 6: tạo 2 việc con, giao mỗi người một việc con (dùng ô "Giao cho") | Bảng việc con có tên người được giao; Member 1 nhận thông báo `GroupAssigned` |
| 8 | Member 1 | Mục 6: bấm **InProgress** ở việc con của mình | Việc con InProgress; việc cha tự **InProgress**; giai đoạn tự **InProgress** |
| 9 | Member 1 | Mục 8: chọn việc con, bấm **Start**, đợi vài giây, chọn lý do **Completed**, bấm **Stop** | Có đồng hồ chạy; sau Stop: việc con **Done**, báo cáo giờ ở mục 8 có số phút |
| 10 | Leader | Mục 6: đặt việc con còn lại **Done** | Việc cha tự **Done** |
| 11 | Leader | Mục 10 | Tiến độ 100%, giờ làm của Member 1; nhật ký ghi từng thay đổi và ai làm |

---

## D. Danh sách ca thử đầy đủ (tick từng ca)

Cột **Ai** là tài khoản bạn chọn ở thanh trên. "Nhận 403/404/400/409" nghĩa là trang hiện thông báo đỏ hoặc vàng với đúng mã đó (xem mục 13 của trang). Các ca đã có trong bộ tự động ghi (tự động) ở cột cuối; **các ca còn lại chỉ thử tay được**.

### D1. Nhóm và phân quyền
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 1.1 | Leader | Tạo nhóm tên rỗng | 400 | |
| 1.2 | Leader | Tạo nhóm có deadline trước ngày bắt đầu | 400 | |
| 1.3 | Người ngoài | Chọn nhóm của người khác (nhập id) hoặc xem thành viên, việc, tiến độ | 404 (không lộ nhóm có tồn tại) | outsider |
| 1.4 | Viewer | Tạo việc, đổi status, bình luận, xem nhật ký | 403 hết; xem việc, tiến độ, giờ làm được | roles |
| 1.5 | Member | Tạo việc, mời, xóa thành viên, tạo giai đoạn, lưu trữ nhóm | 403 hết | roles |
| 1.6 | Member | Bình luận, xem nhật ký | được | roles |
| 1.7 | Leader | Sửa tên, mô tả nhóm | được; nhật ký ghi `Updated` | |

### D2. Lời mời
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 2.1 | Leader | Mời email viết **hoa** của Member | 201, role mặc định Member | invitations |
| 2.2 | Người khác | Chấp nhận lời mời của người khác (bằng id hoặc token) | 404 | invitations |
| 2.3 | Đúng người | Chấp nhận bằng token | vào nhóm | invitations |
| 2.4 | Đúng người | Chấp nhận lần nữa | 400 | invitations |
| 2.5 | Leader | Mời người đã là thành viên; mời trùng email đang chờ; email sai; vai trò "Boss" | 400 cả bốn | invitations |
| 2.6 | Leader | Thu hồi lời mời; người được mời chấp nhận hoặc từ chối | 400 | invitations |
| 2.7 | Leader | Mời lại sau khi thu hồi hoặc bị từ chối | 201 | invitations |
| 2.8 | Leader | Mời một email **chưa có tài khoản** | 201, không có thông báo; khi người đó đăng nhập lần đầu bằng đúng email đó, lời mời hiện ra | |
| 2.9 | Member | Mời | 403 | roles |
| 2.10 | Leader | **Lời mời hết hạn (sau 7 ngày)** | xem **mục E1** | |

### D3. Thành viên
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 3.1 | Leader | Đổi vai trò Member thành Viewer | 200; việc đang giao cho người đó về chưa giao | removal |
| 3.2 | Leader | Xóa Member khỏi nhóm | 204; việc và việc con của họ về chưa giao; họ nhận 404 khi vào nhóm | removal |
| 3.3 | Leader cuối | Rời nhóm / tự hạ vai trò / tự xóa mình | 400 cả ba | lastleader |
| 3.4 | Leader | Thăng Member lên Leader, rồi Leader đầu rời nhóm | 200, 204; Leader đầu nhận 404 | lastleader |
| 3.5 | Leader | Đổi vai trò thành "Boss" | 400 | lastleader |
| 3.6 | Member | Rời nhóm | 204 | |

### D4. Giai đoạn
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 4.1 | Leader | Tạo giai đoạn trùng số thứ tự | 400 | milestone |
| 4.2 | Leader | Planned → Approved trực tiếp | 400 (chỉ Planned → InProgress) | milestone |
| 4.3 | Leader | Có việc InProgress trong giai đoạn Planned | Giai đoạn tự InProgress | milestone |
| 4.4 | Leader | Chuyển Delivered khi còn việc chưa xong | Hộp thoại xác nhận (409 `MILESTONE_HAS_OPEN_TASKS`, kèm danh sách việc); Hủy thì giữ nguyên; Đồng ý thì Delivered, có `deliveredAt` | milestone |
| 4.5 | Leader | Thêm việc vào giai đoạn Delivered hoặc Approved | 400 | milestone |
| 4.6 | Leader | Delivered → InRevision | `revisionCount` tăng 1 | milestone |
| 4.7 | Leader | Gắn việc vào giai đoạn của **nhóm khác** | 400 | milestone |

### D5. Việc và việc con
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 5.1 | Leader | Giao việc cho người ngoài nhóm, cho Viewer | 400 | assign |
| 5.2 | Member | Tự giao việc cho người khác | 403 | assign |
| 5.3 | Member | Đổi status việc của mình (không có việc con) | được; Done có `completedAt` | status |
| 5.4 | Member | Đổi status việc của người khác; việc có việc con | 403 | status |
| 5.5 | Leader | Skipped rồi Done trực tiếp; status "Scheduled" | 400 cả hai; Skipped → ToDo được | status |
| 5.6 | Member | Đổi việc con của người khác | 403 | derive |
| 5.7 | Member | Có việc con InProgress/Done | Việc cha tự InProgress | derive |
| 5.8 | Member | Xong hết việc con | Việc cha tự **Done** (`completedAt`) | derive |
| 5.9 | Leader | Thêm việc con vào việc đã Done | Việc cha mở lại (InProgress), `completedAt` rỗng | derive |
| 5.10 | Leader | Đóng việc bằng tay (Done/Skipped) khi còn việc con dở | Việc con còn dở thành Skipped | close |
| 5.11 | Leader | Thêm việc con vào việc Skipped | Việc vẫn Skipped | close |
| 5.12 | Leader | Xóa việc con chưa xong cuối cùng | Việc cha tự Done | close |
| 5.13 | Leader | Thêm việc con trùng số thứ tự; bỏ trống số thứ tự | 400; xếp cuối (1, 2, ...) | close |
| 5.14 | Leader | Xóa việc | Mất cả việc con, bình luận | |
| 5.15 | Leader | Lọc danh sách việc theo status, người, giai đoạn | bảng lọc đúng | |
| 5.16 | Leader | Mục 5: ước lượng 0 hoặc âm; tiêu đề quá 300 ký tự | 400 | |

### D6. Bình luận, nhật ký, tiến độ
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 6.1 | Member | Bình luận (có khoảng trắng đầu cuối); bình luận rỗng | nội dung được cắt; rỗng 400 | comments |
| 6.2 | Người khác | Sửa, xóa bình luận của người khác | 403 | comments |
| 6.3 | Tác giả / Leader | Sửa (có `editedAt`) / Leader xóa | được | comments |
| 6.4 | Leader | Mục 10: xem tiến độ | số việc theo status, còn lại, chưa giao, % = Done / (tổng - Skipped), tiến độ từng người | progress |
| 6.5 | Member | Xem nhật ký | mới nhất trước, ghi ai làm gì | progress |

### D7. Lưu trữ
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 7.1 | Leader | Lưu trữ nhóm | status Archived; nhóm biến khỏi danh sách (trừ khi tick "hiện nhóm đã lưu trữ") | archive |
| 7.2 | Leader | Tạo việc, đổi status khi đã lưu trữ | 400 "chỉ xem được" | archive |
| 7.3 | Leader | Khôi phục | thao tác ghi dùng lại được | archive |
| 7.4 | Người được mời | Chấp nhận lời mời vào nhóm đã lưu trữ | 400 | |

### D8. Thông báo (mục 11)
| # | Ai | Điều kiện | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 8.1 | Người được mời (đã có tài khoản) | Leader mời | `GroupInvited`, có `groupId`, **không** có `groupTaskId` | notify |
| 8.2 | Người được giao | Leader giao việc hoặc việc con | `GroupAssigned` kèm `groupId` và `groupTaskId` (việc con: id việc cha) | notify |
| 8.3 | Người được giao | Người **khác** đổi status việc của họ | `GroupStatusChanged`; nếu họ tự đổi thì **không** có | notify |
| 8.4 | Thành viên | Bình luận có `@email` của họ | `GroupMention` | notify |
| 8.5 | Leader | Member gửi đề xuất đổi giờ | `GroupChangeRequested` | changereq |
| 8.6 | Người đề xuất | Leader duyệt/từ chối | `GroupChangeDecided` (có ghi chú) | changereq |
| 8.7 | Bất kỳ | Bấm **Mở** trên thông báo | trang nhảy tới nhóm và việc liên quan | |
| 8.8 | Bất kỳ | Người tự thao tác | **không** nhận thông báo về chính mình | notify |

### D9. Bấm giờ (mục 8)
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 9.1 | Member | Start trên việc con của mình | 201; việc con, việc cha InProgress; giai đoạn Planned → InProgress; có đồng hồ chạy | focus |
| 9.2 | Member | Stop **Completed** | việc con Done; còn việc con mở thì việc cha vẫn InProgress; xong việc con cuối thì việc cha Done | focus |
| 9.3 | Member | Stop **Interrupted** | status giữ nguyên (InProgress) | focus |
| 9.4 | Member | Việc không có việc con: Start/Stop Completed | chính việc Done | focus |
| 9.5 | Member | Việc có việc con mà không chọn việc con | 400 | focusrules |
| 9.6 | Member | Start việc con của người khác | 403 | focusrules |
| 9.7 | Viewer / Người ngoài | Start | 403 / 404 | focusrules |
| 9.8 | Member | Start khi đang có phiên | 409 `FOCUS_SESSION_CONFLICT`, nguồn `Group` | focusrules |
| 9.9 | Member | Stop **Blocked** hoặc **Other** không ghi chú | 400; có ghi chú thì 200 | focusrules |
| 9.10 | Người khác / Leader | Stop phiên của người khác | 404 | focusrules |
| 9.11 | Member | Stop hai lần | 409 | focusrules |
| 9.12 | Member | Việc con chưa giao: người được giao **việc cha** bấm | được | focusrules |
| 9.13 | Leader | Mục 8 "Giờ làm thực tế" | tổng phút, theo từng người, theo việc con, danh sách phiên; Viewer cũng xem được | timereport |
| 9.14 | Leader | Xóa Member khi họ đang chạy | phiên tự dừng; họ bấm được ở nhóm khác | focusremove |
| 9.15 | Leader | Lưu trữ nhóm khi Member đang chạy | Member vẫn Stop được (giờ ghi, status không đổi); không Start được | focusarchive |
| 9.16 | Member | Đang chạy việc **cá nhân** (focus-test.html) rồi Start việc nhóm | 409, `details.source = Personal` | personal |
| 9.17 | Member | Đang chạy việc nhóm rồi Start việc **cá nhân** | **vẫn được** (điểm hở đã biết, xem GROUP_FEATURE mục 11) | |
| 9.18 | Member | Phiên chạy quá 12 giờ rồi Stop | xem **mục E2** | |

### D10. Giờ cố định và kiểm tra lịch khi giao (mục 5)
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 10.1 | Leader | Tạo việc chỉ có giờ bắt đầu; kết thúc trước bắt đầu | 400 | fixedvalidate |
| 10.2 | Leader | Giao việc có giờ cố định cho người **đã có việc nhóm trùng giờ** (kể cả nhóm khác) | Hộp thoại xác nhận (409 `GROUP_ASSIGNEE_BUSY`, nguồn `GroupTask`); không lộ tiêu đề việc kia | busy |
| 10.3 | Leader | Hai việc chỉ **chạm đầu mút** (kết thúc 10:00, bắt đầu 10:00) | không báo trùng | busy |
| 10.4 | Leader | Bấm Đồng ý ở hộp thoại | giao được (gửi lại kèm `confirmConflicts`) | busy |
| 10.5 | Leader | Mục 5 **Kiểm tra lịch người nhận** | khung hiện BẬN/RẢNH, khoảng trùng, nguồn; chỉ Leader gọi được (Member 403) | busy |
| 10.6 | Leader | Dời giờ của việc **đã giao** vào giờ người đó bận | 409; đổi tiêu đề mà không đổi giờ thì không kiểm tra lại | busy |
| 10.7 | Leader | Giao việc trùng **slot cá nhân đang hoạt động** của người nhận | 409 nguồn `PersonalSchedule`; không lộ tên dự án và tiêu đề việc cá nhân | personal |
| 10.8 | Leader | Giao việc trùng **event Google** của người nhận | 409 nguồn `GoogleCalendar` | thử tay: **mục E3** |
| 10.9 | Leader | Việc có deadline + ước lượng; tổng ước lượng của người nhận trong 7 ngày kết thúc ở deadline vượt giờ làm một tuần (mặc định 8h-18h, 5 ngày = 3000 phút) | 409, hộp thoại có "Khối lượng: x/y phút" | workload |
| 10.10 | Leader | Giao cùng việc đó cho người khác còn rảnh | được | workload |
| 10.11 | Leader | Việc không giờ cố định, không deadline + ước lượng | giao thẳng, không kiểm tra | |
| 10.12 | Leader | Kiểm tra lịch của **nhóm đã lưu trữ** | việc trong nhóm lưu trữ không còn tính là bận | |

### D11. Đề xuất đổi giờ (mục 9)
| # | Ai | Thao tác | Kết quả đúng | Tự động |
|---|---|---|---|---|
| 11.1 | Người được giao (việc hoặc một việc con) | Gửi đề xuất giờ cố định mới + lý do | 201, Pending; Leader nhận `GroupChangeRequested` | changereq |
| 11.2 | Member không được giao / Viewer / người ngoài | Gửi đề xuất | 403 / 403 / 404 | changereq |
| 11.3 | Người đề xuất | Không có giá trị mới; thiếu một đầu giờ; kết thúc trước bắt đầu; lý do rỗng; giống giá trị hiện tại | 400 cả năm | changereq |
| 11.4 | Người đề xuất | Gửi đề xuất thứ hai khi còn một đề xuất Pending | 400 | changereq |
| 11.5 | Member | Xem danh sách đề xuất của nhóm | chỉ thấy của mình; Leader thấy hết; Viewer 403 | changereq |
| 11.6 | Member | Duyệt | 403 | changereq |
| 11.7 | Leader | Từ chối không ghi lý do | 400 | changereq |
| 11.8 | Leader | Từ chối kèm lý do | Rejected; việc giữ giờ cũ; người đề xuất nhận thông báo kèm lý do | changereq |
| 11.9 | Leader | Duyệt (kiểm tra lịch người nhận) | Approved; **việc đổi sang giờ mới** | changereq |
| 11.10 | Leader | Duyệt vào giờ người nhận đang bận | 409, đề xuất vẫn Pending, việc giữ giờ cũ; Đồng ý thì duyệt được | changebusy |
| 11.11 | Leader | Duyệt hoặc từ chối đề xuất đã xử lý | 400 | changereq |
| 11.12 | Người đề xuất | Rút lại đề xuất Pending; người khác rút | Cancelled; người khác 403; sau khi rút gửi đề xuất mới được | changereq |
| 11.13 | Người được giao | Đề xuất cho việc đã Done/Skipped | 400 | |
| 11.14 | Leader | Nhật ký ghi `ChangeRequested`, `ChangeApproved`, `ChangeRejected`, `ChangeCancelled` | có | changereq |

### D12. Nhánh cá nhân không bị ảnh hưởng
| # | Ai | Thao tác | Kết quả đúng |
|---|---|---|---|
| 12.1 | Bất kỳ | Mở `fe-test.html` / `focus-test.html`, tạo dự án, task, slot, Start/Stop như bình thường | hoạt động như cũ |
| 12.2 | Bất kỳ | `GET /api/notifications` | thông báo nhắc slot cũ vẫn có, `groupId` và `groupTaskId` là `null` |

---

## E. Ca cần sửa database tay (không làm được bằng giao diện)

Chạy trên Supabase SQL Editor, **chỉ với nhóm thử**.

### E1. Lời mời hết hạn (ca 2.10)
1. Leader mời một email, **chưa** chấp nhận. Lấy email đó.
2. Đẩy lùi thời gian của lời mời (phải sửa cả `CreatedAt` vì có ràng buộc `ExpiresAt > CreatedAt`):

```sql
UPDATE "GroupInvitation"
SET "CreatedAt" = now() - interval '8 days', "ExpiresAt" = now() - interval '1 day'
WHERE "Email" = 'email-duoc-moi@gmail.com' AND "Status" = 'Pending';
```

3. Người được mời tải lại mục 3: lời mời **biến mất** khỏi "Lời mời gửi tới tôi". Leader xem "Lời mời của nhóm": status hiện **Expired**. Người được mời chấp nhận bằng token → **400 "đã hết hạn"**. Leader mời lại → **201** (lời mời cũ được đánh dấu Expired).

### E2. Phiên bấm giờ quá 12 giờ (ca 9.18)
1. Member Start một việc, **chưa Stop**. Lấy id của họ (`SELECT "Id","Email" FROM "User";`).
2. Đẩy lùi giờ bắt đầu:

```sql
UPDATE "GroupFocusSession"
SET "StartedAt" = now() - interval '20 hours'
WHERE "EndedAt" IS NULL AND "UserId" = 'id-cua-member';
```

3. Member tải lại trang, mục 8 đồng hồ chạy khoảng 20 giờ. Bấm **Stop** → thông báo "phiên bị cắt còn 12 giờ", `sessionMinutes` = **720**, ghi chú phiên có thêm "[Phiên bị cắt...]".

### E3. Event Google trùng giờ giao việc (ca 10.8)
1. Đăng nhập Member bằng `fe-test.html` (cùng tài khoản), kết nối Google Calendar và đồng bộ (`/api/google-calendar/connect`, `POST /api/google-calendar/sync`).
2. Tạo một event (không phải cả ngày) trong Google Calendar của Member vào một giờ cụ thể, đồng bộ lại.
3. Leader tạo việc có giờ cố định trùng khoảng đó rồi giao cho Member → 409, nguồn **GoogleCalendar**, khung xem trước hiện khoảng bận nhưng không có tiêu đề event.

### E4. Ràng buộc một phiên chạy trong DB (tùy chọn)
Index `UX_GroupFocusSession_OneRunning` chặn hai phiên đang chạy của một người ngay cả khi hai request Start đến cùng lúc. Kiểm tra bằng cách sau khi Member đang chạy một phiên, thử chèn tay một dòng `GroupFocusSession` khác cho cùng `UserId` với `EndedAt` rỗng: Postgres từ chối (vi phạm unique).

---

## F. Dọn dữ liệu thử

- Kịch bản tự động tự lưu trữ nhóm của nó. Nhóm tạo tay thì bấm **Lưu trữ** ở mục 2.
- Xóa hẳn mọi nhóm thử (xóa kéo theo thành viên, lời mời, giai đoạn, việc, việc con, bình luận, nhật ký, phiên bấm giờ, đề xuất; thông báo còn nhưng mất liên kết):

```sql
DELETE FROM "WorkGroup" WHERE "Name" LIKE '[Thử]%';
```

- Dự án cá nhân thử của kịch bản `personal` tên `[Thử] Bí mật riêng tư` đã được lưu trữ; xóa hẳn bằng cách dùng giao diện của nhánh cá nhân nếu muốn.

---

## G. Lỗi thường gặp

| Hiện tượng | Nguyên nhân và cách xử lý |
|---|---|
| Thông báo đỏ "Chưa đăng nhập" hoặc HTTP 401 | Ô tài khoản chưa đăng nhập hoặc token hết hạn và không có refresh token (đăng nhập bằng accessToken dán tay). Đăng nhập lại ở mục 1 |
| Nút Google không hiện / báo origin | Chưa thêm `https://localhost:7007` vào Authorized JavaScript origins của OAuth Client |
| Google luôn chọn lại tài khoản cũ | Dùng "Use another account", hoặc cửa sổ ẩn danh + ID token từ `login-test.html` |
| HTTP 500 ở `/api/notifications` hoặc các API việc của nhóm | Chưa chạy bước 3 của `005` (cột `GroupId`, `GroupTaskId`) hoặc bước 1 của `006` (cột `FixedStart`, `FixedEnd`). Chạy lại phần SQL đó |
| Làm xong mà không thấy thông báo nhóm | Chưa chạy bước 4 của `005` hoặc của `006` (nới CHECK của `Notification`). Thao tác vẫn thành công, lỗi chỉ ghi log server |
| 403 và 404 khác nhau thế nào | **404**: không thuộc nhóm (hoặc không tồn tại). **403**: thuộc nhóm nhưng vai trò không đủ quyền (`code: GROUP_FORBIDDEN`) |
| Kịch bản bị "bỏ qua" | Thiếu tài khoản ở ô cần thiết (xem nhãn đỏ cạnh kịch bản), hoặc `personal` thiếu Domain |
| Kịch bản `personal` báo không tạo được dự án | Member 1 đã hết hạn mức dự án của gói Miễn phí; lưu trữ bớt dự án cá nhân |
| Bước ✗ ở kịch bản `workload` | Member 1 đã đổi giờ làm trong `PUT /api/me/settings` làm sức chứa tuần khác 3000 phút; kịch bản giả định giờ làm mặc định |

## H. Bảng mã lỗi của nhánh Group

| HTTP | Khi nào |
|---|---|
| `400` | Dữ liệu sai hoặc vi phạm luật (giao việc cho Viewer, chuyển status sai, nhóm đã lưu trữ, lời mời đã dùng hoặc hết hạn, Leader cuối...) |
| `403` | Có trong nhóm nhưng thiếu quyền: `GROUP_FORBIDDEN` |
| `404` | Không thuộc nhóm hoặc không tồn tại |
| `409` | `MILESTONE_HAS_OPEN_TASKS` (giao giai đoạn khi còn việc mở), `GROUP_ASSIGNEE_BUSY` (người nhận bận hoặc quá tải), `FOCUS_SESSION_CONFLICT` (đang có phiên chạy; `details.source` là `Group` hoặc `Personal`) |
