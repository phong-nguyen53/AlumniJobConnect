using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class Department
{
    public int DepartmentId { get; set; }

    public string DepartmentName { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Major> Majors { get; set; } = new List<Major>();
}
