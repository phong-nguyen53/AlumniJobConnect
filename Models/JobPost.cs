using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class JobPost
{
    public int JobPostId { get; set; }

    public int CompanyId { get; set; }

    public int? JobCategoryId { get; set; }

    public string Title { get; set; } = null!;

    public string JobDescription { get; set; } = null!;

    public string? Requirements { get; set; }

    public string? Benefits { get; set; }

    public string? WorkLocation { get; set; }

    public string? EmploymentType { get; set; }

    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public int Quantity { get; set; }

    public DateOnly? Deadline { get; set; }

    public string ApprovalStatus { get; set; } = null!;

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Application> Applications { get; set; } = new List<Application>();

    public virtual Company Company { get; set; } = null!;

    public virtual JobCategory? JobCategory { get; set; }
}
