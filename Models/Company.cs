using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class Company
{
    public int CompanyId { get; set; }

    public int UserId { get; set; }

    public string CompanyName { get; set; } = null!;

    public string? TaxCode { get; set; }

    public string? Industry { get; set; }

    public string? Address { get; set; }

    public string? Website { get; set; }

    public string? Description { get; set; }

    public string? LogoUrl { get; set; }

    public string VerificationStatus { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<JobPost> JobPosts { get; set; } = new List<JobPost>();

    public virtual User User { get; set; } = null!;
}
