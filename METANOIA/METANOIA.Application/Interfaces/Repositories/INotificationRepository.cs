using METANOIA.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace METANOIA.Application.Interfaces.Repositories
{
    public interface INotificationRepository
    {
        System.Threading.Tasks.Task AddAsync(Notification notification);
    }
}
