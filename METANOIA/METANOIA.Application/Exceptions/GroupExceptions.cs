namespace METANOIA.Application.Exceptions
{
    // Người dùng thuộc nhóm nhưng vai trò không đủ quyền cho việc này: trả 403.
    // Người không thuộc nhóm thì nhận NotFoundException (404) để không lộ nhóm có tồn tại hay không.
    public sealed class GroupForbiddenException(string message) : Exception(message)
    {
    }

    // Giao việc (hoặc áp dụng giờ mới) cho người đang bận hoặc quá tải: trả 409 kèm danh sách để Leader quyết định.
    // Gửi lại kèm ConfirmConflicts = true để vẫn giao.
    public sealed class GroupAssigneeBusyException(string message, object details) : Exception(message)
    {
        public object Details { get; } = details;
    }
}
