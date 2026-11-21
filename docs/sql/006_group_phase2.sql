-- 006_group_phase2.sql
-- Nhánh Group ĐỢT 2: bấm giờ làm việc (Start/Stop) cho việc của nhóm, giờ cố định + kiểm tra trùng lịch khi giao việc,
-- đề xuất đổi giờ. Cần chạy 005_group_tables.sql TRƯỚC.
-- Chạy trong Supabase: SQL Editor. TÔI KHÔNG TỰ CHẠY file này, bạn đọc rồi chạy từng bước.
-- Có thể chạy lại nhiều lần (IF NOT EXISTS). Chạy TRƯỚC KHI chạy bản backend đợt 2.
-- Thiết kế: docs/GROUP_FEATURE.md mục 11.
--
--   Bước 1 (sửa DB): thêm "FixedStart", "FixedEnd" vào "GroupTask" (BẮT BUỘC trước khi chạy backend đợt 2:
--                    backend đọc hai cột này ở mọi truy vấn GroupTask).
--   Bước 2 (thêm bảng): "GroupFocusSession" (phiên bấm giờ của từng người trên việc nhóm).
--   Bước 3 (thêm bảng): "GroupTaskChangeRequest" (đề xuất đổi giờ hoặc deadline).
--   Bước 4 (sửa DB): nới CHECK của "GroupActivityLog"."EntityType" và "Notification"."Type" cho các loại mới.
--
-- Không sửa bảng nào của nhánh cá nhân (FocusSession, ScheduleSlot...). Bảng "Notification" chỉ được nới CHECK ở Bước 4.

-- ======================================================================================
-- BƯỚC 1. Giờ cố định của việc nhóm (SỬA "GroupTask": chỉ thêm cột NULL được)
-- ======================================================================================
-- Việc có giờ cố định (họp, hẹn) có cả FixedStart và FixedEnd; việc chỉ có deadline thì để NULL cả hai.
ALTER TABLE "GroupTask" ADD COLUMN IF NOT EXISTS "FixedStart" timestamptz;
ALTER TABLE "GroupTask" ADD COLUMN IF NOT EXISTS "FixedEnd"   timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = '"GroupTask"'::regclass AND conname = 'CK_GroupTask_FixedTime') THEN
        ALTER TABLE "GroupTask" ADD CONSTRAINT "CK_GroupTask_FixedTime" CHECK (
            ("FixedStart" IS NULL AND "FixedEnd" IS NULL)
            OR ("FixedStart" IS NOT NULL AND "FixedEnd" IS NOT NULL AND "FixedEnd" > "FixedStart"));
    END IF;
END $$;

-- Tìm việc có giờ cố định trùng khoảng thời gian của người được giao
CREATE INDEX IF NOT EXISTS "IX_GroupTask_Assignee_Fixed"
    ON "GroupTask" ("AssigneeId", "FixedStart") WHERE "FixedStart" IS NOT NULL;

-- ======================================================================================
-- BƯỚC 2. Phiên bấm giờ của nhóm (thêm bảng)
-- ======================================================================================
-- Mỗi dòng là một lần làm của MỘT người trên một việc (hoặc một việc con). Giờ làm của từng người trên từng việc
-- là tổng các phiên đã kết thúc. Một người chỉ có một phiên đang chạy trong nhóm (xem UX_GroupFocusSession_OneRunning).
-- Không lưu số phút: tính từ StartedAt và EndedAt.
CREATE TABLE IF NOT EXISTS "GroupFocusSession" (
    "Id"             uuid        NOT NULL DEFAULT gen_random_uuid(),
    "GroupTaskId"    uuid        NOT NULL,
    "GroupSubTaskId" uuid,
    "UserId"         uuid        NOT NULL,
    "StartedAt"      timestamptz NOT NULL,
    "EndedAt"        timestamptz,
    "StopReason"     varchar(15),
    "Note"           text,
    "CaptureMethod"  varchar(15) NOT NULL DEFAULT 'Timer',

    CONSTRAINT "GroupFocusSession_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupFocusSession_GroupTaskId_fkey"
        FOREIGN KEY ("GroupTaskId") REFERENCES "GroupTask" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupFocusSession_GroupSubTaskId_fkey"
        FOREIGN KEY ("GroupSubTaskId") REFERENCES "GroupSubTask" ("Id") ON DELETE SET NULL,
    CONSTRAINT "GroupFocusSession_UserId_fkey"
        FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_GroupFocusSession_Ended"  CHECK ("EndedAt" IS NULL OR "EndedAt" >= "StartedAt"),
    CONSTRAINT "CK_GroupFocusSession_Reason" CHECK ("StopReason" IS NULL
                                                    OR "StopReason" IN ('Completed', 'Interrupted', 'Blocked', 'Other')),
    CONSTRAINT "CK_GroupFocusSession_Capture" CHECK ("CaptureMethod" IN ('Timer'))
);
CREATE INDEX IF NOT EXISTS "IX_GroupFocusSession_GroupTaskId" ON "GroupFocusSession" ("GroupTaskId");
CREATE INDEX IF NOT EXISTS "IX_GroupFocusSession_UserId"      ON "GroupFocusSession" ("UserId", "StartedAt" DESC);
-- Một người một phiên đang chạy: hai request Start đồng thời thì request sau bị database từ chối
CREATE UNIQUE INDEX IF NOT EXISTS "UX_GroupFocusSession_OneRunning"
    ON "GroupFocusSession" ("UserId") WHERE "EndedAt" IS NULL;

-- ======================================================================================
-- BƯỚC 3. Đề xuất đổi giờ hoặc deadline (thêm bảng)
-- ======================================================================================
-- Thành viên được giao việc đề xuất; Leader duyệt (áp dụng giá trị mới vào việc) hoặc từ chối.
-- Mỗi người chỉ có một đề xuất đang chờ trên một việc.
CREATE TABLE IF NOT EXISTS "GroupTaskChangeRequest" (
    "Id"            uuid         NOT NULL DEFAULT gen_random_uuid(),
    "GroupTaskId"   uuid         NOT NULL,
    "RequestedById" uuid         NOT NULL,
    "NewFixedStart" timestamptz,
    "NewFixedEnd"   timestamptz,
    "NewDeadline"   timestamptz,
    "Reason"        varchar(1000) NOT NULL,
    "Status"        varchar(10)  NOT NULL DEFAULT 'Pending',
    "DecidedById"   uuid,
    "DecidedAt"     timestamptz,
    "DecisionNote"  varchar(1000),
    "CreatedAt"     timestamptz  NOT NULL DEFAULT now(),

    CONSTRAINT "GroupTaskChangeRequest_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupTaskChangeRequest_GroupTaskId_fkey"
        FOREIGN KEY ("GroupTaskId") REFERENCES "GroupTask" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupTaskChangeRequest_RequestedById_fkey"
        FOREIGN KEY ("RequestedById") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "GroupTaskChangeRequest_DecidedById_fkey"
        FOREIGN KEY ("DecidedById") REFERENCES "User" ("Id") ON DELETE SET NULL,
    CONSTRAINT "CK_GroupTaskChangeRequest_Status"
        CHECK ("Status" IN ('Pending', 'Approved', 'Rejected', 'Cancelled')),
    -- Giờ cố định mới gồm cả hai đầu hoặc không có đầu nào, và phải có ít nhất một giá trị mới
    CONSTRAINT "CK_GroupTaskChangeRequest_Fixed"
        CHECK (("NewFixedStart" IS NULL AND "NewFixedEnd" IS NULL)
               OR ("NewFixedStart" IS NOT NULL AND "NewFixedEnd" IS NOT NULL AND "NewFixedEnd" > "NewFixedStart")),
    CONSTRAINT "CK_GroupTaskChangeRequest_AnyValue"
        CHECK ("NewFixedStart" IS NOT NULL OR "NewDeadline" IS NOT NULL),
    CONSTRAINT "CK_GroupTaskChangeRequest_Reason" CHECK (length(btrim("Reason")) > 0)
);
CREATE INDEX IF NOT EXISTS "IX_GroupTaskChangeRequest_GroupTaskId" ON "GroupTaskChangeRequest" ("GroupTaskId", "CreatedAt" DESC);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_GroupTaskChangeRequest_OnePending"
    ON "GroupTaskChangeRequest" ("GroupTaskId", "RequestedById") WHERE "Status" = 'Pending';

-- ======================================================================================
-- BƯỚC 4. Nới hai CHECK cho các loại mới (SỬA CHECK của hai bảng đang có)
-- ======================================================================================
-- (a) "GroupActivityLog"."EntityType": thêm 'FocusSession', 'ChangeRequest'.
-- (b) "Notification"."Type": thêm 'GroupChangeRequested', 'GroupChangeDecided' (cột Type tối đa 20 ký tự, cả hai đều vừa).
-- Cần đã chạy Bước 3 và Bước 4 của 005_group_tables.sql. Mỗi khối dừng và báo nếu còn dòng có giá trị ngoài danh sách.
-- Chưa nới (b): đề xuất đổi giờ vẫn hoạt động, chỉ thiếu thông báo (lỗi ghi log, không làm hỏng request).
-- Chạy lại nhiều lần vẫn an toàn.
DO $$
DECLARE
    vals text[] := ARRAY['Group', 'Member', 'Invitation', 'Milestone', 'Task', 'SubTask', 'Comment',
                         'FocusSession', 'ChangeRequest'];
    bad integer;
    r record;
BEGIN
    SELECT count(*) INTO bad FROM "GroupActivityLog" WHERE "EntityType" <> ALL (vals);
    IF bad > 0 THEN
        RAISE NOTICE 'GroupActivityLog còn % dòng có EntityType ngoài danh sách. Chưa thay đổi gì.', bad;
        RETURN;
    END IF;

    FOR r IN
        SELECT conname FROM pg_constraint
        WHERE conrelid = '"GroupActivityLog"'::regclass AND contype = 'c'
          AND pg_get_constraintdef(oid) LIKE '%EntityType%'
    LOOP
        EXECUTE format('ALTER TABLE "GroupActivityLog" DROP CONSTRAINT %I', r.conname);
    END LOOP;

    ALTER TABLE "GroupActivityLog" ADD CONSTRAINT "CK_GroupActivityLog_EntityType"
        CHECK ("EntityType" = ANY (ARRAY['Group', 'Member', 'Invitation', 'Milestone', 'Task', 'SubTask', 'Comment',
                                         'FocusSession', 'ChangeRequest']));
END $$;

DO $$
DECLARE
    vals text[] := ARRAY['SlotReminder', 'MissedSlot', 'DeadlineRisk',
                         'GroupAssigned', 'GroupMention', 'GroupStatusChanged', 'GroupInvited',
                         'GroupChangeRequested', 'GroupChangeDecided'];
    bad integer;
    r record;
BEGIN
    SELECT count(*) INTO bad FROM "Notification" WHERE "Type" IS NULL OR "Type" <> ALL (vals);
    IF bad > 0 THEN
        RAISE NOTICE 'Notification còn % dòng có Type ngoài danh sách. Chưa thay đổi gì.', bad;
        RETURN;
    END IF;

    FOR r IN
        SELECT conname FROM pg_constraint
        WHERE conrelid = '"Notification"'::regclass AND contype = 'c'
          AND pg_get_constraintdef(oid) LIKE '%"Type"%'
    LOOP
        EXECUTE format('ALTER TABLE "Notification" DROP CONSTRAINT %I', r.conname);
    END LOOP;

    ALTER TABLE "Notification" ADD CONSTRAINT "Notification_Type_check"
        CHECK ("Type" = ANY (ARRAY['SlotReminder', 'MissedSlot', 'DeadlineRisk',
                                   'GroupAssigned', 'GroupMention', 'GroupStatusChanged', 'GroupInvited',
                                   'GroupChangeRequested', 'GroupChangeDecided']));
END $$;
