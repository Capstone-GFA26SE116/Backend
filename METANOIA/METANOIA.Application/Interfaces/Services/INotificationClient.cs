using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using METANOIA.Application.Interfaces.Services;

namespace METANOIA.Application.Interfaces.Services
{
    public interface INotificationClient
    {
        Task ReceiveNotification(string message, string actionType, Guid notificationId);
    }
}
