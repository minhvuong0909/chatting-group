using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ChatServer.Data.Models;
using ChatGroup.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using RagChatbotSystem.DataAccess.Data;

namespace ChatServer
{
    class Program
    {
        private const int DefaultPort = 5000;
        static List<TcpClient> _connectedClients = new List<TcpClient>();
        static object _lock = new object();

        static ConcurrentQueue<ChatMessageDtos> _messageBuffer = new ConcurrentQueue<ChatMessageDtos>();
        static CancellationTokenSource _cts = new CancellationTokenSource();
        static ManualResetEventSlim _flushCompleteEvent = new ManualResetEventSlim(false);
        static Guid _globalRoomId;
        static string _connString = "Host=localhost;Port=5432;Database=chatting_group;Username=postgres;Password=postgres";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== SERVER CHAT GROUP ===");

            var builder = WebApplication.CreateBuilder(args);
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrEmpty(connectionString))
            {
                _connString = connectionString;
            }

            await InitializeDatabaseAsync();

            int port = GetPortFromArgs(args);
            
            // http server
            Console.WriteLine("Đang khởi động HTTP File Server tại port 5001...");
            builder.WebHost.UseUrls("http://0.0.0.0:5001");

            // up file lớn
            builder.Services.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = long.MaxValue; 
            });
            builder.Services.Configure<FormOptions>(options =>
            {
                options.ValueLengthLimit = int.MaxValue;
                options.MultipartBodyLengthLimit = long.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            var app = builder.Build();

            string uploadPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UploadedFiles");
            if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

            // Cấu hình tải file tĩnh
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(uploadPath),
                RequestPath = "/files"
            });

            // API Upload file
            app.MapPost("/upload", async (HttpRequest request) =>
            {
                if (!request.HasFormContentType) return Results.BadRequest("Unsupported media type");
                
                var form = await request.ReadFormAsync();
                var file = form.Files.GetFile("file");
                
                if (file == null || file.Length == 0) return Results.BadRequest("Empty file");
                
                string uniqueName = Guid.NewGuid().ToString() + "_" + file.FileName;
                string physicalPath = Path.Combine(uploadPath, uniqueName);
                
                // Lưu streaming xuống ổ cứng 
                using (var stream = new FileStream(physicalPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                
                // Trả về đường dẫn để người dùng TCP tải
                string fileUrl = $"https:// 172.20.10.3/files/{uniqueName}";
                Console.WriteLine($"[HTTP] Đã nhận file {file.FileName} ({file.Length} bytes)");
                return Results.Ok(new { Url = fileUrl, Size = file.Length, Name = file.FileName });
            });

            // Chạy ngầm HTTP server
            _ = app.RunAsync();

            // start TCP server
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"TCP Server đã khởi động. Đang lắng nghe tại port {port}...");

            _ = Task.Run(() => BackgroundFlusherAsync(_cts.Token));

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                OnProcessExit(s, e);
            };

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    
                    lock (_lock)
                    {
                        _connectedClients.Add(client);
                    }
                    Console.WriteLine($"[+] Có client mới kết nối! Tổng số: {_connectedClients.Count}");

                    _ = Task.Run(() => HandleClientAsync(client));
                }
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                {
                    Console.WriteLine("Lỗi Server: " + ex.Message);
                }
            }
        }

        static async Task InitializeDatabaseAsync()
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(_connString);

            using var db = new AppDbContext(optionsBuilder.Options);
            await db.Database.EnsureCreatedAsync();
            
            var globalRoom = await db.Rooms.FirstOrDefaultAsync(r => r.Name == "Global Group Chat");
            if (globalRoom == null)
            {
                globalRoom = new Room
                {
                    Name = "Global Group Chat",
                    Type = "group"
                };
                db.Rooms.Add(globalRoom);
                await db.SaveChangesAsync();
            }
            _globalRoomId = globalRoom.Id;
            Console.WriteLine($"[DB] Global Room ID: {_globalRoomId}");
        }

        static int GetPortFromArgs(string[] args)
        {
            if (args.Length == 0) return DefaultPort;
            if (!int.TryParse(args[0], out int port) || port < 1 || port > 65535) return DefaultPort;
            return port;
        }

        static async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            StreamWriter writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };

            // save history
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseNpgsql(_connString);
                using var db = new AppDbContext(optionsBuilder.Options);

                var history = await db.Messages
                    .Include(m => m.Sender)
                    .Include(m => m.File)
                    .Where(m => m.RoomId == _globalRoomId)
                    .OrderByDescending(m => m.SentAt)
                    .Take(50) // Lấy 50 tin nhắn gần nhất
                    .ToListAsync();

                history.Reverse(); // Đảo ngược để gửi theo thứ tự thời gian cũ -> mới
                
                Console.WriteLine($"[Lịch sử] Đã lấy {history.Count} tin nhắn từ DB cho Client mới.");

                foreach (var msg in history)
                {
                    string fileUrl = msg.File?.StoragePath ?? "";
                    string messageType = msg.Type;
                    // Chuyển Enum DB sang DTO
                    if (messageType == "text") messageType = "Text";
                    else if (messageType == "image") messageType = "Image";
                    else if (messageType == "video") messageType = "Video";
                    else if (messageType == "file") messageType = "File";
                    else if (messageType == "sticker") messageType = "Sticker";
                    else if (messageType.StartsWith("call") || messageType.StartsWith("videocall")) messageType = "System";
                    
                    var dto = new ChatMessageDtos
                    {
                        SenderName = msg.Sender.Username,
                        Content = msg.Content,
                        Timestamp = msg.SentAt.ToLocalTime(),
                        MessageType = messageType,
                        FileName = msg.File?.Filename,
                        FileUrl = msg.File != null ? $"https://vexingly-circle-proofs.ngrok-free.dev/files/{msg.File.Filename}" : null
                    };
                    string json = JsonSerializer.Serialize(dto);
                    await writer.WriteLineAsync(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lỗi History]: {ex.Message}\n{ex.StackTrace}");
            }

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    string? messageJson = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(messageJson)) break;

                    try
                    {
                        var dto = JsonSerializer.Deserialize<ChatMessageDtos>(messageJson);
                        if (dto != null)
                        {
                            _messageBuffer.Enqueue(dto);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Lỗi parse JSON: {e.Message}");
                    }

                    await BroadcastMessageAsync(messageJson);
                }
            }
            catch
            {
                // Disconnected
            }
            finally
            {
                lock (_lock)
                {
                    _connectedClients.Remove(client);
                }
                Console.WriteLine($"[-] Một client đã thoát. Tổng số: {_connectedClients.Count}");
                client.Close();
            }
        }

        static async Task BroadcastMessageAsync(string message)
        {
            List<TcpClient> clientsToBroadcast;
            lock (_lock)
            {
                clientsToBroadcast = new List<TcpClient>(_connectedClients);
            }

            foreach (var client in clientsToBroadcast)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };
                    await writer.WriteLineAsync(message);
                }
                catch
                {
                    // ignore client disconnected
                }
            }
        }

        static async Task BackgroundFlusherAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith(_ => { });
                await FlushBufferToDatabaseAsync();
            }
        }

        static async Task FlushBufferToDatabaseAsync()
        {
            if (_messageBuffer.IsEmpty) return;

            var messagesToSave = new List<ChatMessageDtos>();
            while (messagesToSave.Count < 100 && _messageBuffer.TryDequeue(out var dto))
            {
                messagesToSave.Add(dto);
            }

            if (!messagesToSave.Any()) return;

            Console.WriteLine($"[DB] Bắt đầu lưu {messagesToSave.Count} tin nhắn vào cơ sở dữ liệu...");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(_connString);

            using var db = new AppDbContext(optionsBuilder.Options);

            foreach (var dto in messagesToSave)
            {
                try
                {
                    if (dto.SenderName == "Hệ thống" || dto.SenderName == "") continue;

                    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == dto.SenderName);
                    if (user == null)
                    {
                        user = new User
                        {
                            Username = dto.SenderName,
                            DisplayName = dto.SenderName
                        };
                        db.Users.Add(user);
                        await db.SaveChangesAsync(); 
                    }

                    var member = await db.RoomMembers.FirstOrDefaultAsync(rm => rm.RoomId == _globalRoomId && rm.UserId == user.Id);
                    if (member == null)
                    {
                        db.RoomMembers.Add(new RoomMember
                        {
                            RoomId = _globalRoomId,
                            UserId = user.Id
                        });
                    }

                    FileRecord? fileRecord = null;
                    // Xử lý lưu File Record từ fileUrl hoặc fileData legacy
                    if ((dto.MessageType == "Image" || dto.MessageType == "Video" || dto.MessageType == "File" || dto.MessageType == "Sticker") && (!string.IsNullOrEmpty(dto.FileData) || !string.IsNullOrEmpty(dto.FileUrl)))
                    {
                        fileRecord = new FileRecord
                        {
                            UploaderId = user.Id,
                            Filename = dto.FileName ?? "Unknown",
                            MimeType = "application/octet-stream",
                            SizeBytes = 0,
                            StoragePath = dto.FileUrl ?? ""
                        };
                        db.Files.Add(fileRecord);
                    }

                    string t = dto.MessageType.ToLower();
                    if (t != "text" && t != "image" && t != "video" && t != "file" && t != "sticker" && t != "callstart" && t != "videocallstart" && t != "calljoin" && t != "callleave" && t != "callend") t = "text";
                    if (t == "sticker") t = "image"; 

                    var entity = new Message
                    {
                        RoomId = _globalRoomId,
                        SenderId = user.Id,
                        Type = t,
                        Content = dto.Content,
                        SentAt = dto.Timestamp.ToUniversalTime(),
                        File = fileRecord
                    };
                    db.Messages.Add(entity);

                    // Xử lý Call Entities
                    if (!string.IsNullOrEmpty(dto.CallId) && Guid.TryParse(dto.CallId, out Guid callIdGuid))
                    {
                        if (t == "callstart" || t == "videocallstart")
                        {
                            db.Calls.Add(new Call
                            {
                                Id = callIdGuid,
                                RoomId = _globalRoomId,
                                InitiatedById = user.Id,
                                Status = "ongoing",
                                StartedAt = dto.Timestamp.ToUniversalTime()
                            });
                            db.CallParticipants.Add(new CallParticipant
                            {
                                CallId = callIdGuid,
                                UserId = user.Id,
                                JoinedAt = dto.Timestamp.ToUniversalTime()
                            });
                        }
                        else if (t == "calljoin")
                        {
                            db.CallParticipants.Add(new CallParticipant
                            {
                                CallId = callIdGuid,
                                UserId = user.Id,
                                JoinedAt = dto.Timestamp.ToUniversalTime()
                            });
                        }
                        else if (t == "callleave")
                        {
                            var p = await db.CallParticipants.FirstOrDefaultAsync(cp => cp.CallId == callIdGuid && cp.UserId == user.Id && cp.LeftAt == null);
                            if (p != null) p.LeftAt = dto.Timestamp.ToUniversalTime();
                        }
                        else if (t == "callend")
                        {
                            var c = await db.Calls.FirstOrDefaultAsync(c => c.Id == callIdGuid);
                            if (c != null)
                            {
                                c.Status = "ended";
                                c.EndedAt = dto.Timestamp.ToUniversalTime();
                                c.DurationSeconds = (int)(c.EndedAt.Value - c.StartedAt).TotalSeconds;
                            }
                            var p = await db.CallParticipants.FirstOrDefaultAsync(cp => cp.CallId == callIdGuid && cp.UserId == user.Id && cp.LeftAt == null);
                            if (p != null) p.LeftAt = dto.Timestamp.ToUniversalTime();
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[DB] Lỗi tạo Entity tin nhắn: {e.Message}");
                }
            }

            try 
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"[DB] Đã lưu thành công {messagesToSave.Count} tin nhắn.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Lỗi khi SaveChangesAsync: {ex.Message}");
            }
        }

        static void OnProcessExit(object? sender, EventArgs e)
        {
            Console.WriteLine("\n[HỆ THỐNG] Phát hiện yêu cầu tắt Server. Bắt đầu lưu lịch sử chat...");
            _cts.Cancel();

            try {
                FlushBufferToDatabaseAsync().GetAwaiter().GetResult();
            } catch (Exception ex) {
                Console.WriteLine($"Lỗi khi flush: {ex.Message}");
            }
            
            lock (_lock)
            {
                foreach (var client in _connectedClients)
                {
                    client.Close();
                }
            }

            Console.WriteLine("[HỆ THỐNG] Đã lưu lịch sử xong. Server đóng an toàn.");
            Environment.Exit(0);
        }
    }
}
