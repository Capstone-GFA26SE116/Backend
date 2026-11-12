-- 004_scheduleslot_status_check.sql
-- Bảo vệ cột "ScheduleSlot"."Status": chỉ nhận 10 giá trị mà code hiểu.
-- Chạy trong Supabase: SQL Editor. TÔI KHÔNG TỰ CHẠY file này, bạn đọc rồi chạy từng bước.
--
-- Vì sao cần: dịch vụ AI (Python) ghi slot THẲNG vào database, không qua luật C#. Nếu nó ghi sai chữ
-- (ví dụ 'approved' viết thường, hoặc 'Cancelled ' thừa dấu cách) thì database vẫn nhận, còn luật chuyển
-- trạng thái ở code sẽ không nhận ra giá trị đó.
--
-- Lưu ý: file 003_focus_session.sql (bước 2) đã mở rộng CHECK này nếu CHECK cũ thiếu 'InProgress'.
-- File này là bước kiểm tra + bổ sung cuối: chạy được dù 003 đã chạy hay chưa, chạy lại nhiều lần vẫn an toàn.
--
--   Bước 1 (chỉ đọc): xem CHECK hiện có và số slot theo từng Status.
--   Bước 2 (chỉ đọc): liệt kê slot có Status lạ (phải sửa trước khi chạy bước 3).
--   Bước 3 (sửa DB)  : thêm hoặc thay CHECK nếu còn thiếu. Tự dừng với thông báo nếu còn dòng sai.
--
-- Giá trị hợp lệ: Proposed, Approved, Due, InProgress, Done, Skipped, Missed, Overrun, Postponed, Cancelled.
-- Luật chuyển giữa các giá trị này nằm ở code (docs/STATUS_WORKFLOW.md), CHECK chỉ kiểm tra "giá trị có hợp lệ không".

-- ======================================================================================
-- BƯỚC 1. CHECK hiện có và phân bố Status (chỉ đọc, an toàn)
-- ======================================================================================
SELECT conname                   AS "Tên ràng buộc",
       pg_get_constraintdef(oid) AS "Định nghĩa"
FROM pg_constraint
WHERE conrelid = '"ScheduleSlot"'::regclass AND contype = 'c'
ORDER BY 1;

SELECT "Status", count(*) AS "Số slot"
FROM "ScheduleSlot"
GROUP BY "Status"
ORDER BY 2 DESC;

-- ======================================================================================
-- BƯỚC 2. Slot có Status lạ (chỉ đọc). Nếu bảng trả về dòng nào thì SỬA CHÚNG trước khi chạy bước 3.
-- ======================================================================================
SELECT "Id", "TaskId", "Status", "StartTime"
FROM "ScheduleSlot"
WHERE "Status" IS NULL
   OR "Status" NOT IN ('Proposed', 'Approved', 'Due', 'InProgress', 'Done',
                       'Skipped', 'Missed', 'Overrun', 'Postponed', 'Cancelled')
ORDER BY "StartTime" DESC;

-- Ví dụ sửa tay (chỉ chạy nếu bạn chắc, KHÔNG nằm trong luồng tự động):
--   UPDATE "ScheduleSlot" SET "Status" = 'Approved' WHERE lower(trim("Status")) = 'approved';

-- ======================================================================================
-- BƯỚC 3. Thêm hoặc thay CHECK (SỬA DATABASE)
-- ======================================================================================
--  * Còn slot có Status lạ        -> dừng, báo số dòng, KHÔNG sửa gì.
--  * Chưa có CHECK nào về Status  -> thêm "CK_ScheduleSlot_Status".
--  * Có CHECK nhưng thiếu giá trị -> gỡ CHECK đó rồi thêm lại đủ 10 giá trị.
--  * Đã có CHECK đủ 10 giá trị    -> không làm gì.
DO $$
DECLARE
    vals text[] := ARRAY['Proposed', 'Approved', 'Due', 'InProgress', 'Done',
                         'Skipped', 'Missed', 'Overrun', 'Postponed', 'Cancelled'];
    bad integer;
    r record;
    v text;
    incomplete boolean;
    has_complete boolean := false;
BEGIN
    SELECT count(*) INTO bad
    FROM "ScheduleSlot"
    WHERE "Status" IS NULL OR "Status" <> ALL (vals);

    IF bad > 0 THEN
        RAISE EXCEPTION 'Còn % slot có Status không hợp lệ. Chạy bước 2 để xem và sửa chúng trước, chưa thay đổi gì.', bad;
    END IF;

    FOR r IN
        SELECT conname, pg_get_constraintdef(oid) AS def
        FROM pg_constraint
        WHERE conrelid = '"ScheduleSlot"'::regclass
          AND contype = 'c'
          AND pg_get_constraintdef(oid) ILIKE '%Status%'
    LOOP
        incomplete := false;
        FOREACH v IN ARRAY vals LOOP
            IF r.def NOT ILIKE '%''' || v || '''%' THEN
                incomplete := true;
            END IF;
        END LOOP;

        IF incomplete THEN
            EXECUTE format('ALTER TABLE "ScheduleSlot" DROP CONSTRAINT %I', r.conname);
        ELSE
            has_complete := true;
        END IF;
    END LOOP;

    IF NOT has_complete THEN
        ALTER TABLE "ScheduleSlot"
            ADD CONSTRAINT "CK_ScheduleSlot_Status"
            CHECK ("Status" IN ('Proposed', 'Approved', 'Due', 'InProgress', 'Done',
                                'Skipped', 'Missed', 'Overrun', 'Postponed', 'Cancelled'));
    END IF;
END $$;

-- ======================================================================================
-- KIỂM TRA SAU KHI CHẠY (chỉ đọc): phải thấy đúng một CHECK về Status với đủ 10 giá trị
-- ======================================================================================
SELECT conname AS "Tên ràng buộc", pg_get_constraintdef(oid) AS "Định nghĩa"
FROM pg_constraint
WHERE conrelid = '"ScheduleSlot"'::regclass AND contype = 'c' AND pg_get_constraintdef(oid) ILIKE '%Status%';

-- Thử nhanh (sẽ báo lỗi vi phạm CHECK, đúng ý muốn; bọc trong giao dịch rồi hoàn tác nên không để lại gì):
--   BEGIN;
--   UPDATE "ScheduleSlot" SET "Status" = 'approved' WHERE "Id" = (SELECT "Id" FROM "ScheduleSlot" LIMIT 1);
--   ROLLBACK;
