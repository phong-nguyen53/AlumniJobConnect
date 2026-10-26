using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class AcademicCourse
{
    public int AcademicCourseId { get; set; }

    public string CourseName { get; set; } = null!;

    public short? StartYear { get; set; }

    public short? GraduationYear { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<AlumniProfile> AlumniProfiles { get; set; } = new List<AlumniProfile>();
}
