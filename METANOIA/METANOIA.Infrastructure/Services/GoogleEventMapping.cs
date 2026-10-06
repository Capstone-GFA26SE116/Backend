using Google.Apis.Calendar.v3.Data;
using METANOIA.Domain.Entities;

namespace METANOIA.Infrastructure.Services
{
    internal static class GoogleEventMapping
    {
        public static void Apply(GoogleCalendarEvent local, Event googleEvent)
        {
            local.Title = googleEvent.Summary ?? "(không có tiêu đề)";
            local.StartTime = ToUtc(googleEvent.Start);
            local.EndTime = ToUtc(googleEvent.End);
            local.IsAllDay = googleEvent.Start.DateTimeDateTimeOffset is null;
            local.Etag = googleEvent.ETag ?? string.Empty;
            local.IsCancelled = googleEvent.Status == "cancelled";
            local.UpdatedAt = DateTime.UtcNow;
        }

        // Cột thời gian là timestamptz: Npgsql chỉ nhận DateTime có Kind = Utc
        private static DateTime ToUtc(EventDateTime value)
        {
            if (value.DateTimeDateTimeOffset is { } timed)
            {
                return timed.UtcDateTime;
            }

            var date = DateOnly.Parse(value.Date!);
            return DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }
    }
}
