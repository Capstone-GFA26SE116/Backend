using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Domain.Entities
{
    public class UserPushSubscription
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string Endpoint { get; set; } = string.Empty;

        public string P256dh { get; set; } = string.Empty;

        public string Auth { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public virtual User? User { get; set; }
    }
}
