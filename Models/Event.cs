using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class Event
{
    public int EventId { get; set; }

    public int CreatedByUserId { get; set; }

    public string EventName { get; set; } = null!;

    public string? Description { get; set; }

    public string? Location { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public int? Capacity { get; set; }

    public string EventStatus { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual User CreatedByUser { get; set; } = null!;

    public virtual ICollection<EventRegistration> EventRegistrations { get; set; } = new List<EventRegistration>();
}
