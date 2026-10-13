-- Bảng lưu refresh token của app (khác với GoogleConnection.RefreshTokenEncrypted là token của Google).
-- Chạy MỘT LẦN trên Supabase (SQL Editor) TRƯỚC KHI chạy bản backend mới: từ giờ đăng nhập cũng ghi vào bảng này.
-- Có thể chạy lại an toàn (IF NOT EXISTS). Không đụng tới bảng nào đang có.
--
-- Chỉ lưu BẢN BĂM (SHA-256, hex 64 ký tự) của token, không lưu token gốc: lộ database cũng không dùng lại được token.
-- Mỗi lần /auth/refresh, token cũ bị thu hồi (RevokedAt) và trỏ sang token mới (ReplacedById).
-- Token cũ bị đem ra dùng lại sau đó = dấu hiệu bị đánh cắp, hệ thống thu hồi toàn bộ token của user.

CREATE TABLE IF NOT EXISTS "RefreshToken" (
    "Id"            uuid        NOT NULL DEFAULT gen_random_uuid(),
    "UserId"        uuid        NOT NULL,
    "TokenHash"     varchar(64) NOT NULL,
    "CreatedAt"     timestamptz NOT NULL DEFAULT now(),
    "ExpiresAt"     timestamptz NOT NULL,
    "RevokedAt"     timestamptz,
    "ReplacedById"  uuid,

    CONSTRAINT "RefreshToken_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "RefreshToken_UserId_fkey"
        FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "RefreshToken_TokenHash_key" UNIQUE ("TokenHash"),
    CONSTRAINT "CK_RefreshToken_Expires" CHECK ("ExpiresAt" > "CreatedAt")
);

CREATE INDEX IF NOT EXISTS "IX_RefreshToken_UserId" ON "RefreshToken" ("UserId");
