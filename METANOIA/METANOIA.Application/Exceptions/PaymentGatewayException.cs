namespace METANOIA.Application.Exceptions
{
    // Cổng thanh toán (PayOS) lỗi mạng, quá hạn, bị từ chối xác thực hoặc chưa được cấu hình.
    // Khác UserFriendlyException: lỗi tạm thời, người dùng có thể thử lại và trả về 503.
    public class PaymentGatewayException : Exception
    {
        public PaymentGatewayException(string message) : base(message)
        {
        }

        public PaymentGatewayException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
