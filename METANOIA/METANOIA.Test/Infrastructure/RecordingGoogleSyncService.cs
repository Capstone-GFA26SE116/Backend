using System.Collections.Concurrent;
using METANOIA.Application.Dto;
using METANOIA.Application.Interfaces.Services;

namespace METANOIA.Test.Infrastructure
{
    public class GoogleSyncRecorder
    {
        public ConcurrentQueue<Guid> Calls { get; } = new();

        // true: gọi bản đồng bộ thật. false: chỉ ghi lại rồi trả kết quả rỗng (không gọi Google)
        public bool PassThrough { get; set; } = true;

        public bool ThrowOnSync { get; set; }
    }

    public class RecordingGoogleSyncService : IGoogleCalendarSyncService
    {
        private readonly GoogleSyncRecorder _recorder;
        private readonly IGoogleCalendarSyncService _inner;

        public RecordingGoogleSyncService(GoogleSyncRecorder recorder, IGoogleCalendarSyncService inner)
        {
            _recorder = recorder;
            _inner = inner;
        }

        public Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (_recorder.PassThrough)
            {
                return _inner.SyncAsync(userId, cancellationToken);
            }

            _recorder.Calls.Enqueue(userId);
            if (_recorder.ThrowOnSync)
            {
                throw new InvalidOperationException("Đồng bộ lỗi giả lập.");
            }

            return Task.FromResult(new GoogleCalendarSyncResultDto());
        }
    }
}
