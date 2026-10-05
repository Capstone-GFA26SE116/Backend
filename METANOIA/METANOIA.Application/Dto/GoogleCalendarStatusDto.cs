namespace METANOIA.Application.Dto
{
    public class GoogleCalendarStatusDto
    {
        public bool IsConnected { get; set; }

        public string? Status { get; set; }

        public DateTime? TokenExpiresAt { get; set; }
    }
}
