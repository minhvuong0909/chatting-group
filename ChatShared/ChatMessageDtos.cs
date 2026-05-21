using System;

namespace ChatGroup.Shared.DTOs
{
    public class ChatMessageDtos
    {
        public string SenderName { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string MessageType { get; set; } = "Text"; 
        public string? FileData { get; set; } 
        public string? FileName { get; set; }
        
        public string? FileUrl { get; set; }
        public string? ThumbnailBase64 { get; set; }
        
        public string? CallId { get; set; }
        
        // UI Binding properties
        public bool IsMyMessage { get; set; }
        public bool IsText => MessageType == "Text" || MessageType == "System";
        public bool IsImage => MessageType == "Image";
        public bool IsFile => MessageType == "File";
        public bool IsSticker => MessageType == "Sticker";
        public bool IsCall => MessageType == "CallStart" || MessageType == "VideoCallStart";
        
        public string? DisplayImageBase64 => !string.IsNullOrEmpty(ThumbnailBase64) ? ThumbnailBase64 : (!string.IsNullOrEmpty(FileData) ? FileData : FileUrl);
    }
}
