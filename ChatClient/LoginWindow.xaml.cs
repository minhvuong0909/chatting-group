using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ChatClient
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void JoinButton_Click(object sender, RoutedEventArgs e)
        {
            string userName = NameInputBox.Text.Trim();
            if (string.IsNullOrEmpty(userName))
            {
                MessageBox.Show("Vui lòng nhập tên của bạn!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Parse địa chỉ server — chấp nhận "host:port" hoặc chỉ "host"
            string serverInput = ServerInputBox.Text.Trim();
            if (string.IsNullOrEmpty(serverInput))
            {
                MessageBox.Show("Vui lòng nhập địa chỉ server.", "Thiếu thông tin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string host;
            int port;

            try
            {
                var parts = serverInput.Split(':');
                host = parts[0];
                port = parts.Length > 1 ? int.Parse(parts[1]) : 5000;

                if (port < 1 || port > 65535)
                    throw new Exception("Port không hợp lệ.");
            }
            catch
            {
                MessageBox.Show("Lỗi kết nối server", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // Mở cửa sổ chat và truyền tên user vào
            MainWindow mainWindow = new MainWindow(userName, host, port);
            bool connected = await mainWindow.ConnectToServerAsync();
            if (!connected)
            {
                mainWindow.Close();
                return;
            }

            mainWindow.Show();

            // Đóng cửa sổ đăng nhập
            this.Close();
        }
    }
}
