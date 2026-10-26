using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class Application
{
    public int ApplicationId { get; set; }

    public int JobPostId { get; set; }

    public int AlumniId { get; set; }

    public int ApplicationStatusId { get; set; }

    public string? Cvurl { get; set; }

    public string? CoverLetter { get; set; }

    public DateTime AppliedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? EmployerNote { get; set; }

    public virtual AlumniProfile Alumni { get; set; } = null!;

    public virtual ApplicationStatus ApplicationStatus { get; set; } = null!;

    public virtual JobPost JobPost { get; set; } = null!;
}
