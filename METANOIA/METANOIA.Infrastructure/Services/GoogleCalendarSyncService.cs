using System.Net;
using Google;
using Google.Apis.Calendar.v3.Data;
using METANOIA.Application.Dto;
using METANOIA.Application.Exceptions;
using METANOIA.Application.Interfaces.Repositories;
using METANOIA.Application.Interfaces.Services;
using METANOIA.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace METANOIA.Infrastructure.Services
{
    public class GoogleCalendarSyncService : IGoogleCalendarSyncService
    {
        private const string CalendarId = "primary";
        private const int PageSize = 250;

        private readonly GoogleCalendarClientProvider _provider;
        private readonly IGoogleCalendarEventRepository _eventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public GoogleCalendarSyncService(
            GoogleCalendarClientProvider provider,
            IGoogleCalendarEventRepository eventRepository,
            IUnitOfWork unitOfWork)
        {
            _provider = provider;
            _eventRepository = eventRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<GoogleCalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var session = await _provider.OpenAsync(userId, cancellationToken);

            try
            {
                return await RunSyncAsync(session, cancellationToken);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
            {
                // syncToken đã hết hạn: Google yêu cầu đồng bộ lại từ đầu
                session.Connection.SyncToken = null;
                return await RunSyncAsync(session, cancellationToken);
            }
            catch (GoogleApiException ex)
            {
                session.Connection.LastSyncError = ex.Message;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new UserFriendlyException(
                    $"Đồng bộ Google Calendar thất bại ({(int)ex.HttpStatusCode}): {ex.Message}", ex);
            }
        }

        private async Task<GoogleCalendarSyncResultDto> RunSyncAsync(GoogleCalendarSession session, CancellationToken cancellationToken)
        {
            var connection = session.Connection;
            var result = new GoogleCalendarSyncResultDto();
            string? pageToken = null;
            string? nextSyncToken = null;

            do
            {
                // Các tham số phải giữ nguyên giữa các lần sync để syncToken hợp lệ
                var request = session.Service.Events.List(CalendarId);
                request.ShowDeleted = true;
                request.MaxResults = PageSize;
                request.SyncToken = connection.SyncToken;
                request.PageToken = pageToken;

                var page = await request.ExecuteAsync(cancellationToken);
                foreach (var googleEvent in page.Items ?? [])
                {
                    await ApplyAsync(connection, googleEvent, result, cancellationToken);
                }

                pageToken = page.NextPageToken;
                nextSyncToken = page.NextSyncToken;
            } while (pageToken is not null);

            connection.SyncToken = nextSyncToken;
            connection.LastSyncedAt = DateTime.UtcNow;
            connection.LastSyncError = null;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return result;
        }

        private async Task ApplyAsync(GoogleConnection connection, Event googleEvent, GoogleCalendarSyncResultDto result, CancellationToken cancellationToken)
        {
            var local = await _eventRepository.GetByGoogleEventIdAsync(connection.Id, googleEvent.Id, cancellationToken);

            if (googleEvent.Status == "cancelled")
            {
                if (local is not null && !local.IsCancelled)
                {
                    local.IsCancelled = true;
                    local.UpdatedAt = DateTime.UtcNow;
                    result.Cancelled++;
                }
                return;
            }

            if (local is null)
            {
                local = new GoogleCalendarEvent
                {
                    Id = Guid.NewGuid(),
                    GoogleConnectionId = connection.Id,
                    GoogleEventId = googleEvent.Id,
                    IsCreatedByMetanoia = false
                };
                await _eventRepository.AddAsync(local, cancellationToken);
                result.Added++;
            }
            else
            {
                result.Updated++;
            }

            GoogleEventMapping.Apply(local, googleEvent);
        }
    }
}
