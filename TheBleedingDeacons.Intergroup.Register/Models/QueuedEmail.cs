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
        public string To { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string From { get; set; } = string.Empty;

        // Optional Reply-To address. Set when the queueing caller wants
        // replies to land somewhere other than the From address — e.g.
        // compliance acceptance emails, where From stays as the
        // authenticated SMTP user (so SPF/DMARC checks pass) but replies
        // are routed to the configured compliance mailbox.
        // NULL when no override is needed (the common case).
        [MaxLength(200)]
        public string? ReplyTo { get; set; }

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
