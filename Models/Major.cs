using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class Major
{
    public int MajorId { get; set; }

    public int DepartmentId { get; set; }

    public string MajorName { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<AlumniProfile> AlumniProfiles { get; set; } = new List<AlumniProfile>();

    public virtual Department Department { get; set; } = null!;
}
