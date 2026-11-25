using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Areas.Admin.ViewModels;

public sealed class AdminDashboardViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalUsers { get; set; }
    public int TotalProfiles { get; set; }
    public int TotalCompanies { get; set; }
    public int TotalJobPosts { get; set; }
    public int TotalApplications { get; set; }
    public int PendingJobPosts { get; set; }
    public int PendingCompanies { get; set; }
    public int PeriodUsers { get; set; }
    public int PeriodCompanies { get; set; }
    public int PeriodJobs { get; set; }
    public int PeriodApplications { get; set; }
    public List<DashboardCount> Months { get; set; } = new();
    public List<DashboardCount> Statuses { get; set; } = new();
    public List<DashboardCompany> TopCompanies { get; set; } = new();
    public List<DashboardPending> PendingJobs { get; set; } = new();
    public List<DashboardPending> PendingBusinesses { get; set; } = new();
}

public sealed class DashboardCount
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public sealed class DashboardCompany
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = "";
    public int Applications { get; set; }
}

public sealed class DashboardPending
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
