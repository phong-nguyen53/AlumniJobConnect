using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class Skill
{
    public int SkillId { get; set; }

    public string SkillName { get; set; } = null!;

    public virtual ICollection<AlumniSkill> AlumniSkills { get; set; } = new List<AlumniSkill>();
}
