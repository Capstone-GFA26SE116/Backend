-- 003_focus_session.sql
-- Tính năng Focus session (Start / Stop, nhắc việc bằng Hangfire, thông báo).
-- Chạy trong Supabase: SQL Editor. TÔI KHÔNG TỰ CHẠY file này, bạn đọc rồi chạy từng bước.
--
-- Bạn đã tạo hai bảng "HangfireJobTrackers" và "UserPushSubscriptions". File này chỉ lo phần còn lại:
--   Bước 1 (chỉ đọc)  : xem các CHECK hiện có trên ScheduleSlot, Notification, FocusSession.
--   Bước 2 (sửa DB)   : mở rộng CHECK của "ScheduleSlot"."Status". CHỈ CHẠY nếu bước 1 cho thấy CHECK chưa có 'InProgress'.
--   Bước 3 (đọc)     : giải thích CHECK của "Notification"."Type" (không cần sửa).
--
-- Hangfire tự tạo schema riêng tên "hangfire" (khoảng chục bảng) trong database ở lần chạy server đầu tiên.
-- Không cần làm gì thêm, nhưng tài khoản trong connection string phải có quyền CREATE trên database
-- (user postgres.<project-ref> của Supabase có quyền này).

-- ======================================================================================
-- BƯỚC 1. XEM CHECK HIỆN CÓ (chỉ đọc, an toàn)
-- ======================================================================================
SELECT conrelid::regclass            AS "Bảng",
       conname                       AS "Tên ràng buộc",
       pg_get_constraintdef(oid)     AS "Định nghĩa"
FROM pg_constraint
WHERE contype = 'c'
  AND conrelid IN ('"ScheduleSlot"'::regclass, '"Notification"'::regclass, '"FocusSession"'::regclass)
ORDER BY 1, 2;

-- Cách đọc kết quả:
--  * Dòng của "ScheduleSlot" nhắc tới Status mà KHÔNG có 'InProgress' hoặc 'Due' hoặc 'Overrun'
--      -> phải chạy bước 2, nếu không bấm Start sẽ lỗi 500 (vi phạm CHECK).
--  * Không có dòng nào về Status của "ScheduleSlot" -> bỏ qua bước 2.
--  * Dòng của "Notification" nhắc tới "Type" -> xem bước 3.
--  * "FocusSession": kiểm tra CaptureMethod có đủ 'Timer', 'SelfReported', 'AutoTimeout'
--    và StopReason có đủ 'Completed', 'Interrupted', 'Blocked', 'Other'.

-- ======================================================================================
-- BƯỚC 2. MỞ RỘNG CHECK CỦA "ScheduleSlot"."Status"  (SỬA DATABASE)
-- ======================================================================================
-- Nếu bước 1 không có dòng CHECK nào về Status của "ScheduleSlot" thì BỎ QUA bước này (không có gì để sửa).
-- Giá trị cũ của code: Proposed, Approved, Done, Skipped, Missed, Cancelled.
-- Giá trị mới thêm   : Due, InProgress, Overrun, Postponed.
-- Khối này idempotent: chỉ đụng tới CHECK về Status chưa có 'InProgress', chạy lại lần hai không làm gì.
DO $$
DECLARE
    r record;
    replaced boolean := false;
BEGIN
    FOR r IN
        SELECT conname
        FROM pg_constraint
        WHERE conrelid = '"ScheduleSlot"'::regclass
          AND contype = 'c'
          AND pg_get_constraintdef(oid) ILIKE '%Status%'
          AND pg_get_constraintdef(oid) NOT ILIKE '%InProgress%'
    LOOP
        EXECUTE format('ALTER TABLE "ScheduleSlot" DROP CONSTRAINT %I', r.conname);
        replaced := true;
    END LOOP;

    IF replaced THEN
        ALTER TABLE "ScheduleSlot"
            ADD CONSTRAINT "CK_ScheduleSlot_Status"
            CHECK ("Status" IN ('Proposed', 'Approved', 'Due', 'InProgress', 'Done',
                                'Skipped', 'Missed', 'Overrun', 'Postponed', 'Cancelled'));
    END IF;
END $$;

-- ======================================================================================
-- BƯỚC 3. "Notification"."Type"  (KHÔNG CẦN SỬA, chỉ để hiểu)
-- ======================================================================================
-- Database có CHECK Notification_Type_check chỉ cho 'SlotReminder', 'MissedSlot', 'DeadlineRisk'.
-- Code đã được sửa cho khớp: cột Type luôn lưu 'SlotReminder' (nhắc việc) hoặc 'MissedSlot' (bỏ lỡ slot).
-- Loại chi tiết (START_PROMPT, NEAR_END, OVERRUN...) chỉ được gửi qua SignalR và Web Push, không ghi vào cột này.
-- Vì vậy KHÔNG cần đụng tới CHECK. (Nếu sau này muốn lưu loại chi tiết trong DB thì phải mở rộng CHECK này và sửa lại
-- NotificationService.ToStoredType; cứ hỏi trước khi làm.)

-- ======================================================================================
-- KIỂM TRA SAU KHI CHẠY (chỉ đọc)
-- ======================================================================================
SELECT pg_get_constraintdef(oid) AS "CHECK của ScheduleSlot"
FROM pg_constraint
WHERE conrelid = '"ScheduleSlot"'::regclass AND contype = 'c';
