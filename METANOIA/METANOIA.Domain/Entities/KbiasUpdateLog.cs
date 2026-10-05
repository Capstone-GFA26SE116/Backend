using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class KbiasUpdateLog
{
    public Guid Id { get; set; }

    public Guid UserDomainProfileId { get; set; }

    public Guid TaskId { get; set; }

    public decimal OldKbias { get; set; }

    public decimal NewKbias { get; set; }

    public decimal Alpha { get; set; }

    public int EstimatedMinutes { get; set; }

    public int ActualMinutes { get; set; }

    public decimal ObservedRatio { get; set; }

    public string CaptureMethod { get; set; } = null!;

    public string ConfidenceTier { get; set; } = null!;

    public int SampleIndex { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Task Task { get; set; } = null!;

    public virtual UserDomainProfile UserDomainProfile { get; set; } = null!;
}
