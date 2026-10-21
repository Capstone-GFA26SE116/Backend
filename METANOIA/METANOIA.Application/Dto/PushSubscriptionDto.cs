using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Dto
{
    public class PushSubscriptionDto
    {
        // Đường dẫn định tuyến tới máy chủ Push của trình duyệt (FCM, Mozilla Push,...)
        public string Endpoint { get; set; } = null!;

        public PushSubscriptionKeysDto Keys { get; set; } = null!;
    }
    public class PushSubscriptionKeysDto
    {
        // Khóa công khai dùng để mã hóa payload
        public string P256dh { get; set; } = null!;

        // Khóa xác thực
        public string Auth { get; set; } = null!;
    }
}
