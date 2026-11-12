using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers;

public class MentorsController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public MentorsController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    public sealed class MentorCard
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = "";
        public string Expertise { get; set; } = "";
        public string? Introduction { get; set; }
        public string? CompanyName { get; set; }
        public string? JobTitle { get; set; }
    }

    [Authorize(Roles = "Alumni")]
    [HttpGet("/Mentors", Name = "MentorDirectory")]
    public async Task<IActionResult> Index([FromQuery] string? company)
    {
        var companyFilter = company?.Trim() ?? "";

        var query = _context.Database.SqlQuery<MentorCard>($"""
        SELECT
            account.UserId,
            account.FullName,
            COALESCE(mentor.Expertise, N'') AS Expertise,
            COALESCE(mentor.Introduction, profile.Bio) AS Introduction,
            currentJob.CompanyName,
            COALESCE(currentJob.JobTitle, profile.CurrentJobTitle) AS JobTitle
        FROM dbo.AlumniProfiles AS profile
        INNER JOIN dbo.Users AS account
            ON account.UserId = profile.UserId
        LEFT JOIN dbo.MentorProfiles AS mentor
            ON mentor.UserId = account.UserId
        OUTER APPLY (
            SELECT TOP (1)
                experience.CompanyName,
                experience.JobTitle
            FROM dbo.WorkExperiences AS experience
            WHERE experience.AlumniId = profile.AlumniId
              AND experience.IsCurrent = 1
            ORDER BY
                experience.StartDate DESC,
                experience.WorkExperienceId DESC
        ) AS currentJob
        WHERE account.IsActive = 1
          AND (
              profile.GraduationConfirmedAtUtc IS NOT NULL
              OR profile.IsGraduationVerified = 1
          )
          AND EXISTS (
              SELECT 1
              FROM dbo.UserRoles AS userRole
              INNER JOIN dbo.Roles AS role
                  ON role.RoleId = userRole.RoleId
              WHERE userRole.UserId = account.UserId
                AND role.RoleName = 'Alumni'
          )
        """);

        if (companyFilter.Length > 0)
        {
            query = query.Where(person =>
                person.CompanyName != null &&
                person.CompanyName.Contains(companyFilter));
        }

        var alumni = await query
            .OrderBy(person => person.FullName)
            .ThenBy(person => person.UserId)
            .ToListAsync();

        ViewData["CompanyFilter"] = companyFilter;

        return View(alumni);
    }

}