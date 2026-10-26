using System;
using System.Collections.Generic;

namespace AlumniJobConnect.Models;

public partial class Menu
{
    public int MenuId { get; set; }

    public string MenuName { get; set; } = null!;

    public string? MenuUrl { get; set; }

    public string? Icon { get; set; }

    public int? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Menu> InverseParent { get; set; } = new List<Menu>();

    public virtual Menu? Parent { get; set; }
}
