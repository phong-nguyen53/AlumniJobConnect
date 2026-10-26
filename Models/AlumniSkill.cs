using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class AlumniSkill
{
    public int AlumniId { get; set; }

    public int SkillId { get; set; }

    public string? ProficiencyLevel { get; set; }

    public virtual AlumniProfile Alumni { get; set; } = null!;

    public virtual Skill Skill { get; set; } = null!;
}
