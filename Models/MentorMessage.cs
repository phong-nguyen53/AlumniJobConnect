using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlumniJobConnect.Models;

[Table("MentorMessages", Schema = "dbo")]
public class MentorMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long MessageId { get; set; }

    public int ConversationId { get; set; }

    public int SenderUserId { get; set; }

    [Required]
    [StringLength(2000)]
    public string Content { get; set; } = "";

    public DateTime SentAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public int? JobPostId { get; set; }
}