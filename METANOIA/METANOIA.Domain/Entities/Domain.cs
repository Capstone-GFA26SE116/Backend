using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class Domain
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; }

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual ICollection<UserDomainProfile> UserDomainProfiles { get; set; } = new List<UserDomainProfile>();
}
