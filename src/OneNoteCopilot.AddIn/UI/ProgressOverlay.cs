using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OneNoteCopilot.UI
{
    public class ProgressOverlay : Form
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Label _lblStatus;
        private readonly Label _lblHint;
        private readonly Label _lblThroughput;
        private readonly Button _btnCancel;
        private readonly System.Windows.Forms.Timer _animationTimer;
        private readonly System.Windows.Forms.Timer _throughputTimer;

        private int _dotCount;
        private string _baseStatus = "AI 正在思考";

        // Token throughput tracking. We approximate "tokens" by character
        // count (deepseek tokenizer ≈ 1 token / 1.5 char for English,
        // 1 token / 1 char for Chinese — close enough for a UX indicator).
        private DateTime _streamStart;
        private long _totalChars;
        private bool _throughputStarted;

        public CancellationToken Token
        {
            get { return _cts.Token; }
        }

        public static ProgressOverlay Show(Form owner)
        {
            ProgressOverlay overlay = new ProgressOverlay(owner);
            ((Form)overlay).Show(owner);
            return overlay;
        }

        public ProgressOverlay(Form owner = null)
        {
            Text = "AI 正在思考";
            ClientSize = new Size(360, 130);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            ShowInTaskbar = true;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            Opacity = 0.96;
            BackColor = Color.FromArgb(245, 248, 252);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            if (owner != null)
            {
                Owner = owner;
            }

            _lblStatus = new Label
            {
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(320, 26),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(42, 52, 65),
                Text = "AI 正在思考..."
            };

            _lblThroughput = new Label
            {
                AutoSize = false,
                Location = new Point(20, 42),
                Size = new Size(320, 18),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(80, 110, 150),
                Text = string.Empty
            };

            _lblHint = new Label
            {
                AutoSize = false,
                Location = new Point(20, 62),
                Size = new Size(320, 18),
                ForeColor = Color.FromArgb(110, 118, 130),
                Text = "可随时点击下方按钮取消"
            };

            // Larger, accent-colored cancel button so users always notice it.
            _btnCancel = new Button
            {
                Text = "取消生成",
                Size = new Size(150, 36),
                Location = new Point(105, 86),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderSize = 0;
            _btnCancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 35, 51);
            _btnCancel.FlatAppearance.MouseDownBackColor = Color.FromArgb(176, 30, 44);
            _btnCancel.Click += OnCancelClick;

            _animationTimer = new System.Windows.Forms.Timer { Interval = 450 };
            _animationTimer.Tick += OnAnimationTick;
            _animationTimer.Start();

            _throughputTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _throughputTimer.Tick += OnThroughputTick;
            _throughputTimer.Start();

            Controls.Add(_lblStatus);
            Controls.Add(_lblThroughput);
            Controls.Add(_lblHint);
            Controls.Add(_btnCancel);

            Shown += delegate { ForceForeground.Apply(this); };
            FormClosed += OnOverlayClosed;
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            _btnCancel.Enabled = false;
            _btnCancel.Text = "正在取消...";
            _btnCancel.BackColor = Color.FromArgb(150, 150, 150);
            _baseStatus = "正在取消";
            _lblStatus.Text = "正在取消...";
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            _dotCount++;
            if (_dotCount > 3)
            {
                _dotCount = 1;
            }

            string baseText = string.IsNullOrEmpty(_baseStatus) ? "AI 正在思考" : _baseStatus;
            _lblStatus.Text = baseText + new string('.', _dotCount);
        }

        private void OnThroughputTick(object sender, EventArgs e)
        {
            if (!_throughputStarted || _cts.IsCancellationRequested)
            {
                return;
            }

            double elapsed = (DateTime.UtcNow - _streamStart).TotalSeconds;
            if (elapsed < 0.2) return;

            double cps = _totalChars / elapsed;
            // Display as both chars/s and an approximate tokens/s so users
            // have an intuitive feel for how fast the stream is going.
            _lblThroughput.Text = string.Format(
                "已接收 {0} 字 · 约 {1:F1} 字/秒",
                _totalChars, cps);
        }

        /// <summary>
        /// Called by streaming code on every token. The first call starts the
        /// throughput timer; subsequent calls accumulate the character count.
        /// Safe to call from any thread.
        /// </summary>
        public void ReportTokens(int charCount)
        {
            if (IsDisposed || charCount <= 0) return;

            if (!_throughputStarted)
            {
                _throughputStarted = true;
                _streamStart = DateTime.UtcNow;
            }
            // Interlocked-ish: this is only called from the streaming
            // callback which is already serialized by the HTTP reader.
            _totalChars += charCount;
        }

        /// <summary>
        /// Updates the animated status text (e.g. "正在处理第 3/12 页").
        /// Safe to call from any thread.
        /// </summary>
        public void UpdateStatus(string text)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), text);
                return;
            }

            _baseStatus = string.IsNullOrWhiteSpace(text) ? "AI 正在思考" : text;
            _dotCount = 0;
            _lblStatus.Text = _baseStatus;
        }

        private void OnOverlayClosed(object sender, FormClosedEventArgs e)
        {
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _throughputTimer.Stop();
            _throughputTimer.Dispose();
            _cts.Dispose();
        }
    }
}
