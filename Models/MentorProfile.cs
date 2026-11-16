using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlumniJobConnect.Models;

[Table("MentorProfiles", Schema = "dbo")]
public class MentorProfile
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int UserId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập chủ đề tư vấn.")]
    [StringLength(150)]
    public string Expertise { get; set; } = "";

    [StringLength(500)]
    public string? Introduction { get; set; }

    public bool IsAvailable { get; set; }
}