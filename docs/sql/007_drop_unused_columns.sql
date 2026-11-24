-- 007_drop_unused_columns.sql
-- Dọn các cột mồ côi sau khi bỏ 3 bảng "FocusTrack", "BlindTestSession" và "PaymentRefund" (đã bỏ khỏi database và khỏi code).
--
-- File này KHÔNG BẮT BUỘC: bản backend mới không còn đọc hoặc ghi các cột dưới đây, nên chúng nằm yên cũng không gây lỗi
-- (các cột đều cho phép NULL). Chạy để database gọn hơn.
--
-- Cách dùng (tự chạy trên Supabase, SQL Editor):
--   Bước 1 (chỉ đọc)  : xem còn cột, khóa ngoại, chỉ mục nào liên quan.
--   Bước 2 (sửa DB)   : bỏ các cột mồ côi. CHỈ CHẠY SAU KHI mọi nơi chạy backend đã dùng bản mới (bản cũ còn đọc các cột này,
--                       bỏ cột trước thì bản cũ lỗi 500 khi bấm giờ và khi tạo đề xuất lịch).

-- =====================================================================================================================
-- BƯỚC 1. KIỂM TRA (chỉ đọc, an toàn)
-- =====================================================================================================================

-- 1a. Các cột còn lại. Mong đợi: tối đa 4 dòng (nếu 0 dòng thì không cần làm gì thêm).
SELECT table_name AS "Bảng", column_name AS "Cột", is_nullable AS "Cho phép NULL"
FROM information_schema.columns
WHERE (table_name = 'FocusSession'     AND column_name = 'FocusTrackId')
   OR (table_name = 'ScheduleProposal' AND column_name IN ('BlindTestSessionId', 'DisplayPosition', 'IsChosen'));

-- 1b. Khóa ngoại còn trỏ tới 3 bảng đã bỏ. Mong đợi: 0 dòng (khóa ngoại tự mất khi bảng bị xóa bằng CASCADE).
SELECT conrelid::regclass AS "Bảng", conname AS "Khóa ngoại"
FROM pg_constraint
WHERE contype = 'f'
  AND (pg_get_constraintdef(oid) ILIKE '%FocusTrack%'
    OR pg_get_constraintdef(oid) ILIKE '%BlindTestSession%'
    OR pg_get_constraintdef(oid) ILIKE '%PaymentRefund%');

-- 1c. Chỉ mục còn dùng các cột sẽ bỏ (sẽ tự mất khi bỏ cột).
SELECT tablename AS "Bảng", indexname AS "Chỉ mục"
FROM pg_indexes
WHERE (tablename = 'ScheduleProposal' AND indexdef ILIKE ANY (ARRAY['%BlindTestSessionId%', '%DisplayPosition%', '%IsChosen%']))
   OR (tablename = 'FocusSession' AND indexdef ILIKE '%FocusTrackId%');

-- =====================================================================================================================
-- BƯỚC 2. BỎ CÁC CỘT MỒ CÔI (sửa DB, tùy chọn)
-- =====================================================================================================================
-- Không dùng CASCADE: nếu còn view hoặc ràng buộc nào phụ thuộc, lệnh dừng với thông báo rõ thay vì xóa lan sang chỗ khác.
-- Bỏ dấu "--" ở đầu các dòng dưới đây rồi chạy.

-- ALTER TABLE "FocusSession"     DROP COLUMN IF EXISTS "FocusTrackId";
-- ALTER TABLE "ScheduleProposal" DROP COLUMN IF EXISTS "BlindTestSessionId";
-- ALTER TABLE "ScheduleProposal" DROP COLUMN IF EXISTS "DisplayPosition";
-- ALTER TABLE "ScheduleProposal" DROP COLUMN IF EXISTS "IsChosen";
