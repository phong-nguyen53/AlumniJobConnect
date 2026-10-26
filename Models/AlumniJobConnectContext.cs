using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Models;

public partial class AlumniJobConnectContext : DbContext
{
    public AlumniJobConnectContext()
    {
    }

    public AlumniJobConnectContext(DbContextOptions<AlumniJobConnectContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AcademicCourse> AcademicCourses { get; set; }

    public virtual DbSet<AlumniProfile> AlumniProfiles { get; set; }

    public virtual DbSet<AlumniSkill> AlumniSkills { get; set; }

    public virtual DbSet<Application> Applications { get; set; }

    public virtual DbSet<ApplicationStatus> ApplicationStatuses { get; set; }

    public virtual DbSet<Company> Companies { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Event> Events { get; set; }

    public virtual DbSet<EventRegistration> EventRegistrations { get; set; }

    public virtual DbSet<JobCategory> JobCategories { get; set; }

    public virtual DbSet<JobPost> JobPosts { get; set; }

    public virtual DbSet<Major> Majors { get; set; }

    public virtual DbSet<Menu> Menus { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Skill> Skills { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<WorkExperience> WorkExperiences { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Data Source=PhongNguyen\\PHONG_NGUYEN;Initial Catalog=AlumniJobConnect;Integrated Security=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcademicCourse>(entity =>
        {
            entity.HasKey(e => e.AcademicCourseId).HasName("PK__Academic__C4BE4231957A4FFE");

            entity.HasIndex(e => e.CourseName, "UQ__Academic__9526E277DDE3D038").IsUnique();

            entity.Property(e => e.CourseName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<AlumniProfile>(entity =>
        {
            entity.HasKey(e => e.AlumniId).HasName("PK__AlumniPr__9336246A18370405");

            entity.HasIndex(e => new { e.MajorId, e.AcademicCourseId }, "IX_AlumniProfiles_Major_Course");

            entity.HasIndex(e => e.UserId, "UQ__AlumniPr__1788CC4D0680D2AF").IsUnique();

            entity.HasIndex(e => e.StudentCode, "UQ__AlumniPr__1FC88604BAACB93C").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.CurrentCompanyName).HasMaxLength(200);
            entity.Property(e => e.CurrentJobTitle).HasMaxLength(150);
            entity.Property(e => e.EmploymentStatus).HasMaxLength(40);
            entity.Property(e => e.Gender).HasMaxLength(30);
            entity.Property(e => e.LinkedInUrl).HasMaxLength(500);
            entity.Property(e => e.StudentCode)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.AcademicCourse).WithMany(p => p.AlumniProfiles)
                .HasForeignKey(d => d.AcademicCourseId)
                .HasConstraintName("FK__AlumniPro__Acade__534D60F1");

            entity.HasOne(d => d.Major).WithMany(p => p.AlumniProfiles)
                .HasForeignKey(d => d.MajorId)
                .HasConstraintName("FK__AlumniPro__Major__52593CB8");

            entity.HasOne(d => d.User).WithOne(p => p.AlumniProfile)
                .HasForeignKey<AlumniProfile>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AlumniPro__UserI__5165187F");
        });

        modelBuilder.Entity<AlumniSkill>(entity =>
        {
            entity.HasKey(e => new { e.AlumniId, e.SkillId });

            entity.Property(e => e.ProficiencyLevel).HasMaxLength(30);

            entity.HasOne(d => d.Alumni).WithMany(p => p.AlumniSkills)
                .HasForeignKey(d => d.AlumniId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AlumniSki__Alumn__7D439ABD");

            entity.HasOne(d => d.Skill).WithMany(p => p.AlumniSkills)
                .HasForeignKey(d => d.SkillId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__AlumniSki__Skill__7E37BEF6");
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.ApplicationId).HasName("PK__Applicat__C93A4C99A5235144");

            entity.HasIndex(e => new { e.AlumniId, e.ApplicationStatusId }, "IX_Applications_Alumni_Status");

            entity.HasIndex(e => new { e.JobPostId, e.AlumniId }, "UQ_Applications_Job_Alumni").IsUnique();

            entity.Property(e => e.AppliedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Cvurl)
                .HasMaxLength(500)
                .HasColumnName("CVUrl");
            entity.Property(e => e.EmployerNote).HasMaxLength(1000);

            entity.HasOne(d => d.Alumni).WithMany(p => p.Applications)
                .HasForeignKey(d => d.AlumniId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Applicati__Alumn__70DDC3D8");

            entity.HasOne(d => d.ApplicationStatus).WithMany(p => p.Applications)
                .HasForeignKey(d => d.ApplicationStatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Applicati__Appli__71D1E811");

            entity.HasOne(d => d.JobPost).WithMany(p => p.Applications)
                .HasForeignKey(d => d.JobPostId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Applicati__JobPo__6FE99F9F");
        });

        modelBuilder.Entity<ApplicationStatus>(entity =>
        {
            entity.HasKey(e => e.ApplicationStatusId).HasName("PK__Applicat__E4F149EF883A3890");

            entity.HasIndex(e => e.StatusCode, "UQ__Applicat__6A7B44FC6B1C00AD").IsUnique();

            entity.Property(e => e.StatusCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.StatusName).HasMaxLength(80);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.CompanyId).HasName("PK__Companie__2D971CAC514B0393");

            entity.HasIndex(e => e.UserId, "UQ__Companie__1788CC4DC688B27A").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.CompanyName).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Industry).HasMaxLength(120);
            entity.Property(e => e.LogoUrl).HasMaxLength(500);
            entity.Property(e => e.TaxCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.VerificationStatus)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pending");
            entity.Property(e => e.Website).HasMaxLength(300);

            entity.HasOne(d => d.User).WithOne(p => p.Company)
                .HasForeignKey<Company>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Companies__UserI__571DF1D5");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.DepartmentId).HasName("PK__Departme__B2079BED2D1740DE");

            entity.HasIndex(e => e.DepartmentName, "UQ__Departme__D949CC3435719B6A").IsUnique();

            entity.Property(e => e.DepartmentName).HasMaxLength(150);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__Events__7944C810957C54C0");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.EventName).HasMaxLength(200);
            entity.Property(e => e.EventStatus)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Upcoming");
            entity.Property(e => e.Location).HasMaxLength(300);

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.Events)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Events__CreatedB__01142BA1");
        });

        modelBuilder.Entity<EventRegistration>(entity =>
        {
            entity.HasKey(e => e.EventRegistrationId).HasName("PK__EventReg__83225A9287246876");

            entity.HasIndex(e => new { e.EventId, e.UserId }, "UQ_EventRegistrations").IsUnique();

            entity.Property(e => e.AttendanceStatus)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Registered");
            entity.Property(e => e.RegisteredAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Event).WithMany(p => p.EventRegistrations)
                .HasForeignKey(d => d.EventId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__EventRegi__Event__09A971A2");

            entity.HasOne(d => d.User).WithMany(p => p.EventRegistrations)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__EventRegi__UserI__0A9D95DB");
        });

        modelBuilder.Entity<JobCategory>(entity =>
        {
            entity.HasKey(e => e.JobCategoryId).HasName("PK__JobCateg__302BAD2D9F98200F");

            entity.HasIndex(e => e.CategoryName, "UQ__JobCateg__8517B2E0EC23D53B").IsUnique();

            entity.Property(e => e.CategoryName).HasMaxLength(120);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<JobPost>(entity =>
        {
            entity.HasKey(e => e.JobPostId).HasName("PK__JobPosts__57689C3AF5B6AC05");

            entity.HasIndex(e => new { e.ApprovalStatus, e.IsPublished, e.Deadline }, "IX_JobPosts_Status_Deadline");

            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pending");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .IsUnicode(false)
                .HasDefaultValue("VND")
                .IsFixedLength();
            entity.Property(e => e.EmploymentType).HasMaxLength(50);
            entity.Property(e => e.Quantity).HasDefaultValue(1);
            entity.Property(e => e.SalaryMax).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.SalaryMin).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.WorkLocation).HasMaxLength(250);

            entity.HasOne(d => d.Company).WithMany(p => p.JobPosts)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__JobPosts__Compan__5FB337D6");

            entity.HasOne(d => d.JobCategory).WithMany(p => p.JobPosts)
                .HasForeignKey(d => d.JobCategoryId)
                .HasConstraintName("FK__JobPosts__JobCat__60A75C0F");
        });

        modelBuilder.Entity<Major>(entity =>
        {
            entity.HasKey(e => e.MajorId).HasName("PK__Majors__D5B8BF911652B6A0");

            entity.HasIndex(e => new { e.DepartmentId, e.MajorName }, "UQ_Majors").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.MajorName).HasMaxLength(150);

            entity.HasOne(d => d.Department).WithMany(p => p.Majors)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Majors__Departme__48CFD27E");
        });

        modelBuilder.Entity<Menu>(entity =>
        {
            entity.HasKey(e => e.MenuId).HasName("PK__Menus__C99ED230CD92BEC2");

            entity.Property(e => e.Icon).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.MenuName).HasMaxLength(100);
            entity.Property(e => e.MenuUrl).HasMaxLength(255);

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("FK_Menus_Parent");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("PK__Notifica__20CF2E12EC720361");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "IX_Notifications_User_Read");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.NotificationType)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.RelatedUrl).HasMaxLength(500);
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Notificat__UserI__10566F31");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1A2D2C8059");

            entity.HasIndex(e => e.RoleName, "UQ__Roles__8A2B6160FF7F7F7B").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(250);
            entity.Property(e => e.RoleName)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(e => e.SkillId).HasName("PK__Skills__DFA091872D8B9910");

            entity.HasIndex(e => e.SkillName, "UQ__Skills__B63C6571270C4E21").IsUnique();

            entity.Property(e => e.SkillName).HasMaxLength(100);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C2C33E465");

            entity.HasIndex(e => e.Email, "UQ__Users__A9D1053486E8168B").IsUnique();

            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId });

            entity.Property(e => e.AssignedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserRoles__RoleI__403A8C7D");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserRoles__UserI__3F466844");
        });

        modelBuilder.Entity<WorkExperience>(entity =>
        {
            entity.HasKey(e => e.WorkExperienceId).HasName("PK__WorkExpe__55A2B8893996EACE");

            entity.Property(e => e.CompanyName).HasMaxLength(200);
            entity.Property(e => e.JobTitle).HasMaxLength(150);

            entity.HasOne(d => d.Alumni).WithMany(p => p.WorkExperiences)
                .HasForeignKey(d => d.AlumniId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__WorkExper__Alumn__75A278F5");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
