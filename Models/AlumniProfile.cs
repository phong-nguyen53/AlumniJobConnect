using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class AlumniProfile
{
    public int AlumniId { get; set; }

    public int UserId { get; set; }

    public string? StudentCode { get; set; }

    public int? MajorId { get; set; }

    public int? AcademicCourseId { get; set; }

    public short? GraduationYear { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? Address { get; set; }

    public string? LinkedInUrl { get; set; }

    public string? CurrentJobTitle { get; set; }

    public string? CurrentCompanyName { get; set; }

    public string? EmploymentStatus { get; set; }

    public string? Bio { get; set; }

    public DateTime? ProfileUpdatedAt { get; set; }

    public virtual AcademicCourse? AcademicCourse { get; set; }

    public virtual ICollection<AlumniSkill> AlumniSkills { get; set; } = new List<AlumniSkill>();

    public virtual ICollection<Application> Applications { get; set; } = new List<Application>();

    public virtual Major? Major { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual ICollection<WorkExperience> WorkExperiences { get; set; } = new List<WorkExperience>();
}
