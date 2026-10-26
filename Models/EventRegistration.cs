using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class EventRegistration
{
    public int EventRegistrationId { get; set; }

    public int EventId { get; set; }

    public int UserId { get; set; }

    public DateTime RegisteredAt { get; set; }

    public string AttendanceStatus { get; set; } = null!;

    public virtual Event Event { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
