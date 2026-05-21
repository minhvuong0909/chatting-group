using System;
using System.Windows;
using System.Windows.Threading;

namespace ChatClient
{
    public partial class CallWindow : Window
    {
        private DispatcherTimer _timer;
        private int _secondsElapsed;
        private bool _isVideo;

        public CallWindow(bool isVideo)
        {
            InitializeComponent();
            _isVideo = isVideo;
            
            if (_isVideo)
            {
                CallStatusText.Text = "Đang kết nối video...";
            }
            
            // Start mock connection timer
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }
        
        private void Timer_Tick(object? sender, EventArgs e)
        {
            _secondsElapsed++;
            
            if (_secondsElapsed == 3)
            {
                CallStatusText.Text = "Đang gọi nhóm";
                TimerText.Visibility = Visibility.Visible;
            }
            
            if (_secondsElapsed >= 3)
            {
                int s = _secondsElapsed - 3;
                TimeSpan ts = TimeSpan.FromSeconds(s);
                TimerText.Text = ts.ToString(@"mm\:ss");
            }
        }

        private void EndCall_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            this.Close();
        }
    }
}
