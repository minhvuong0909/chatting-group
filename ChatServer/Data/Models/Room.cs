using System;
using System.Collections.Generic;

namespace ChatServer.Data.Models
{
    public class Room
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Name { get; set; }
        public required string Type { get; set; } // 'group' or 'private'
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<RoomMember> RoomMembers { get; set; } = new List<RoomMember>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<Call> Calls { get; set; } = new List<Call>();
    }
}
