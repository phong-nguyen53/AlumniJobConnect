using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class ApplicationStatus
{
    public int ApplicationStatusId { get; set; }

    public string StatusCode { get; set; } = null!;

    public string StatusName { get; set; } = null!;

    public int SortOrder { get; set; }

    public virtual ICollection<Application> Applications { get; set; } = new List<Application>();
}
