using System;
using System.Collections.Generic;

namespace ChatServer.Data.Models
{
    public class Call
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RoomId { get; set; }
        public Room Room { get; set; } = null!;
        public Guid InitiatedById { get; set; }
        public User InitiatedBy { get; set; } = null!;
        public required string Status { get; set; } // 'ongoing', 'ended', 'missed'
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
        public int? DurationSeconds { get; set; }

        public ICollection<CallParticipant> Participants { get; set; } = new List<CallParticipant>();
    }
}
