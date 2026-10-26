using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class WorkExperience
{
    public int WorkExperienceId { get; set; }

    public int AlumniId { get; set; }

    public string CompanyName { get; set; } = null!;

    public string JobTitle { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsCurrent { get; set; }

    public virtual AlumniProfile Alumni { get; set; } = null!;
}
