namespace METANOIA.Infrastructure.Services
{
    // Id sự kiện Google do METANOIA tạo từ slot. Id cố định theo slot nên đẩy lại nhiều lần vẫn không sinh sự kiện trùng,
    // và khi kéo về thì nhận ra đây là sự kiện của METANOIA (không hiện trùng với slot trên lịch).
    // Google chỉ nhận id gồm chữ a-v và số 0-9, dài 5 đến 1024 ký tự: "metanoia" + Guid dạng hex thỏa điều đó.
    internal static class GoogleEventIds
    {
        private const string Prefix = "metanoia";

        public static string ForSlot(Guid slotId) => Prefix + slotId.ToString("N");

        public static bool IsFromMetanoia(string? googleEventId) =>
            googleEventId is not null && googleEventId.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
