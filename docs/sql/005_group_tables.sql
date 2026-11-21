-- 005_group_tables.sql
-- Nhánh Group (làm việc nhóm) ĐỢT 1: chỉ THÊM bảng mới, KHÔNG sửa bảng nào đang có.
-- Chạy trong Supabase: SQL Editor. TÔI KHÔNG TỰ CHẠY file này, bạn đọc rồi chạy.
-- Có thể chạy lại nhiều lần (IF NOT EXISTS). Chạy TRƯỚC KHI chạy bản backend có nhánh Group.
-- Thiết kế và luật nghiệp vụ: docs/GROUP_FEATURE.md.
--
--   Bước 1 (chỉ đọc)  : xem CHECK của "Notification"."Type".
--   Bước 2 (thêm bảng): 8 bảng mới.
--   Bước 3 (sửa DB)   : thêm 2 cột "GroupId", "GroupTaskId" vào "Notification". BẮT BUỘC trước khi chạy backend mới.
--   Bước 4 (sửa DB)   : nới CHECK của "Notification"."Type" cho 4 loại thông báo nhóm (dùng chung bảng Notification).
--
-- Thứ tự chạy: Bước 1 (đọc) -> Bước 2 -> Bước 3 -> Bước 4. Bước 3 phải sau Bước 2 vì khóa ngoại trỏ tới bảng mới.
--
-- Tên bảng chính là "WorkGroup" (không phải "Group") vì GROUP là từ khóa SQL; các cột tham chiếu vẫn gọi "GroupId".
-- Giá trị trạng thái và vai trò là chuỗi có CHECK, giống các bảng khác. Luật chuyển trạng thái nằm ở code.
-- Thành viên và vai trò: luật "luôn còn ít nhất một Leader" và "việc chỉ giao cho thành viên của nhóm"
-- được kiểm ở code (khó diễn tả bằng ràng buộc một bảng).

-- ======================================================================================
-- BƯỚC 1. CHECK hiện có của bảng Notification (chỉ đọc, an toàn)
-- ======================================================================================
SELECT conname                   AS "Tên ràng buộc",
       pg_get_constraintdef(oid) AS "Định nghĩa"
FROM pg_constraint
WHERE conrelid = '"Notification"'::regclass AND contype = 'c'
ORDER BY 1;

-- ======================================================================================
-- BƯỚC 2. Tạo bảng mới (SỬA DATABASE: chỉ thêm bảng)
-- ======================================================================================

-- ---- Nhóm. Đợt 1: một nhóm là một dự án chung.
CREATE TABLE IF NOT EXISTS "WorkGroup" (
    "Id"          uuid         NOT NULL DEFAULT gen_random_uuid(),
    "Name"        varchar(200) NOT NULL,
    "Description" text,
    "OwnerId"     uuid         NOT NULL,   -- người tạo nhóm, mặc định là Leader đầu tiên
    "StartDate"   date,
    "Deadline"    date,
    "Status"      varchar(20)  NOT NULL DEFAULT 'Active',
    "CreatedAt"   timestamptz  NOT NULL DEFAULT now(),

    CONSTRAINT "WorkGroup_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "WorkGroup_OwnerId_fkey"
        FOREIGN KEY ("OwnerId") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_WorkGroup_Status"    CHECK ("Status" IN ('Active', 'Archived')),
    CONSTRAINT "CK_WorkGroup_Dates"     CHECK ("StartDate" IS NULL OR "Deadline" IS NULL OR "Deadline" >= "StartDate"),
    CONSTRAINT "CK_WorkGroup_NameLen"   CHECK (length(btrim("Name")) > 0)
);
CREATE INDEX IF NOT EXISTS "IX_WorkGroup_OwnerId" ON "WorkGroup" ("OwnerId");

-- ---- Thành viên. Mỗi người một dòng trong mỗi nhóm.
CREATE TABLE IF NOT EXISTS "GroupMember" (
    "Id"        uuid        NOT NULL DEFAULT gen_random_uuid(),
    "GroupId"   uuid        NOT NULL,
    "UserId"    uuid        NOT NULL,
    "Role"      varchar(10) NOT NULL,
    "JoinedAt"  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT "GroupMember_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupMember_GroupId_fkey"
        FOREIGN KEY ("GroupId") REFERENCES "WorkGroup" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupMember_UserId_fkey"
        FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "GroupMember_GroupId_UserId_key" UNIQUE ("GroupId", "UserId"),
    CONSTRAINT "CK_GroupMember_Role" CHECK ("Role" IN ('Leader', 'Member', 'Viewer'))
);
CREATE INDEX IF NOT EXISTS "IX_GroupMember_UserId" ON "GroupMember" ("UserId");

-- ---- Lời mời. Chỉ lưu BẢN BĂM (SHA-256, hex 64 ký tự) của token, giống "RefreshToken".
CREATE TABLE IF NOT EXISTS "GroupInvitation" (
    "Id"           uuid         NOT NULL DEFAULT gen_random_uuid(),
    "GroupId"      uuid         NOT NULL,
    "Email"        varchar(254) NOT NULL,   -- lưu chữ thường
    "Role"         varchar(10)  NOT NULL,
    "TokenHash"    varchar(64)  NOT NULL,
    "InvitedById"  uuid         NOT NULL,
    "Status"       varchar(10)  NOT NULL DEFAULT 'Pending',
    "CreatedAt"    timestamptz  NOT NULL DEFAULT now(),
    "ExpiresAt"    timestamptz  NOT NULL,
    "RespondedAt"  timestamptz,

    CONSTRAINT "GroupInvitation_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupInvitation_GroupId_fkey"
        FOREIGN KEY ("GroupId") REFERENCES "WorkGroup" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupInvitation_InvitedById_fkey"
        FOREIGN KEY ("InvitedById") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "GroupInvitation_TokenHash_key" UNIQUE ("TokenHash"),
    CONSTRAINT "CK_GroupInvitation_Role"    CHECK ("Role" IN ('Leader', 'Member', 'Viewer')),
    CONSTRAINT "CK_GroupInvitation_Status"  CHECK ("Status" IN ('Pending', 'Accepted', 'Declined', 'Expired', 'Revoked')),
    CONSTRAINT "CK_GroupInvitation_Expires" CHECK ("ExpiresAt" > "CreatedAt")
);
-- Một email chỉ có một lời mời đang chờ trong một nhóm
CREATE UNIQUE INDEX IF NOT EXISTS "UX_GroupInvitation_Pending"
    ON "GroupInvitation" ("GroupId", "Email") WHERE "Status" = 'Pending';
CREATE INDEX IF NOT EXISTS "IX_GroupInvitation_Email" ON "GroupInvitation" ("Email");

-- ---- Giai đoạn của nhóm (giống "Milestone")
CREATE TABLE IF NOT EXISTS "GroupMilestone" (
    "Id"            uuid         NOT NULL DEFAULT gen_random_uuid(),
    "GroupId"       uuid         NOT NULL,
    "Name"          varchar(200) NOT NULL,
    "OrderIndex"    integer      NOT NULL,
    "DueDate"       date,
    "Status"        varchar(15)  NOT NULL DEFAULT 'Planned',
    "DeliveredAt"   timestamptz,
    "RevisionCount" integer      NOT NULL DEFAULT 0,

    CONSTRAINT "GroupMilestone_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupMilestone_GroupId_fkey"
        FOREIGN KEY ("GroupId") REFERENCES "WorkGroup" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupMilestone_GroupId_OrderIndex_key" UNIQUE ("GroupId", "OrderIndex"),
    CONSTRAINT "CK_GroupMilestone_Status"
        CHECK ("Status" IN ('Planned', 'InProgress', 'Delivered', 'InRevision', 'Approved')),
    CONSTRAINT "CK_GroupMilestone_OrderIndex" CHECK ("OrderIndex" >= 1),
    CONSTRAINT "CK_GroupMilestone_Revision"   CHECK ("RevisionCount" >= 0)
);

-- ---- Việc của nhóm. Không có 'Scheduled' vì nhánh Group đợt 1 không có lịch.
-- Người tạo không có cột riêng: được ghi trong "GroupActivityLog".
CREATE TABLE IF NOT EXISTS "GroupTask" (
    "Id"               uuid         NOT NULL DEFAULT gen_random_uuid(),
    "GroupId"          uuid         NOT NULL,
    "MilestoneId"      uuid,
    "AssigneeId"       uuid,               -- người được giao, rỗng = chưa giao
    "Title"            varchar(300) NOT NULL,
    "Description"      text,
    "Priority"         varchar(10)  NOT NULL DEFAULT 'Medium',
    "Status"           varchar(15)  NOT NULL DEFAULT 'ToDo',
    "Deadline"         timestamptz,
    "EstimatedMinutes" integer,
    "CreatedAt"        timestamptz  NOT NULL DEFAULT now(),
    "CompletedAt"      timestamptz,

    CONSTRAINT "GroupTask_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupTask_GroupId_fkey"
        FOREIGN KEY ("GroupId") REFERENCES "WorkGroup" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupTask_MilestoneId_fkey"
        FOREIGN KEY ("MilestoneId") REFERENCES "GroupMilestone" ("Id") ON DELETE SET NULL,
    CONSTRAINT "GroupTask_AssigneeId_fkey"
        FOREIGN KEY ("AssigneeId") REFERENCES "User" ("Id") ON DELETE SET NULL,
    CONSTRAINT "CK_GroupTask_Priority" CHECK ("Priority" IN ('Low', 'Medium', 'High')),
    CONSTRAINT "CK_GroupTask_Status"   CHECK ("Status" IN ('ToDo', 'InProgress', 'Done', 'Skipped')),
    CONSTRAINT "CK_GroupTask_Estimate" CHECK ("EstimatedMinutes" IS NULL OR "EstimatedMinutes" > 0),
    CONSTRAINT "CK_GroupTask_Title"    CHECK (length(btrim("Title")) > 0)
);
CREATE INDEX IF NOT EXISTS "IX_GroupTask_GroupId_Status" ON "GroupTask" ("GroupId", "Status");
CREATE INDEX IF NOT EXISTS "IX_GroupTask_AssigneeId"     ON "GroupTask" ("AssigneeId");
CREATE INDEX IF NOT EXISTS "IX_GroupTask_MilestoneId"    ON "GroupTask" ("MilestoneId");

-- ---- Việc con của nhóm. Mỗi việc con một người được giao.
CREATE TABLE IF NOT EXISTS "GroupSubTask" (
    "Id"               uuid         NOT NULL DEFAULT gen_random_uuid(),
    "GroupTaskId"      uuid         NOT NULL,
    "AssigneeId"       uuid,
    "Title"            varchar(300) NOT NULL,
    "EstimatedMinutes" integer,
    "OrderIndex"       integer      NOT NULL,
    "Status"           varchar(15)  NOT NULL DEFAULT 'ToDo',
    "CompletedById"    uuid,
    "CompletedAt"      timestamptz,

    CONSTRAINT "GroupSubTask_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupSubTask_GroupTaskId_fkey"
        FOREIGN KEY ("GroupTaskId") REFERENCES "GroupTask" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupSubTask_AssigneeId_fkey"
        FOREIGN KEY ("AssigneeId") REFERENCES "User" ("Id") ON DELETE SET NULL,
    CONSTRAINT "GroupSubTask_CompletedById_fkey"
        FOREIGN KEY ("CompletedById") REFERENCES "User" ("Id") ON DELETE SET NULL,
    CONSTRAINT "GroupSubTask_GroupTaskId_OrderIndex_key" UNIQUE ("GroupTaskId", "OrderIndex"),
    CONSTRAINT "CK_GroupSubTask_Status"     CHECK ("Status" IN ('ToDo', 'InProgress', 'Done', 'Skipped')),
    CONSTRAINT "CK_GroupSubTask_OrderIndex" CHECK ("OrderIndex" >= 1),
    CONSTRAINT "CK_GroupSubTask_Estimate"   CHECK ("EstimatedMinutes" IS NULL OR "EstimatedMinutes" > 0),
    CONSTRAINT "CK_GroupSubTask_Title"      CHECK (length(btrim("Title")) > 0)
);
CREATE INDEX IF NOT EXISTS "IX_GroupSubTask_AssigneeId" ON "GroupSubTask" ("AssigneeId");

-- ---- Bình luận trên việc
CREATE TABLE IF NOT EXISTS "GroupTaskComment" (
    "Id"          uuid        NOT NULL DEFAULT gen_random_uuid(),
    "GroupTaskId" uuid        NOT NULL,
    "AuthorId"    uuid        NOT NULL,
    "Content"     text        NOT NULL,
    "CreatedAt"   timestamptz NOT NULL DEFAULT now(),
    "EditedAt"    timestamptz,

    CONSTRAINT "GroupTaskComment_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupTaskComment_GroupTaskId_fkey"
        FOREIGN KEY ("GroupTaskId") REFERENCES "GroupTask" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupTaskComment_AuthorId_fkey"
        FOREIGN KEY ("AuthorId") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_GroupTaskComment_Content" CHECK (length(btrim("Content")) > 0 AND length("Content") <= 4000)
);
CREATE INDEX IF NOT EXISTS "IX_GroupTaskComment_GroupTaskId" ON "GroupTaskComment" ("GroupTaskId", "CreatedAt");

-- ---- Nhật ký hoạt động của nhóm (ai đổi gì). Không có FK tới EntityId vì nó trỏ tới nhiều bảng.
CREATE TABLE IF NOT EXISTS "GroupActivityLog" (
    "Id"         uuid        NOT NULL DEFAULT gen_random_uuid(),
    "GroupId"    uuid        NOT NULL,
    "ActorId"    uuid        NOT NULL,
    "EntityType" varchar(20) NOT NULL,
    "EntityId"   uuid,
    "Action"     varchar(40) NOT NULL,
    "OldValue"   text,
    "NewValue"   text,
    "CreatedAt"  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT "GroupActivityLog_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "GroupActivityLog_GroupId_fkey"
        FOREIGN KEY ("GroupId") REFERENCES "WorkGroup" ("Id") ON DELETE CASCADE,
    CONSTRAINT "GroupActivityLog_ActorId_fkey"
        FOREIGN KEY ("ActorId") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_GroupActivityLog_EntityType"
        CHECK ("EntityType" IN ('Group', 'Member', 'Invitation', 'Milestone', 'Task', 'SubTask', 'Comment'))
);
CREATE INDEX IF NOT EXISTS "IX_GroupActivityLog_GroupId_CreatedAt" ON "GroupActivityLog" ("GroupId", "CreatedAt" DESC);

-- ======================================================================================
-- BƯỚC 3. Thêm 2 cột liên kết vào "Notification" (SỬA MỘT BẢNG ĐANG CÓ, BẮT BUỘC)
-- ======================================================================================
-- "GroupId" và "GroupTaskId" cho biết thông báo nhóm thuộc nhóm nào và việc nào, để FE bấm mở thẳng.
-- Cả hai đều NULL được: thông báo cũ và thông báo của nhánh cá nhân để NULL, dữ liệu cũ không đổi.
-- BẮT BUỘC chạy bước này TRƯỚC KHI chạy backend mới: backend mới đọc hai cột này ở mọi truy vấn Notification
-- (kể cả thông báo nhắc slot), thiếu cột thì GET /api/notifications và các job nhắc việc sẽ lỗi.
-- Khóa ngoại ON DELETE SET NULL: xóa nhóm hoặc việc thì thông báo vẫn còn, chỉ mất liên kết.
ALTER TABLE "Notification" ADD COLUMN IF NOT EXISTS "GroupId"     uuid;
ALTER TABLE "Notification" ADD COLUMN IF NOT EXISTS "GroupTaskId" uuid;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = '"Notification"'::regclass AND conname = 'Notification_GroupId_fkey') THEN
        ALTER TABLE "Notification" ADD CONSTRAINT "Notification_GroupId_fkey"
            FOREIGN KEY ("GroupId") REFERENCES "WorkGroup" ("Id") ON DELETE SET NULL;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = '"Notification"'::regclass AND conname = 'Notification_GroupTaskId_fkey') THEN
        ALTER TABLE "Notification" ADD CONSTRAINT "Notification_GroupTaskId_fkey"
            FOREIGN KEY ("GroupTaskId") REFERENCES "GroupTask" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_Notification_GroupId"     ON "Notification" ("GroupId")     WHERE "GroupId" IS NOT NULL;
CREATE INDEX IF NOT EXISTS "IX_Notification_GroupTaskId" ON "Notification" ("GroupTaskId") WHERE "GroupTaskId" IS NOT NULL;

-- ======================================================================================
-- BƯỚC 4. Nới CHECK của "Notification"."Type" để thông báo nhóm dùng chung bảng Notification (SỬA MỘT BẢNG ĐANG CÓ)
-- ======================================================================================
-- Thông báo nhóm lưu Type = GroupAssigned, GroupMention, GroupStatusChanged, GroupInvited.
-- Code (NotificationService) ghi chú rằng CHECK hiện có tên "Notification_Type_check" và chỉ cho
-- 'SlotReminder', 'MissedSlot', 'DeadlineRisk'. ĐỌC KẾT QUẢ BƯỚC 1 trước: nếu tên hoặc danh sách giá trị khác thì sửa
-- ARRAY bên dưới cho đủ các giá trị cũ rồi mới chạy.
--  * Còn dòng có Type ngoài danh sách mới -> dừng, báo, KHÔNG sửa gì.
--  * Chưa nới mà chạy bản backend mới: thao tác nhóm vẫn thành công, chỉ thiếu thông báo (lỗi được ghi log, không làm hỏng request).
-- Chạy lại nhiều lần vẫn an toàn.
DO $$
DECLARE
    vals text[] := ARRAY['SlotReminder', 'MissedSlot', 'DeadlineRisk',
                         'GroupAssigned', 'GroupMention', 'GroupStatusChanged', 'GroupInvited'];
    bad integer;
    r record;
BEGIN
    SELECT count(*) INTO bad FROM "Notification" WHERE "Type" IS NULL OR "Type" <> ALL (vals);
    IF bad > 0 THEN
        RAISE NOTICE 'Còn % thông báo có Type ngoài danh sách. Sửa chúng trước, chưa thay đổi gì.', bad;
        RETURN;
    END IF;

    -- Gỡ mọi CHECK đang ràng buộc cột "Type" (tên có thể khác nhau giữa các môi trường)
    FOR r IN
        SELECT conname FROM pg_constraint
        WHERE conrelid = '"Notification"'::regclass AND contype = 'c'
          AND pg_get_constraintdef(oid) LIKE '%"Type"%'
    LOOP
        EXECUTE format('ALTER TABLE "Notification" DROP CONSTRAINT %I', r.conname);
    END LOOP;

    ALTER TABLE "Notification" ADD CONSTRAINT "Notification_Type_check"
        CHECK ("Type" = ANY (ARRAY['SlotReminder', 'MissedSlot', 'DeadlineRisk',
                                   'GroupAssigned', 'GroupMention', 'GroupStatusChanged', 'GroupInvited']));
END $$;
