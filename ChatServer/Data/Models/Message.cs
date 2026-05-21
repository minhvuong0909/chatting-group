using System;

namespace ChatServer.Data.Models
{
    public class Message
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RoomId { get; set; }
        public Room Room { get; set; } = null!;
        public Guid SenderId { get; set; }
        public User Sender { get; set; } = null!;
        public required string Type { get; set; } // 'text', 'image', 'file', 'sticker'
        public string? Content { get; set; }
        public Guid? FileId { get; set; }
        public FileRecord? File { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }
}
