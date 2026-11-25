-- 009_group_schedule_slot.sql
-- Cho phép dùng chung bảng "ScheduleSlot" để member tự xếp lịch cho việc nhóm ("GroupTask", "GroupSubTask"), và nối
-- "GroupFocusSession" với slot. Chạy file này TRƯỚC khi chạy backend mới. Backend cũ vẫn chạy được sau khi chạy file này.
--
-- Không đụng dữ liệu cũ: mọi slot hiện có là slot cá nhân (TaskId có giá trị, GroupTaskId và UserId rỗng).
-- Dịch vụ AI (Python) ghi slot cá nhân như cũ, không cần gửi "UserId".
--
-- Cách dùng (tự chạy trên Supabase, SQL Editor):
--   Bước 1 (chỉ đọc)  : xem số slot và các ràng buộc hiện có.
--   Bước 2 (sửa DB)   : thêm cột và ràng buộc, trong MỘT transaction. Chạy lại lần hai không làm gì thêm.

-- =====================================================================================================================
-- BƯỚC 1. KIỂM TRA (chỉ đọc, an toàn)
-- =====================================================================================================================

-- 1a. Số slot hiện có. Sau bước 2 tất cả vẫn là slot cá nhân.
SELECT count(*) AS "Số slot", count(*) FILTER (WHERE "TaskId" IS NULL) AS "Slot không có TaskId (phải là 0)"
FROM "ScheduleSlot";

-- 1b. Các ràng buộc (CHECK, khóa ngoại) đang có trên ScheduleSlot.
SELECT conname AS "Tên", contype AS "Loại", pg_get_constraintdef(oid) AS "Định nghĩa"
FROM pg_constraint
WHERE conrelid = '"ScheduleSlot"'::regclass
ORDER BY contype, conname;

-- =====================================================================================================================
-- BƯỚC 2. THÊM CỘT VÀ RÀNG BUỘC (sửa DB)
-- =====================================================================================================================
BEGIN;

-- 2a. Cột mới cho slot nhóm.
--     UserId: người đã xếp slot. Chỉ slot nhóm có giá trị; slot cá nhân vẫn suy ra chủ qua Task -> Project -> UserId.
ALTER TABLE "ScheduleSlot" ADD COLUMN IF NOT EXISTS "UserId"         uuid;
ALTER TABLE "ScheduleSlot" ADD COLUMN IF NOT EXISTS "GroupTaskId"    uuid;
ALTER TABLE "ScheduleSlot" ADD COLUMN IF NOT EXISTS "GroupSubTaskId" uuid;

-- Slot nhóm không có việc cá nhân.
ALTER TABLE "ScheduleSlot" ALTER COLUMN "TaskId" DROP NOT NULL;

-- 2b. Khóa ngoại. Xóa việc nhóm thì xóa slot của nó; xóa việc con nhóm thì slot về việc cha (GroupSubTaskId = NULL).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ScheduleSlot_UserId_fkey') THEN
        ALTER TABLE "ScheduleSlot" ADD CONSTRAINT "ScheduleSlot_UserId_fkey"
            FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE RESTRICT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ScheduleSlot_GroupTaskId_fkey') THEN
        ALTER TABLE "ScheduleSlot" ADD CONSTRAINT "ScheduleSlot_GroupTaskId_fkey"
            FOREIGN KEY ("GroupTaskId") REFERENCES "GroupTask" ("Id") ON DELETE CASCADE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ScheduleSlot_GroupSubTaskId_fkey') THEN
        ALTER TABLE "ScheduleSlot" ADD CONSTRAINT "ScheduleSlot_GroupSubTaskId_fkey"
            FOREIGN KEY ("GroupSubTaskId") REFERENCES "GroupSubTask" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

-- 2c. Ràng buộc dữ liệu: mỗi slot thuộc ĐÚNG MỘT loại việc (cá nhân hoặc nhóm); việc con đi với đúng loại việc của nó;
--     slot nhóm bắt buộc có người xếp (UserId).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_ScheduleSlot_OneKindOfTask') THEN
        ALTER TABLE "ScheduleSlot" ADD CONSTRAINT "CK_ScheduleSlot_OneKindOfTask"
            CHECK (("TaskId" IS NOT NULL AND "GroupTaskId" IS NULL) OR ("TaskId" IS NULL AND "GroupTaskId" IS NOT NULL));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_ScheduleSlot_SubTaskKind') THEN
        ALTER TABLE "ScheduleSlot" ADD CONSTRAINT "CK_ScheduleSlot_SubTaskKind"
            CHECK (("SubTaskId" IS NULL OR "TaskId" IS NOT NULL) AND ("GroupSubTaskId" IS NULL OR "GroupTaskId" IS NOT NULL));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_ScheduleSlot_GroupSlotHasUser') THEN
        ALTER TABLE "ScheduleSlot" ADD CONSTRAINT "CK_ScheduleSlot_GroupSlotHasUser"
            CHECK ("GroupTaskId" IS NULL OR "UserId" IS NOT NULL);
    END IF;
END $$;

-- 2d. Chỉ mục cho lịch gộp, luật chồng giờ và báo cáo nhóm.
CREATE INDEX IF NOT EXISTS "IX_ScheduleSlot_UserId_StartTime" ON "ScheduleSlot" ("UserId", "StartTime") WHERE "UserId" IS NOT NULL;
CREATE INDEX IF NOT EXISTS "IX_ScheduleSlot_GroupTaskId"      ON "ScheduleSlot" ("GroupTaskId")          WHERE "GroupTaskId" IS NOT NULL;
CREATE INDEX IF NOT EXISTS "IX_ScheduleSlot_GroupSubTaskId"   ON "ScheduleSlot" ("GroupSubTaskId")       WHERE "GroupSubTaskId" IS NOT NULL;

-- 2e. Phiên bấm giờ nhóm có thể gắn với một slot (tùy chọn), giống FocusSession cá nhân. Xóa slot thì để rỗng.
ALTER TABLE "GroupFocusSession" ADD COLUMN IF NOT EXISTS "ScheduleSlotId" uuid;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'GroupFocusSession_ScheduleSlotId_fkey') THEN
        ALTER TABLE "GroupFocusSession" ADD CONSTRAINT "GroupFocusSession_ScheduleSlotId_fkey"
            FOREIGN KEY ("ScheduleSlotId") REFERENCES "ScheduleSlot" ("Id") ON DELETE SET NULL;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS "IX_GroupFocusSession_ScheduleSlotId" ON "GroupFocusSession" ("ScheduleSlotId") WHERE "ScheduleSlotId" IS NOT NULL;

COMMIT;

-- Kiểm tra sau khi chạy (chỉ đọc): mong đợi "Slot nhóm" = 0 và các cột mới có mặt.
SELECT count(*) FILTER (WHERE "GroupTaskId" IS NOT NULL) AS "Slot nhóm", count(*) AS "Tổng slot" FROM "ScheduleSlot";
