using System;
using System.Collections.Generic;

namespace ChatServer.Data.Models
{
    public class FileRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UploaderId { get; set; }
        public User Uploader { get; set; } = null!;
        public required string Filename { get; set; }
        public required string MimeType { get; set; }
        public long SizeBytes { get; set; }
        public required string StoragePath { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
