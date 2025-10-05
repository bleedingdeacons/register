using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TheBleedingDeacons.Intergroup.Register.Models
{
    [Table("QueuedEmails")]
    public class QueuedEmail
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(500)]
        public string To { get; set; }

        [Required, MaxLength(200)]
        public string Subject { get; set; }

        [Required]
        public string Body { get; set; }

        [Required, MaxLength(200)]
        public string From { get; set; }

        [MaxLength(500)]
        public string? Cc { get; set; }

        [MaxLength(500)]
        public string? Bcc { get; set; }

        public bool IsHtml { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? LastAttemptAt { get; set; }

        public int AttemptCount { get; set; } = 0;

        public int MaxRetries { get; set; } = 3;

        public string? LastError { get; set; }

        public EmailStatus Status { get; set; } = EmailStatus.Pending;

        // Store attachments as JSON if needed
        public string? AttachmentsJson { get; set; }
    }

    public enum EmailStatus
    {
        Pending = 0,
        Sending = 1,
        Sent = 2,
        Failed = 3,
        Cancelled = 4
    }
}
