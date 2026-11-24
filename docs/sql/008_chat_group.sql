-- 008_chat_group.sql
-- Chat của nhóm: thêm cột "GroupId" vào bảng "ChatConversation" có sẵn (bạn đã tự chạy file này trên database).
-- Lưu lại ở đây để repo khớp với database. Backend đã ánh xạ cột mới (ChatConversation.GroupId, ProjectId cho phép NULL)
-- nhưng CHƯA có API chat nào.
--
-- Luật sau khi chạy: Scope = 'Project' hoặc 'Task' thì có ProjectId (Task có thêm TaskId), không có GroupId;
--                    Scope = 'Group' thì chỉ có GroupId (ProjectId và TaskId rỗng).

BEGIN;

-- 1. Cột liên kết tới nhóm; xóa nhóm thì xóa luôn chat của nhóm.
ALTER TABLE "ChatConversation" ADD COLUMN IF NOT EXISTS "GroupId" uuid;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'ChatConversation_GroupId_fkey') THEN
        ALTER TABLE "ChatConversation"
            ADD CONSTRAINT "ChatConversation_GroupId_fkey"
            FOREIGN KEY ("GroupId") REFERENCES "WorkGroup" ("Id") ON DELETE CASCADE;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS "IX_ChatConversation_GroupId" ON "ChatConversation" ("GroupId");

-- 2. Chat nhóm không thuộc dự án cá nhân nào.
ALTER TABLE "ChatConversation" ALTER COLUMN "ProjectId" DROP NOT NULL;

-- 3. Thêm loại chat 'Group' và luật đi kèm (thay 2 CHECK cũ về Scope).
DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT conname FROM pg_constraint
             WHERE conrelid = '"ChatConversation"'::regclass AND contype = 'c'
               AND pg_get_constraintdef(oid) LIKE '%Scope%'
    LOOP
        EXECUTE format('ALTER TABLE "ChatConversation" DROP CONSTRAINT %I', r.conname);
    END LOOP;
END $$;

ALTER TABLE "ChatConversation" ADD CONSTRAINT "CK_ChatConversation_Scope"
    CHECK ("Scope" IN ('Project', 'Task', 'Group'));
ALTER TABLE "ChatConversation" ADD CONSTRAINT "CK_ChatConversation_ScopeLinks" CHECK (
       ("Scope" = 'Task'    AND "ProjectId" IS NOT NULL AND "TaskId" IS NOT NULL AND "GroupId" IS NULL)
    OR ("Scope" = 'Project' AND "ProjectId" IS NOT NULL AND "TaskId" IS NULL     AND "GroupId" IS NULL)
    OR ("Scope" = 'Group'   AND "ProjectId" IS NULL     AND "TaskId" IS NULL     AND "GroupId" IS NOT NULL)
);

COMMIT;
