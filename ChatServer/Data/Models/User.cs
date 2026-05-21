using System;
using System.Collections.Generic;

namespace ChatServer.Data.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Username { get; set; }
        public required string DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeen { get; set; }

        public ICollection<RoomMember> RoomMembers { get; set; } = new List<RoomMember>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<FileRecord> UploadedFiles { get; set; } = new List<FileRecord>();
        public ICollection<Call> InitiatedCalls { get; set; } = new List<Call>();
        public ICollection<CallParticipant> CallParticipations { get; set; } = new List<CallParticipant>();
    }
}
