-- Bảng theo dõi hoàn tiền cho PaymentTransaction.
-- Chạy MỘT LẦN trên Supabase (SQL Editor) trước khi dùng các API thanh toán / hoàn tiền mới.
-- Có thể chạy lại an toàn (IF NOT EXISTS).
--
-- Vì sao cần bảng riêng: CHECK của PaymentTransaction.Status chỉ cho
-- Pending / Succeeded / Failed / Cancelled / Expired nên không thể thêm trạng thái "Refunded".
-- Một giao dịch được coi là "đã hoàn tiền" khi có dòng PaymentRefund với Status = 'Completed'.

CREATE TABLE IF NOT EXISTS "PaymentRefund" (
    "Id"                    uuid         NOT NULL DEFAULT gen_random_uuid(),
    "PaymentTransactionId"  uuid         NOT NULL,
    "Amount"                bigint       NOT NULL,
    "Reason"                varchar(500) NOT NULL,
    "Source"                varchar(10)  NOT NULL,
    "Status"                varchar(20)  NOT NULL DEFAULT 'Requested',
    "AdminNote"             varchar(500),
    "BankReference"         varchar(100),
    "CreatedAt"             timestamptz  NOT NULL DEFAULT now(),
    "ProcessedAt"           timestamptz,

    CONSTRAINT "PaymentRefund_pkey" PRIMARY KEY ("Id"),
    CONSTRAINT "PaymentRefund_PaymentTransactionId_fkey"
        FOREIGN KEY ("PaymentTransactionId") REFERENCES "PaymentTransaction" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_PaymentRefund_Amount"  CHECK ("Amount" > 0),
    CONSTRAINT "CK_PaymentRefund_Source"  CHECK ("Source" IN ('User', 'System')),
    CONSTRAINT "CK_PaymentRefund_Status"  CHECK ("Status" IN ('Requested', 'Approved', 'Rejected', 'Completed'))
);

-- Mỗi giao dịch chỉ có một yêu cầu hoàn tiền "đang sống"; bị Rejected thì được gửi yêu cầu mới.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PaymentRefund_OneOpenPerPayment"
    ON "PaymentRefund" ("PaymentTransactionId")
    WHERE "Status" <> 'Rejected';

CREATE INDEX IF NOT EXISTS "IX_PaymentRefund_Status" ON "PaymentRefund" ("Status");
