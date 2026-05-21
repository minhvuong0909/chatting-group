using System;
using Microsoft.EntityFrameworkCore;
using ChatServer.Data.Models;

namespace RagChatbotSystem.DataAccess.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Room> Rooms { get; set; }
        public DbSet<RoomMember> RoomMembers { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<FileRecord> Files { get; set; }
        public DbSet<Call> Calls { get; set; }
        public DbSet<CallParticipant> CallParticipants { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Indexes
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<RoomMember>()
                .HasIndex(rm => new { rm.RoomId, rm.UserId })
                .IsUnique();

            modelBuilder.Entity<Message>()
                .HasIndex(m => m.RoomId);
            modelBuilder.Entity<Message>()
                .HasIndex(m => m.SentAt);

            modelBuilder.Entity<FileRecord>()
                .HasIndex(f => f.UploaderId);

            modelBuilder.Entity<Call>()
                .HasIndex(c => c.RoomId);
                
            modelBuilder.Entity<CallParticipant>()
                .HasIndex(cp => new { cp.CallId, cp.UserId })
                .IsUnique();

            // Configure Relationships

            // RoomMembers
            modelBuilder.Entity<RoomMember>()
                .HasOne(rm => rm.Room)
                .WithMany(r => r.RoomMembers)
                .HasForeignKey(rm => rm.RoomId);

            modelBuilder.Entity<RoomMember>()
                .HasOne(rm => rm.User)
                .WithMany(u => u.RoomMembers)
                .HasForeignKey(rm => rm.UserId);

            // Messages
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Room)
                .WithMany(r => r.Messages)
                .HasForeignKey(m => m.RoomId);

            modelBuilder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany(u => u.Messages)
                .HasForeignKey(m => m.SenderId);

            modelBuilder.Entity<Message>()
                .HasOne(m => m.File)
                .WithMany(f => f.Messages)
                .HasForeignKey(m => m.FileId)
                .IsRequired(false);

            // Files
            modelBuilder.Entity<FileRecord>()
                .HasOne(f => f.Uploader)
                .WithMany(u => u.UploadedFiles)
                .HasForeignKey(f => f.UploaderId);

            // Calls
            modelBuilder.Entity<Call>()
                .HasOne(c => c.Room)
                .WithMany(r => r.Calls)
                .HasForeignKey(c => c.RoomId);

            modelBuilder.Entity<Call>()
                .HasOne(c => c.InitiatedBy)
                .WithMany(u => u.InitiatedCalls)
                .HasForeignKey(c => c.InitiatedById);

            // CallParticipants
            modelBuilder.Entity<CallParticipant>()
                .HasOne(cp => cp.Call)
                .WithMany(c => c.Participants)
                .HasForeignKey(cp => cp.CallId);

            modelBuilder.Entity<CallParticipant>()
                .HasOne(cp => cp.User)
                .WithMany(u => u.CallParticipations)
                .HasForeignKey(cp => cp.UserId);
        }
    }

    public class AppDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
