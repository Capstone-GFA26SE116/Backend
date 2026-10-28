using METANOIA.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Dto
{
    public sealed record FocusStartResponse(
        Guid SessionId,
        DateTimeOffset StartedAt,
        CaptureMethod CaptureMethod,
        int PlannedMinutes,
        DateTimeOffset ExpectedEndAt,
        int StartedEarlyMinutes,
        string? Warning
    );

    public sealed record FocusStopResponse(
        Guid SessionId,
        DateTimeOffset EndedAt,
        double SessionMinutes,
        double TaskTotalMinutes,
        bool CheckInRequired
    );
    public sealed record FocusCurrentResponse(
        Guid SessionId,
        Guid TaskId,
        string TaskTitle,
        Guid? ScheduleSlotId,
        DateTimeOffset StartedAt,
        long ElapsedSeconds,
        DateTimeOffset ServerTime,
        bool IsOverrun
    );

    public sealed record FocusSessionSummaryItem(
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        double Minutes,
        CaptureMethod CaptureMethod,
        FocusStopReason? StopReason,
        Guid SessionId
    );

    // Phiên bị hệ thống tự ngắt (AutoTimeout) mà người dùng chưa xác nhận hoặc loại bỏ. Dùng để dựng hộp thoại
    // "Bạn thực sự làm bao lâu?" rồi gọi POST /api/focus/{sessionId}/confirm
    public sealed record FocusPendingConfirmationItem(
        Guid SessionId,
        Guid TaskId,
        string TaskTitle,
        Guid? ScheduleSlotId,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        double SystemMinutes
    );

    public sealed record FocusTimeSummaryResponse(
        int PlannedMinutes,
        double ActualMinutes,
        int SessionCount,
        IReadOnlyList<FocusSessionSummaryItem> Sessions
    );
}
