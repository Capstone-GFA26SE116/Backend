namespace METANOIA.Application.Interfaces.Services
{
    public interface IUserInitializationService
    {
        // Bảo đảm user có đủ dữ liệu khởi tạo: project "Cá nhân", một gói Active (Miễn phí nếu chưa có gói nào),
        // và đủ 24 dòng UserCircadianProfile. Idempotent: thứ nào đã có thì bỏ qua.
        // Mọi thứ được lưu bằng MỘT lần SaveChanges nên gồm cả các thay đổi đang chờ trong cùng unit of work
        // (ví dụ User vừa được thêm bởi luồng đăng nhập): lỗi ở đâu thì không có gì được lưu.
        Task InitializeNewUserAsync(Guid userId, CancellationToken cancellationToken = default);

        // Chỉ chuẩn bị (chưa lưu) UserSubscription gói Miễn phí nếu user chưa có gói Active nào.
        // Trả về true nếu có thêm. Người gọi tự lưu để gộp chung transaction với việc của mình.
        Task<bool> StageFreeSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);

        // Id các user chưa được khởi tạo đầy đủ (dùng cho lệnh bù dữ liệu user cũ)
        Task<IReadOnlyList<Guid>> GetUserIdsNeedingInitializationAsync(CancellationToken cancellationToken = default);
    }
}
