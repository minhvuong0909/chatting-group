using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ChatGroup.Shared.DTOs;
using System.Net.Http;
using System.Windows.Data;
using System.Globalization;

namespace ChatClient
{
    public class Base64ToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && !string.IsNullOrEmpty(str))
            {
                try
                {
                    if (str.StartsWith("http://") || str.StartsWith("https://"))
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.UriSource = new Uri(str);
                        image.EndInit();
                        return image;
                    }
                    else
                    {
                        if (str.Contains(",")) str = str.Substring(str.IndexOf(",") + 1);
                        byte[] bytes = System.Convert.FromBase64String(str);
                        using var ms = new MemoryStream(bytes);
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = ms;
                        image.EndInit();
                        return image;
                    }
                }
                catch { return null; }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ProgressableStreamContent : HttpContent
    {
        private readonly HttpContent _content;
        private readonly int _bufferSize;
        private readonly Action<long, long> _progress;
        private readonly long _contentLength;

        public ProgressableStreamContent(HttpContent content, Action<long, long> progress)
        {
            _content = content;
            _bufferSize = 81920; 
            _progress = progress;
            _contentLength = content.Headers.ContentLength ?? 0;

            foreach (var header in content.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[_bufferSize];
            long uploaded = 0;

            using var contentStream = await _content.ReadAsStreamAsync();
            while (true)
            {
                var length = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (length <= 0) break;

                await stream.WriteAsync(buffer, 0, length);
                uploaded += length;
                _progress?.Invoke(uploaded, _contentLength);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _contentLength;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _content.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public partial class MainWindow : Window
    {
        public ObservableCollection<ChatMessageDtos> Messages { get; } = new();

        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly string _userName;
        private readonly string _serverHost;
        private readonly int _serverPort;

        private CancellationTokenSource _cts = new();

        private string? _pendingFilePath;
        private string? _pendingFileType; 

        public MainWindow(string userName, string serverHost, int serverPort)
        {
            InitializeComponent();

            _userName = userName;
            _serverHost = serverHost;
            _serverPort = serverPort;
            DataContext = this;

            CurrentUserLabel.Text = $"Đang đăng nhập dưới tên: {_userName}";

            EmojiItemsControl.ItemsSource = new[]
            {
                "😀","😃","😄","😁","😆","😅","😂","🤣","🥲","☺️",
                "😊","😇","🙂","🙃","😉","😌","😍","🥰","😘","😗",
                "😙","😚","😋","😛","😝","😜","🤪","🤨","🧐","🤓",
                "😎","🥸","🤩","🥳","😏","😒","😞","😔","😟","😕",
                "❤️","🔥","👍","👎","🙏","👏","🎉","✨","💯","💔"
            };
            
            string stickersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Stickers");
            if (Directory.Exists(stickersPath))
            {
                StickerItemsControl.ItemsSource = Directory.GetFiles(stickersPath, "*.png");
            }

            Closed += OnWindowClosed;
        }

        public async Task<bool> ConnectToServerAsync()
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_serverHost, _serverPort);

                var stream = _client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                UpdateSendButton();

                _ = ListenForMessagesAsync(_cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể kết nối đến server:\n{ex.Message}",
                    "Lỗi kết nối",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }
        }

        private async Task ListenForMessagesAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? json = await _reader!.ReadLineAsync();
                    if (json is null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Messages.Add(new ChatMessageDtos
                            {
                                SenderName = "Hệ thống",
                                Content = "Mất kết nối với server.",
                                Timestamp = DateTime.Now,
                                IsMyMessage = false
                            });
                        });
                        break;
                    }

                    ChatMessageDtos? msg = JsonSerializer.Deserialize<ChatMessageDtos>(json);
                    if (msg is null) continue;

                    msg.IsMyMessage = msg.SenderName == _userName;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Messages.Add(msg);
                        ChatScrollViewer.ScrollToEnd();
                    });
                }
            }
            catch (IOException)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Add(new ChatMessageDtos
                    {
                        SenderName = "Hệ thống",
                        Content = "⚠️ Mất kết nối với server.",
                        Timestamp = DateTime.Now,
                        IsMyMessage = false
                    });
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    MessageBox.Show($"Lỗi nhận tin:\n{ex.Message}", "Lỗi"));
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
            => await SendMessageAsync();

        private async void MessageInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }
        
        private void MessageInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateSendButton();
        }

        private async Task SendMessageAsync()
        {
            if (_writer is null) return;
            if (_client is null || !_client.Connected) return;

            string content = MessageInputBox.Text.Trim();
            
            if (string.IsNullOrEmpty(content) && _pendingFilePath == null) return;

            var msg = new ChatMessageDtos
            {
                SenderName = _userName,
                Timestamp = DateTime.Now
            };

            if (_pendingFilePath != null)
            {
                try
                {
                    msg.MessageType = _pendingFileType!;
                    msg.FileName = Path.GetFileName(_pendingFilePath);
                    msg.Content = content; 

                    if (msg.MessageType == "Image")
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(_pendingFilePath);
                        bmp.DecodePixelHeight = 100; 
                        bmp.EndInit();

                        var encoder = new JpegBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bmp));
                        using var ms = new MemoryStream();
                        encoder.Save(ms);
                        msg.ThumbnailBase64 = Convert.ToBase64String(ms.ToArray());
                    }

                    using var httpClient = new HttpClient();
                    httpClient.Timeout = Timeout.InfiniteTimeSpan; 
                    using var contentData = new MultipartFormDataContent();
                    await using var fileStream = new FileStream(_pendingFilePath, FileMode.Open, FileAccess.Read);
                    
                    var streamContent = new StreamContent(fileStream);
                    var progressContent = new ProgressableStreamContent(streamContent, (uploaded, total) => 
                    {
                        Application.Current.Dispatcher.InvokeAsync(() => 
                        {
                            if (total > 0)
                            {
                                UploadProgressBar.Value = (double)uploaded / total * 100;
                                double uploadedMb = uploaded / (1024.0 * 1024.0);
                                double totalMb = total / (1024.0 * 1024.0);
                                UploadProgressText.Text = $"Đang gửi: {uploadedMb:F1} MB / {totalMb:F1} MB";
                            }
                        });
                    });

                    contentData.Add(progressContent, "file", msg.FileName);

                    if (SendButton != null)
                    {
                        SendButton.IsEnabled = false;
                        SendButton.Content = "⏳";
                        UploadProgressBar.Visibility = Visibility.Visible;
                        UploadProgressText.Visibility = Visibility.Visible;
                        UploadProgressBar.Value = 0;
                    }

                    var response = await httpClient.PostAsync("https://172.20.10.3:5000/upload", contentData);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = await response.Content.ReadAsStringAsync();
                        var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseString);
                        msg.FileUrl = result.GetProperty("url").GetString();
                    }
                    else
                    {
                        MessageBox.Show("Lỗi upload file lên Server.", "Lỗi");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi xử lý file:\n{ex.Message}", "Lỗi");
                    return;
                }
                finally
                {
                    if (SendButton != null)
                    {
                        SendButton.IsEnabled = true;
                        SendButton.Content = "➤";
                    }
                }
            }
            else
            {
                msg.MessageType = "Text";
                msg.Content = content;
            }

            string json = JsonSerializer.Serialize(msg);

            try
            {
                await _writer.WriteLineAsync(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gửi tin thất bại:\n{ex.Message}", "Lỗi");
                Disconnect();
                return;
            }

            MessageInputBox.Clear();
            ClearPreview();
            MessageInputBox.Focus();
        }
        
        private async Task SendSystemMessageAsync(string systemContent)
        {
            if (_writer is null || _client is null || !_client.Connected) return;

            var msg = new ChatMessageDtos
            {
                SenderName = _userName,
                Timestamp = DateTime.Now,
                MessageType = "Text",
                Content = systemContent
            };
            
            string json = JsonSerializer.Serialize(msg);
            try
            {
                await _writer.WriteLineAsync(json);
            }
            catch
            {
            }
        }

        private async void VoiceCallButton_Click(object sender, RoutedEventArgs e)
        {
            string callId = Guid.NewGuid().ToString();
            await SendCallMessageAsync("CallStart", callId, "📞 Đã bắt đầu cuộc gọi thoại.");
            
            CallWindow callWindow = new CallWindow(false);
            callWindow.ShowDialog();
            
            await SendCallMessageAsync("CallEnd", callId, "Cuộc gọi thoại kết thúc.");
        }
        
        private async void VideoCallButton_Click(object sender, RoutedEventArgs e)
        {
            string callId = Guid.NewGuid().ToString();
            await SendCallMessageAsync("VideoCallStart", callId, "🎥 Đã bắt đầu cuộc gọi video.");
            
            CallWindow callWindow = new CallWindow(true);
            callWindow.ShowDialog();
            
            await SendCallMessageAsync("CallEnd", callId, "Cuộc gọi video kết thúc.");
        }

        private async void JoinCallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ChatMessageDtos msg && msg.CallId != null)
            {
                await SendCallMessageAsync("CallJoin", msg.CallId, "Đã tham gia cuộc gọi.");
                
                bool isVideo = msg.MessageType == "VideoCallStart";
                CallWindow callWindow = new CallWindow(isVideo);
                callWindow.ShowDialog();
                
                await SendCallMessageAsync("CallLeave", msg.CallId, "Đã rời khỏi cuộc gọi.");
            }
        }

        private async Task SendCallMessageAsync(string type, string callId, string content)
        {
            if (_writer is null || _client is null || !_client.Connected) return;
            try
            {
                var msg = new ChatMessageDtos
                {
                    SenderName = _userName,
                    Timestamp = DateTime.Now,
                    MessageType = type,
                    CallId = callId,
                    Content = content
                };
                string json = JsonSerializer.Serialize(msg);
                await _writer.WriteLineAsync(json);
            }
            catch { }
        }
        
        private void AttachImageButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg;*.gif;*.bmp)|*.png;*.jpeg;*.jpg;*.gif;*.bmp|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                ShowPreview(openFileDialog.FileName, "Image");
            }
        }

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                string ext = Path.GetExtension(openFileDialog.FileName).ToLower();
                if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".wmv" || ext == ".mov")
                {
                    ShowPreview(openFileDialog.FileName, "Video");
                }
                else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp")
                {
                    ShowPreview(openFileDialog.FileName, "Image");
                }
                else
                {
                    ShowPreview(openFileDialog.FileName, "File");
                }
            }
        }
        
        private void StickerButton_Click(object sender, RoutedEventArgs e)
            => StickerPopup.IsOpen = true;
            
        private async void Sticker_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            string imagePath = btn.Tag as string ?? "";
            StickerPopup.IsOpen = false;
            
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;
            if (_writer is null || _client is null || !_client.Connected) return;
            
            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(imagePath);
                var msg = new ChatMessageDtos
                {
                    SenderName = _userName,
                    Timestamp = DateTime.Now,
                    MessageType = "Sticker",
                    FileData = Convert.ToBase64String(fileBytes)
                };
                
                string json = JsonSerializer.Serialize(msg);
                await _writer.WriteLineAsync(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gửi sticker thất bại:\n{ex.Message}", "Lỗi");
            }
        }

        private void ShowPreview(string filePath, string type)
        {
            _pendingFilePath = filePath;
            _pendingFileType = type;
            
            PreviewArea.Visibility = Visibility.Visible;
            FileInfo fi = new FileInfo(filePath);
            PreviewFileName.Text = fi.Name;
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = fi.Length;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            PreviewFileSize.Text = String.Format("{0:0.##} {1}", len, sizes[order]);
            
            if (type == "Image")
            {
                try {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(filePath);
                    bitmap.DecodePixelHeight = 60; 
                    bitmap.EndInit();
                    PreviewImage.Source = bitmap;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewFileIcon.Visibility = Visibility.Collapsed;
                    PreviewVideoContainer.Visibility = Visibility.Collapsed;
                } catch {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewFileIcon.Visibility = Visibility.Visible;
                    PreviewVideoContainer.Visibility = Visibility.Collapsed;
                }
            }
            else if (type == "Video")
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewFileIcon.Visibility = Visibility.Collapsed;
                PreviewVideoContainer.Visibility = Visibility.Visible;
                PreviewVideo.Source = new Uri(filePath);
            }
            else
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewFileIcon.Visibility = Visibility.Visible;
                PreviewVideoContainer.Visibility = Visibility.Collapsed;
            }
            
            UpdateSendButton();
        }
        
        private void CancelPreview_Click(object sender, RoutedEventArgs e)
        {
            ClearPreview();
        }
        
        private void ClearPreview()
        {
            _pendingFilePath = null;
            _pendingFileType = null;
            PreviewArea.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            PreviewVideo.Source = null;
            UploadProgressBar.Visibility = Visibility.Collapsed;
            UploadProgressText.Visibility = Visibility.Collapsed;
            UpdateSendButton();
        }
        
        private void MessageVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MediaElement media)
            {
                media.Position = TimeSpan.FromMilliseconds(1);
            }
        }

        private async void MessageElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ChatMessageDtos msg)
            {
                await PreviewMediaInline(msg);
            }
        }

        private async Task PreviewMediaInline(ChatMessageDtos msg)
        {
            try
            {
                string fileName = msg.FileName ?? (msg.IsImage ? "preview_image.jpg" : "preview_video.mp4");
                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                if (!string.IsNullOrEmpty(msg.FileUrl))
                {
                    // Tải xuống file (video/ảnh lớn) vào Temp để mở
                    if (!System.IO.File.Exists(tempPath)) 
                    {
                        using var httpClient = new HttpClient();
                        httpClient.Timeout = Timeout.InfiniteTimeSpan;
                        using var response = await httpClient.GetAsync(msg.FileUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        await using var streamToReadFrom = await response.Content.ReadAsStreamAsync();
                        await using var streamToWriteTo = System.IO.File.Open(tempPath, System.IO.FileMode.Create);
                        await streamToReadFrom.CopyToAsync(streamToWriteTo);
                    }
                }
                else if (!string.IsNullOrEmpty(msg.FileData) || !string.IsNullOrEmpty(msg.DisplayImageBase64))
                {
                    string base64 = string.IsNullOrEmpty(msg.FileData) ? msg.DisplayImageBase64 : msg.FileData;
                    byte[] fileBytes = Convert.FromBase64String(base64);
                    await System.IO.File.WriteAllBytesAsync(tempPath, fileBytes);
                }

                // Mở file bằng phần mềm xem ảnh/video mặc định của Windows
                var psi = new System.Diagnostics.ProcessStartInfo(tempPath)
                {
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể mở xem trước: {ex.Message}", "Lỗi Preview", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        private async void FileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ChatMessageDtos msg)
            {
                await DownloadAndOpenFile(msg);
            }
        }

        private async Task DownloadAndOpenFile(ChatMessageDtos msg)
        {
            if (string.IsNullOrEmpty(msg.FileName)) return;

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                FileName = msg.FileName,
                Filter = "All files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    if (!string.IsNullOrEmpty(msg.FileUrl))
                    {
                        using var httpClient = new HttpClient();
                        httpClient.Timeout = Timeout.InfiniteTimeSpan; 
                        using var response = await httpClient.GetAsync(msg.FileUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        await using var streamToReadFrom = await response.Content.ReadAsStreamAsync();
                        await using var streamToWriteTo = File.Open(saveFileDialog.FileName, FileMode.Create);
                        await streamToReadFrom.CopyToAsync(streamToWriteTo);
                    }
                    else if (!string.IsNullOrEmpty(msg.FileData))
                    {
                        byte[] fileBytes = Convert.FromBase64String(msg.FileData);
                        await File.WriteAllBytesAsync(saveFileDialog.FileName, fileBytes);
                    }
                    else
                    {
                        return;
                    }
                    
                    MessageBox.Show("Tải file thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    var p = new System.Diagnostics.Process();
                    p.StartInfo = new System.Diagnostics.ProcessStartInfo(saveFileDialog.FileName)
                    {
                        UseShellExecute = true
                    };
                    p.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi lưu/mở file:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EmojiButton_Click(object sender, RoutedEventArgs e)
            => EmojiPopup.IsOpen = true;

        private void Emoji_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;

            string emoji = btn.Content.ToString() ?? "";
            MessageInputBox.Text += emoji;
            MessageInputBox.CaretIndex = MessageInputBox.Text.Length;

            EmojiPopup.IsOpen = false;
            MessageInputBox.Focus();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            _cts.Cancel();
            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Close();
        }

        private void UpdateSendButton()
        {
            bool hasText = !string.IsNullOrWhiteSpace(MessageInputBox.Text);
            bool hasFile = _pendingFilePath != null;
            
            if (SendButton != null)
                SendButton.IsEnabled = (hasText || hasFile) && (_client != null && _client.Connected);
        }
    }
}
