using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Interfaces.Services
{
    public interface INotificationService
    {
        System.Threading.Tasks.Task SendAsync(Guid userId, string title, string message, string type, Guid? scheduleSlotId = null);
    }
}
