using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class JobCategory
{
    public int JobCategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<JobPost> JobPosts { get; set; } = new List<JobPost>();
}
