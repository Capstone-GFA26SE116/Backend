using System;
using System.Collections.Generic;

namespace METANOIA.Domain.Entities;

public partial class TaskExecutionMemory
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public Guid? SubTaskId { get; set; }

    public string Content { get; set; } = null!;

    public string Outcome { get; set; } = null!;

    public int EstimatedMinutes { get; set; }

    public int ActualMinutes { get; set; }

    public string EmbeddingStatus { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual SubTask? SubTask { get; set; }

    public virtual Task Task { get; set; } = null!;
}
