using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IPushSubscriptionService
    {
        System.Threading.Tasks.Task SubscribeAsync(Guid userId, PushSubscriptionDto request);
        System.Threading.Tasks.Task UnsubscribeAsync(Guid userId, string endpoint);
    }
}
