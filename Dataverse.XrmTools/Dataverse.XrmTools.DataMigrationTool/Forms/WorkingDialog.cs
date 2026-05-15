// System
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Forms
{
    public class WorkingDialog : Form
    {
        private readonly Label _messageLabel;
        private readonly ProgressBar _progressBar;
        private readonly Label _tipLabel;
        private readonly Button _abortButton;

        public event EventHandler AbortRequested;

        public WorkingDialog()
        {
            Text = "Working";
            Width = 560;
            Height = 250;
            MinimumSize = new Size(520, 230);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            BackColor = SystemColors.Window;

            _messageLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font(Font, FontStyle.Bold),
                Left = 24,
                Top = 22,
                Width = ClientSize.Width - 48,
                Height = 24,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleCenter
            };

            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Left = 80,
                Top = 68,
                Width = ClientSize.Width - 160,
                Height = 22,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            _tipLabel = new Label
            {
                Left = 24,
                Top = 112,
                Width = ClientSize.Width - 48,
                Height = 48,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = Color.FromArgb(72, 72, 72),
                TextAlign = ContentAlignment.TopCenter
            };

            _abortButton = new Button
            {
                Text = "Abort",
                Width = 96,
                Height = 28,
                Left = (ClientSize.Width - 96) / 2,
                Top = 174,
                Anchor = AnchorStyles.Bottom,
                UseVisualStyleBackColor = true
            };
            _abortButton.Click += (sender, args) =>
            {
                _abortButton.Enabled = false;
                _abortButton.Text = "Aborting...";
                AbortRequested?.Invoke(this, EventArgs.Empty);
            };

            Controls.Add(_messageLabel);
            Controls.Add(_progressBar);
            Controls.Add(_tipLabel);
            Controls.Add(_abortButton);
        }

        public void UpdateContent(string message, string tip)
        {
            _messageLabel.Text = string.IsNullOrWhiteSpace(message) ? "Working..." : message;
            _tipLabel.Text = string.IsNullOrWhiteSpace(tip) ? string.Empty : $"Tip: {tip}";
        }

        public void SetAbortEnabled(bool enabled)
        {
            _abortButton.Enabled = enabled;
            _abortButton.Text = enabled ? "Abort" : "Aborting...";
        }

        public void CenterOverOwner()
        {
            var owner = Owner;
            if (owner == null || owner.IsDisposed)
            {
                CenterToScreen();
                return;
            }

            Left = owner.Left + Math.Max(0, (owner.Width - Width) / 2);
            Top = owner.Top + Math.Max(0, (owner.Height - Height) / 2);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterOverOwner();
            _progressBar.Left = (ClientSize.Width - _progressBar.Width) / 2;
        }

        protected override void WndProc(ref Message m)
        {
            const int wmNcLButtonDown = 0x00A1;
            const int htCaption = 0x02;
            if (m.Msg == wmNcLButtonDown && m.WParam.ToInt32() == htCaption)
                return;

            base.WndProc(ref m);
        }
    }
}
