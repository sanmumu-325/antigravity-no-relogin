using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgyManager
{
    internal static class Theme
    {
        public static readonly Color Page = Color.FromArgb(244, 245, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Text = Color.FromArgb(31, 35, 51);
        public static readonly Color Muted = Color.FromArgb(108, 116, 138);
        public static readonly Color Line = Color.FromArgb(228, 231, 239);
        public static readonly Color Accent = Color.FromArgb(108, 76, 241);
        public static readonly Color AccentDark = Color.FromArgb(88, 58, 214);
        public static readonly Color AccentSoft = Color.FromArgb(238, 234, 255);
        public static readonly Color Green = Color.FromArgb(34, 160, 107);
        public static readonly Color Amber = Color.FromArgb(224, 161, 0);
        public static readonly Color Red = Color.FromArgb(214, 69, 69);
        public static readonly Color Track = Color.FromArgb(236, 238, 245);

        public static Font Font(float size)
        {
            return Font(size, FontStyle.Regular);
        }

        public static Font Font(float size, FontStyle style)
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, style);
            }
            catch
            {
                return new Font("Segoe UI", size, style);
            }
        }

        public static Color QuotaColor(double? remaining)
        {
            if (!remaining.HasValue) return Color.FromArgb(178, 184, 200);
            if (remaining.Value <= 0.2) return Red;
            if (remaining.Value <= 0.5) return Amber;
            return Green;
        }
    }

    internal static class DrawHelper
    {
        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class GradientHeader : Panel
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle rect = ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                Color.FromArgb(38, 27, 76), Color.FromArgb(108, 76, 241), 18f))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            {
                e.Graphics.FillEllipse(glow, rect.Width - 220, -90, 320, 240);
            }
        }
    }

    internal sealed class RoundButton : Button
    {
        private Color normalColor;
        private Color hoverColor;
        private Color pressedColor;
        private bool hovered;
        private bool pressed;
        private int radius = 10;

        public RoundButton(string text, Color normal, Color hover, Color pressed, Color fore)
        {
            Text = text;
            normalColor = normal;
            hoverColor = hover;
            pressedColor = pressed;
            ForeColor = fore;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Font(9f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            Height = 36;
        }

        public int Radius
        {
            get { return radius; }
            set { radius = value; UpdateRegion(); Invalidate(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = DrawHelper.RoundedRect(
                new Rectangle(0, 0, Width, Height), radius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = pressed ? pressedColor : (hovered ? hoverColor : normalColor);
            if (!Enabled) color = Color.FromArgb(220, 222, 230);
            using (GraphicsPath path = DrawHelper.RoundedRect(
                new Rectangle(0, 0, Width - 1, Height - 1), radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                e.Graphics.FillPath(brush, path);
            }
            Color fore = Enabled ? ForeColor : Color.FromArgb(140, 145, 160);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class QuotaRow : Control
    {
        private Font titleFont;
        private Font smallFont;
        private Font percentFont;
        private string title = "额度";
        private double? remaining;
        private DateTimeOffset? reset;
        private bool disabled;

        public QuotaRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.White;
            Height = 50;
            titleFont = Theme.Font(8.8f);
            smallFont = Theme.Font(7.8f);
            percentFont = Theme.Font(8.8f, FontStyle.Bold);
        }

        public void SetValue(string caption, double? value, DateTimeOffset? resetTime, bool isDisabled)
        {
            title = caption;
            remaining = value;
            reset = resetTime;
            disabled = isDisabled;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.Clear(Color.White);

            int right = Width - 2;
            string percentText = remaining.HasValue
                ? Math.Round(remaining.Value * 100).ToString("0") + "%"
                : "—";
            Size percentSize = TextRenderer.MeasureText(percentText, percentFont);
            TextRenderer.DrawText(e.Graphics, title, titleFont,
                new Rectangle(0, 0, Math.Max(10, Width - percentSize.Width - 12), 20),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, percentText, percentFont,
                new Rectangle(Width - percentSize.Width - 2, 0, percentSize.Width + 2, 20),
                remaining.HasValue ? Theme.QuotaColor(remaining) : Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            Rectangle track = new Rectangle(0, 20, Math.Max(10, Width), 9);
            using (GraphicsPath trackPath = DrawHelper.RoundedRect(track, 4))
            using (SolidBrush trackBrush = new SolidBrush(Theme.Track))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
            }
            if (remaining.HasValue && remaining.Value > 0)
            {
                int fillWidth = (int)Math.Round(track.Width * Math.Max(0, Math.Min(1, remaining.Value)));
                if (fillWidth < 8) fillWidth = 8;
                Rectangle fill = new Rectangle(track.Left, track.Top, Math.Min(fillWidth, track.Width), track.Height);
                using (GraphicsPath fillPath = DrawHelper.RoundedRect(fill, 4))
                using (SolidBrush fillBrush = new SolidBrush(Theme.QuotaColor(remaining)))
                {
                    e.Graphics.FillPath(fillBrush, fillPath);
                }
            }

            string resetText = remaining.HasValue ? FormatReset(reset) : "点击刷新额度";
            if (disabled) resetText = "当前套餐不可用";
            TextRenderer.DrawText(e.Graphics, resetText, smallFont,
                new Rectangle(0, 31, Width, 18), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public static string FormatReset(DateTimeOffset? reset)
        {
            if (!reset.HasValue) return "重置时间未知";
            TimeSpan span = reset.Value.ToLocalTime() - DateTimeOffset.Now;
            if (span.TotalSeconds <= 0) return "即将重置";
            if (span.TotalDays >= 1)
                return string.Format("{0}天{1}小时后重置", (int)span.TotalDays, span.Hours);
            if (span.TotalHours >= 1)
                return string.Format("{0}小时{1}分后重置", (int)span.TotalHours, span.Minutes);
            return string.Format("{0}分钟后重置", Math.Max(1, span.Minutes));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (titleFont != null) titleFont.Dispose();
                if (smallFont != null) smallFont.Dispose();
                if (percentFont != null) percentFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class QuotaGroupView : Panel
    {
        private Label header;
        private QuotaRow firstRow;
        private QuotaRow secondRow;
        private string emptyText = "暂无数据";

        public QuotaGroupView(string headerText)
        {
            BackColor = Color.White;
            header = new Label();
            header.Text = headerText;
            header.Font = Theme.Font(8.6f, FontStyle.Bold);
            header.ForeColor = Theme.Text;
            header.AutoSize = false;
            header.BackColor = Color.White;
            header.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(header);

            firstRow = new QuotaRow();
            secondRow = new QuotaRow();
            Controls.Add(firstRow);
            Controls.Add(secondRow);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetGroup(QuotaGroup group)
        {
            if (group == null)
            {
                SetEmpty(emptyText);
                return;
            }
            header.Text = group.IsGemini() ? "Gemini 模型" : (group.IsThirdParty() ? "Claude / GPT 模型" : group.Name);
            QuotaBucket fiveHour = FindWindow(group, "5h");
            QuotaBucket weekly = FindWindow(group, "weekly");
            if (fiveHour == null && weekly == null)
            {
                if (group.Buckets.Count > 0) fiveHour = group.Buckets[0];
                if (group.Buckets.Count > 1) weekly = group.Buckets[1];
            }
            Apply(firstRow, fiveHour, "额度");
            secondRow.Visible = weekly != null;
            if (weekly != null) Apply(secondRow, weekly, "额度");
            PerformLayout();
        }

        public void SetEmpty(string text)
        {
            firstRow.SetValue("额度", null, null, false);
            secondRow.Visible = false;
            PerformLayout();
        }

        public void SetUnavailable()
        {
            firstRow.SetValue("额度", null, null, true);
            secondRow.Visible = false;
            PerformLayout();
        }

        private static void Apply(QuotaRow row, QuotaBucket bucket, string fallbackTitle)
        {
            if (bucket == null)
            {
                row.SetValue(fallbackTitle, null, null, false);
                return;
            }
            row.SetValue(WindowTitle(bucket), bucket.Remaining, bucket.Reset, bucket.Disabled);
        }

        private static string WindowTitle(QuotaBucket bucket)
        {
            if (bucket.Window == "5h") return "5 小时";
            if (bucket.Window == "weekly") return "每周";
            if (bucket.Window == "requests") return "按请求（每日）";
            if (bucket.Window == "pool") return "共享池";
            if (!string.IsNullOrEmpty(bucket.DisplayName)) return bucket.DisplayName;
            return "额度";
        }

        private static QuotaBucket FindWindow(QuotaGroup group, string window)
        {
            foreach (QuotaBucket bucket in group.Buckets)
            {
                if (string.Equals(bucket.Window, window, StringComparison.OrdinalIgnoreCase))
                    return bucket;
            }
            return null;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (header == null || firstRow == null || secondRow == null) return;
            int width = Math.Max(10, ClientSize.Width);
            header.SetBounds(0, 0, width, 20);
            if (secondRow.Visible)
            {
                firstRow.SetBounds(0, 22, width, 50);
                secondRow.SetBounds(0, 74, width, 50);
            }
            else
            {
                firstRow.SetBounds(0, 30, width, 50);
            }
        }
    }

    internal sealed class AccountCard : Panel
    {
        private Profile profile;
        private bool active;
        private Label emailLabel;
        private Label badge;
        private Label planLabel;
        private Label savedLabel;
        private Label messageLabel;
        private Label footerLabel;
        private QuotaGroupView geminiView;
        private QuotaGroupView thirdView;
        private RoundButton switchButton;
        private RoundButton refreshButton;
        private RoundButton moreButton;
        private Action<Profile> switchAction;
        private Action<Profile> refreshAction;
        private Action<Profile> renameAction;
        private Action<Profile> deleteAction;

        public AccountCard(Profile profile, bool active, Action<Profile> onSwitch,
            Action<Profile> onRefresh, Action<Profile> onRename, Action<Profile> onDelete)
        {
            this.profile = profile;
            this.active = active;
            switchAction = onSwitch;
            refreshAction = onRefresh;
            renameAction = onRename;
            deleteAction = onDelete;
            BackColor = Theme.Card;
            DoubleBuffered = true;
            Height = 236;
            Padding = new Padding(18, 14, 18, 12);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            BuildUi();
            UpdateFromProfile();
            UpdateRegion();
        }

        public Profile ProfileData
        {
            get { return profile; }
        }

        private void BuildUi()
        {
            emailLabel = new Label();
            emailLabel.Font = Theme.Font(11.4f, FontStyle.Bold);
            emailLabel.ForeColor = Theme.Text;
            emailLabel.AutoSize = false;
            emailLabel.BackColor = Color.Transparent;
            emailLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(emailLabel);

            badge = new Label();
            badge.Text = "当前使用";
            badge.Font = Theme.Font(7.6f, FontStyle.Bold);
            badge.ForeColor = Color.White;
            badge.BackColor = Theme.Green;
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.AutoSize = false;
            badge.Visible = false;
            Controls.Add(badge);

            planLabel = new Label();
            planLabel.Font = Theme.Font(8.8f);
            planLabel.ForeColor = Theme.Muted;
            planLabel.AutoSize = false;
            planLabel.BackColor = Color.Transparent;
            planLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(planLabel);

            savedLabel = new Label();
            savedLabel.Font = Theme.Font(8f);
            savedLabel.ForeColor = Color.FromArgb(148, 154, 172);
            savedLabel.AutoSize = false;
            savedLabel.BackColor = Color.Transparent;
            savedLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(savedLabel);

            messageLabel = new Label();
            messageLabel.Font = Theme.Font(8.2f);
            messageLabel.ForeColor = Theme.Muted;
            messageLabel.AutoSize = false;
            messageLabel.BackColor = Color.Transparent;
            messageLabel.TextAlign = ContentAlignment.MiddleLeft;
            messageLabel.AutoEllipsis = true;
            Controls.Add(messageLabel);

            footerLabel = new Label();
            footerLabel.Font = Theme.Font(7.8f);
            footerLabel.ForeColor = Color.FromArgb(154, 160, 176);
            footerLabel.AutoSize = false;
            footerLabel.BackColor = Color.Transparent;
            footerLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(footerLabel);

            geminiView = new QuotaGroupView("Gemini 模型");
            thirdView = new QuotaGroupView("Claude / GPT 模型");
            Controls.Add(geminiView);
            Controls.Add(thirdView);

            switchButton = new RoundButton("切换", Theme.Accent, Theme.AccentDark,
                Color.FromArgb(72, 44, 190), Color.White);
            switchButton.Width = 78;
            switchButton.Click += delegate
            {
                if (switchAction != null) switchAction(profile);
            };
            Controls.Add(switchButton);

            refreshButton = new RoundButton("刷新", Theme.AccentSoft, Color.FromArgb(225, 219, 255),
                Color.FromArgb(210, 202, 250), Theme.AccentDark);
            refreshButton.Width = 62;
            refreshButton.Height = 32;
            refreshButton.Font = Theme.Font(8.6f, FontStyle.Bold);
            refreshButton.Click += delegate
            {
                if (refreshAction != null) refreshAction(profile);
            };
            Controls.Add(refreshButton);

            moreButton = new RoundButton("⋯", Color.FromArgb(246, 247, 251),
                Color.FromArgb(235, 237, 244), Color.FromArgb(226, 228, 238), Theme.Muted);
            moreButton.Width = 30;
            moreButton.Height = 32;
            moreButton.Radius = 8;
            moreButton.Font = Theme.Font(12f, FontStyle.Bold);
            moreButton.Click += MoreButton_Click;
            Controls.Add(moreButton);
        }

        private void MoreButton_Click(object sender, EventArgs e)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Theme.Font(9f);
            ToolStripMenuItem rename = new ToolStripMenuItem("重命名");
            rename.Click += delegate { if (renameAction != null) renameAction(profile); };
            ToolStripMenuItem copy = new ToolStripMenuItem("复制邮箱");
            copy.Click += delegate
            {
                if (!string.IsNullOrEmpty(profile.Email)) Clipboard.SetText(profile.Email);
            };
            ToolStripMenuItem delete = new ToolStripMenuItem("删除账号配置");
            delete.Click += delegate { if (deleteAction != null) deleteAction(profile); };
            menu.Items.Add(rename);
            menu.Items.Add(copy);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(delete);
            menu.Show(moreButton, new Point(0, moreButton.Height + 2));
        }

        public void UpdateFromProfile()
        {
            emailLabel.Text = string.IsNullOrEmpty(profile.Email) ? profile.DisplayName : profile.Email;
            badge.Visible = active;
            planLabel.Text = string.IsNullOrEmpty(profile.Plan) ? "套餐信息待查询" : profile.Plan;
            savedLabel.Text = FormatSaved(profile.SavedAt);
            if (active)
            {
                switchButton.Text = "使用中";
                switchButton.Enabled = false;
            }
            else
            {
                switchButton.Text = "切换";
                switchButton.Enabled = true;
            }
            if (profile.Quota != null)
            {
                UpdateQuota(profile.Quota);
            }
            else
            {
                geminiView.SetEmpty("Gemini 额度");
                thirdView.SetEmpty("Claude / GPT 额度");
                footerLabel.Text = "尚未查询额度，点“刷新”获取";
            }
            PerformLayout();
        }

        public void UpdateQuota(QuotaSnapshot snapshot)
        {
            if (snapshot == null)
            {
                profile.Quota = null;
                geminiView.SetEmpty("Gemini 额度");
                thirdView.SetEmpty("Claude / GPT 额度");
                return;
            }
            if (!string.IsNullOrEmpty(snapshot.Error) && snapshot.Groups.Count == 0)
            {
                QuotaSnapshot previous = profile.Quota;
                if (previous == null || previous.Groups.Count == 0)
                {
                    geminiView.SetEmpty("Gemini 额度");
                    thirdView.SetEmpty("Claude / GPT 额度");
                }
                messageLabel.Text = snapshot.Error;
                messageLabel.ForeColor = Theme.Red;
                footerLabel.Text = "额度刷新失败";
                profile.Quota = snapshot;
                return;
            }
            profile.Quota = snapshot;
            if (!string.IsNullOrEmpty(snapshot.Plan)) planLabel.Text = snapshot.Plan;
            QuotaGroup gemini = snapshot.FindGemini();
            QuotaGroup third = snapshot.FindThirdParty();
            if (gemini != null) geminiView.SetGroup(gemini);
            else geminiView.SetUnavailable();
            if (third != null) thirdView.SetGroup(third);
            else thirdView.SetUnavailable();
            if (!string.IsNullOrEmpty(snapshot.Error))
            {
                messageLabel.Text = snapshot.Error;
                messageLabel.ForeColor = Theme.Red;
            }
            else if (!string.IsNullOrEmpty(snapshot.Warning))
            {
                messageLabel.Text = snapshot.Warning;
                messageLabel.ForeColor = Theme.Amber;
            }
            else
            {
                messageLabel.Text = "";
            }
            footerLabel.Text = snapshot.FetchedAt.HasValue
                ? "更新于 " + snapshot.FetchedAt.Value.ToLocalTime().ToString("HH:mm") +
                  (snapshot.Source != null ? " · " + snapshot.Source : "")
                : "额度已更新";
        }

        public void SetBusy(bool busy)
        {
            refreshButton.Enabled = !busy;
            refreshButton.Text = busy ? "查询中" : "刷新";
        }

        public void SetActive(bool value)
        {
            active = value;
            UpdateFromProfile();
            Invalidate();
        }

        private static string FormatSaved(string savedAt)
        {
            DateTimeOffset parsed;
            if (!string.IsNullOrEmpty(savedAt) &&
                DateTimeOffset.TryParse(savedAt, out parsed))
            {
                return "保存于 " + parsed.ToLocalTime().ToString("MM-dd HH:mm");
            }
            return "";
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformLayout();
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = DrawHelper.RoundedRect(
                new Rectangle(0, 0, Width, Height), 14))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (emailLabel == null || moreButton == null || geminiView == null) return;
            int width = ClientSize.Width;
            int right = width - 18;
            moreButton.SetBounds(right - 30, 14, 30, 30);
            refreshButton.SetBounds(right - 30 - 8 - 62, 13, 62, 32);
            switchButton.SetBounds(right - 30 - 8 - 62 - 8 - 78, 12, 78, 34);
            int emailWidth = Math.Max(100, Math.Min(300, width - 18 - 230));
            emailLabel.SetBounds(18, 12, emailWidth, 24);
            emailLabel.AutoEllipsis = true;
            int badgeLeft = 18 + Math.Min(emailWidth,
                TextRenderer.MeasureText(emailLabel.Text, emailLabel.Font).Width + 10);
            badge.SetBounds(badgeLeft, 15, 62, 20);
            planLabel.SetBounds(18, 40, Math.Max(80, width - 220), 18);
            savedLabel.SetBounds(Math.Max(18, width - 200), 40, 182, 18);
            messageLabel.SetBounds(18, 60, Math.Max(80, width - 36), 16);

            int gap = 14;
            int columnWidth = Math.Max(160, (width - 36 - gap) / 2);
            geminiView.SetBounds(18, 82, columnWidth, 126);
            thirdView.SetBounds(18 + columnWidth + gap, 82, columnWidth, 126);
            footerLabel.SetBounds(18, Height - 24, Math.Max(80, width - 36), 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle shadow = new Rectangle(1, 2, Width - 2, Height - 3);
            using (GraphicsPath shadowPath = DrawHelper.RoundedRect(shadow, 14))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(14, 30, 24, 80)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }
            Rectangle body = new Rectangle(0, 0, Width - 1, Height - 2);
            using (GraphicsPath path = DrawHelper.RoundedRect(body, 14))
            using (SolidBrush brush = new SolidBrush(Theme.Card))
            {
                e.Graphics.FillPath(brush, path);
                using (Pen pen = new Pen(active ? Theme.Accent : Theme.Line, active ? 2f : 1f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }
    }

    internal sealed class PromptForm : Form
    {
        private TextBox input;

        public PromptForm(string title, string label, string value)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 150);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = Theme.Font(9.5f);
            Label caption = new Label();
            caption.Text = label;
            caption.SetBounds(20, 18, 380, 24);
            Controls.Add(caption);
            input = new TextBox();
            input.Text = value ?? "";
            input.SetBounds(20, 50, 380, 28);
            Controls.Add(input);
            RoundButton ok = new RoundButton("确定", Theme.Accent, Theme.AccentDark,
                Color.FromArgb(72, 44, 190), Color.White);
            ok.SetBounds(230, 100, 80, 34);
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);
            RoundButton cancel = new RoundButton("取消", Color.FromArgb(242, 243, 248),
                Color.FromArgb(232, 234, 241), Color.FromArgb(222, 225, 235), Theme.Muted);
            cancel.SetBounds(320, 100, 80, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string ValueText
        {
            get { return input.Text.Trim(); }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly ProfileStore store = new ProfileStore();
        private readonly QuotaService quotaService = new QuotaService();
        private Panel header;
        private Label headerTitle;
        private Label headerSubtitle;
        private FlowLayoutPanel cardsPanel;
        private Panel emptyPanel;
        private Label statusLabel;
        private RoundButton saveButton;
        private RoundButton refreshButton;
        private RoundButton openButton;
        private System.Windows.Forms.Timer autoTimer;
        private List<Profile> profiles = new List<Profile>();
        private Dictionary<string, AccountCard> cardMap = new Dictionary<string, AccountCard>();
        private CancellationTokenSource refreshCts;
        private bool busy;
        private int refreshDone;
        private int refreshTotal;

        public MainForm()
        {
            Text = "Antigravity 账号管家";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(880, 740);
            MinimumSize = new Size(760, 560);
            BackColor = Theme.Page;
            Font = Theme.Font(9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }
            BuildUi();
            LoadProfiles();
            autoTimer = new System.Windows.Forms.Timer();
            autoTimer.Interval = 5 * 60 * 1000;
            autoTimer.Tick += async delegate { if (!busy) await RefreshAllAsync(false); };
            autoTimer.Start();
        }

        private void BuildUi()
        {
            Panel topHost = new Panel();
            topHost.Dock = DockStyle.Top;
            topHost.Height = 154;
            topHost.BackColor = Theme.Page;
            Controls.Add(topHost);

            header = new GradientHeader();
            header.SetBounds(0, 0, topHost.ClientSize.Width, 92);
            topHost.Controls.Add(header);

            headerTitle = new Label();
            headerTitle.Text = "Antigravity 账号管家";
            headerTitle.ForeColor = Color.White;
            headerTitle.Font = Theme.Font(16.5f, FontStyle.Bold);
            headerTitle.AutoSize = false;
            headerTitle.BackColor = Color.Transparent;
            headerTitle.SetBounds(24, 18, 500, 32);
            header.Controls.Add(headerTitle);

            headerSubtitle = new Label();
            headerSubtitle.Text = "切换账号 · 查看剩余额度";
            headerSubtitle.ForeColor = Color.FromArgb(222, 216, 247);
            headerSubtitle.Font = Theme.Font(9f);
            headerSubtitle.AutoSize = false;
            headerSubtitle.BackColor = Color.Transparent;
            headerSubtitle.SetBounds(26, 51, 600, 20);
            header.Controls.Add(headerSubtitle);

            Panel toolbar = new Panel();
            toolbar.SetBounds(0, 92, topHost.ClientSize.Width, 62);
            toolbar.BackColor = Theme.Page;
            topHost.Controls.Add(toolbar);
            topHost.Resize += delegate
            {
                header.Width = topHost.ClientSize.Width;
                toolbar.Width = topHost.ClientSize.Width;
            };

            saveButton = new RoundButton("保存当前账号", Theme.Accent, Theme.AccentDark,
                Color.FromArgb(72, 44, 190), Color.White);
            saveButton.SetBounds(20, 13, 148, 38);
            saveButton.Click += async delegate { await SaveCurrentAsync(); };
            toolbar.Controls.Add(saveButton);

            refreshButton = new RoundButton("刷新全部额度", Theme.Card, Color.FromArgb(248, 248, 252),
                Color.FromArgb(238, 239, 246), Theme.AccentDark);
            refreshButton.SetBounds(180, 13, 136, 38);
            refreshButton.Click += async delegate { await RefreshAllAsync(true); };
            toolbar.Controls.Add(refreshButton);

            openButton = new RoundButton("打开反重力", Theme.Card, Color.FromArgb(248, 248, 252),
                Color.FromArgb(238, 239, 246), Theme.Text);
            openButton.SetBounds(328, 13, 120, 38);
            openButton.Click += delegate
            {
                if (!File.Exists(AppPaths.AppExe))
                {
                    MessageBox.Show(this, "没有找到 Antigravity.exe：\n" + AppPaths.AppExe,
                        "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ProfileStore.StartAntigravity();
            };
            toolbar.Controls.Add(openButton);

            Label autoLabel = new Label();
            autoLabel.Text = "额度每 5 分钟自动刷新";
            autoLabel.ForeColor = Theme.Muted;
            autoLabel.Font = Theme.Font(8.4f);
            autoLabel.AutoSize = true;
            autoLabel.BackColor = Color.Transparent;
            autoLabel.Location = new Point(470, 24);
            toolbar.Controls.Add(autoLabel);

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 30;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(22, 0, 0, 0);
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.Font = Theme.Font(8.4f);
            statusLabel.BackColor = Color.FromArgb(249, 250, 253);
            Controls.Add(statusLabel);

            cardsPanel = new FlowLayoutPanel();
            cardsPanel.Dock = DockStyle.Fill;
            cardsPanel.FlowDirection = FlowDirection.TopDown;
            cardsPanel.WrapContents = false;
            cardsPanel.AutoScroll = true;
            cardsPanel.Padding = new Padding(20, 8, 20, 20);
            cardsPanel.BackColor = Theme.Page;
            cardsPanel.Resize += delegate { LayoutCards(); };
            Controls.Add(cardsPanel);
            cardsPanel.BringToFront();

            emptyPanel = new Panel();
            emptyPanel.BackColor = Color.Transparent;
            emptyPanel.Visible = false;
            cardsPanel.Controls.Add(emptyPanel);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Environment.GetEnvironmentVariable("AGY_UI_NO_REFRESH") == "1") return;
            await RefreshAllAsync(false);
        }

        private void LoadProfiles()
        {
            profiles = store.LoadAll();
            string currentEmail = TokenStore.LiveEmail();
            cardsPanel.Controls.Remove(emptyPanel);
            foreach (Control control in cardsPanel.Controls)
            {
                control.Dispose();
            }
            cardsPanel.Controls.Clear();
            cardMap.Clear();

            foreach (Profile profile in profiles)
            {
                bool active = !string.IsNullOrEmpty(currentEmail) &&
                    JsonUtil.CanonicalEmail(currentEmail) == JsonUtil.CanonicalEmail(profile.Email);
                AccountCard card = new AccountCard(profile, active,
                    delegate(Profile p) { SwitchAccount(p); },
                    delegate(Profile p) { RefreshOneFromCard(p); },
                    delegate(Profile p) { RenameProfile(p); },
                    delegate(Profile p) { DeleteProfile(p); });
                cardMap[profile.Directory] = card;
                cardsPanel.Controls.Add(card);
            }
            if (profiles.Count == 0)
            {
                cardsPanel.Controls.Add(emptyPanel);
                emptyPanel.Visible = true;
                ShowEmptyState();
            }
            else if (emptyPanel != null)
            {
                emptyPanel.Visible = false;
            }
            LayoutCards();
            UpdateHeader(currentEmail);
            statusLabel.Text = profiles.Count == 0
                ? "还没有保存账号"
                : string.Format("共 {0} 个账号 · 当前 {1}", profiles.Count,
                    string.IsNullOrEmpty(currentEmail) ? "未登录" : currentEmail);
        }

        private void UpdateHeader(string currentEmail)
        {
            headerSubtitle.Text = string.IsNullOrEmpty(currentEmail)
                ? "当前未登录 · 先在反重力里登录，再点“保存当前账号”"
                : "当前账号：" + currentEmail;
        }

        private void ShowEmptyState()
        {
            emptyPanel.Visible = true;
            emptyPanel.Controls.Clear();
            Label title = new Label();
            title.Text = "还没有保存任何账号";
            title.Font = Theme.Font(13f, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.AutoSize = false;
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.SetBounds(0, 70, 640, 32);
            emptyPanel.Controls.Add(title);
            Label steps = new Label();
            steps.Text = "1. 在反重力里登录账号 A，回到这里点“保存当前账号”\n" +
                         "2. 在反重力里退出并登录账号 B，再保存一次\n" +
                         "3. 以后在卡片上点“切换”，几秒完成换号";
            steps.Font = Theme.Font(9.5f);
            steps.ForeColor = Theme.Muted;
            steps.AutoSize = false;
            steps.TextAlign = ContentAlignment.MiddleCenter;
            steps.SetBounds(0, 112, 640, 90);
            emptyPanel.Controls.Add(steps);
            RoundButton add = new RoundButton("保存当前账号", Theme.Accent, Theme.AccentDark,
                Color.FromArgb(72, 44, 190), Color.White);
            add.SetBounds(250, 220, 140, 38);
            add.Click += async delegate { await SaveCurrentAsync(); };
            emptyPanel.Controls.Add(add);
        }

        private void LayoutCards()
        {
            if (cardsPanel == null) return;
            int width = cardsPanel.ClientSize.Width - cardsPanel.Padding.Horizontal - 4;
            if (width < 400) width = 400;
            foreach (Control control in cardsPanel.Controls)
            {
                AccountCard card = control as AccountCard;
                if (card != null)
                {
                    card.Width = width;
                    card.Margin = new Padding(0, 0, 0, 12);
                }
            }
            if (emptyPanel != null)
            {
                emptyPanel.Width = width;
                emptyPanel.Height = 320;
                foreach (Control child in emptyPanel.Controls)
                {
                    child.Left = (width - child.Width) / 2;
                }
            }
        }

        private async Task SaveCurrentAsync()
        {
            if (busy) return;
            try
            {
                TokenData live = TokenStore.LoadLive();
                if (live == null)
                {
                    MessageBox.Show(this, "没有找到登录令牌。\n请先在反重力里登录一个账号，再保存。",
                        "未登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string email = live.Email;
                if (!JsonUtil.IsEmail(email))
                {
                    using (PromptForm prompt = new PromptForm("输入账号邮箱",
                        "无法从令牌中读取邮箱，请手动输入当前 Google 账号：", ""))
                    {
                        if (prompt.ShowDialog(this) != DialogResult.OK || !JsonUtil.IsEmail(prompt.ValueText))
                        {
                            return;
                        }
                        email = prompt.ValueText;
                    }
                }
                Profile saved = store.SaveCurrent(email);
                Log.Info("UI saved " + saved.Email);
                LoadProfiles();
                MessageBox.Show(this, "已保存账号：\n" + saved.Email, "保存成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (cardMap.ContainsKey(saved.Directory))
                {
                    await RefreshOneAsync(saved, cardMap[saved.Directory], CancellationToken.None, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Save current failed: " + ex);
                MessageBox.Show(this, ex.Message, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void RefreshOneFromCard(Profile profile)
        {
            if (busy) return;
            AccountCard card;
            if (!cardMap.TryGetValue(profile.Directory, out card)) return;
            CancellationTokenSource cts = new CancellationTokenSource();
            try
            {
                await RefreshOneAsync(profile, card, cts.Token, false);
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async Task RefreshAllAsync(bool showErrors)
        {
            if (busy) return;
            if (profiles.Count == 0)
            {
                if (showErrors) MessageBox.Show(this, "还没有保存账号，无法查询额度。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            busy = true;
            saveButton.Enabled = false;
            refreshButton.Enabled = false;
            refreshButton.Text = "查询中...";
            refreshDone = 0;
            refreshTotal = profiles.Count;
            refreshCts = new CancellationTokenSource();
            CancellationToken token = refreshCts.Token;
            List<Profile> snapshot = new List<Profile>(profiles);
            SemaphoreSlim gate = new SemaphoreSlim(2);
            List<Task> tasks = new List<Task>();
            foreach (Profile profile in snapshot)
            {
                Profile local = profile;
                tasks.Add(Task.Run(async delegate
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        AccountCard card;
                        if (cardMap.TryGetValue(local.Directory, out card))
                        {
                            await RefreshOneAsync(local, card, token, true).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, token));
            }
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error("Refresh all failed: " + ex.Message);
            }
            finally
            {
                gate.Dispose();
                refreshCts.Dispose();
                refreshCts = null;
                busy = false;
                saveButton.Enabled = true;
                refreshButton.Enabled = true;
                refreshButton.Text = "刷新全部额度";
                statusLabel.Text = string.Format("上次刷新 {0} · 共 {1} 个账号",
                    DateTime.Now.ToString("HH:mm"), profiles.Count);
            }
        }

        private async Task RefreshOneAsync(Profile profile, AccountCard card, CancellationToken token,
            bool updateProgress)
        {
            InvokeOnUi(delegate { card.SetBusy(true); });
            try
            {
                QuotaSnapshot snapshot = await quotaService.FetchAsync(profile, token).ConfigureAwait(false);
                ProfileStore.SaveQuota(profile, snapshot);
                profile.Quota = snapshot;
                if (!string.IsNullOrEmpty(snapshot.Plan)) profile.Plan = snapshot.Plan;
                InvokeOnUi(delegate
                {
                    card.UpdateQuota(snapshot);
                    card.SetBusy(false);
                    if (updateProgress)
                    {
                        refreshDone++;
                        statusLabel.Text = string.Format("正在查询额度 {0}/{1} ...", refreshDone, refreshTotal);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                InvokeOnUi(delegate { card.SetBusy(false); });
            }
            catch (Exception ex)
            {
                Log.Error("Quota refresh failed for " + profile.Email + ": " + ex.Message);
                QuotaSnapshot error = new QuotaSnapshot();
                error.Email = profile.Email;
                error.Error = ex.Message;
                InvokeOnUi(delegate
                {
                    card.UpdateQuota(error);
                    card.SetBusy(false);
                });
            }
        }

        private async void SwitchAccount(Profile profile)
        {
            if (busy) return;
            DialogResult confirm = MessageBox.Show(this,
                "切换到 " + profile.Email + " 会关闭并重新启动反重力。\n" +
                "未保存的对话或任务可能丢失，确定继续吗？",
                "切换账号", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            busy = true;
            try
            {
                statusLabel.Text = "正在切换账号...";
                await Task.Run(delegate { store.Switch(profile); });
                busy = false;
                LoadProfiles();
                statusLabel.Text = "已切换，正在查询额度...";
                await RefreshAllAsync(false);
                MessageBox.Show(this,
                    "已切换到：\n" + profile.Email +
                    (File.Exists(AppPaths.AppExe) ? "" : "\n\n未找到 Antigravity.exe，请手动启动。"),
                    "切换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("Switch failed: " + ex);
                MessageBox.Show(this, ex.Message, "切换失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
                refreshButton.Enabled = true;
                saveButton.Enabled = true;
                refreshButton.Text = "刷新全部额度";
            }
        }

        private void RenameProfile(Profile profile)
        {
            if (busy) return;
            using (PromptForm prompt = new PromptForm("重命名账号", "给这个账号起个备注名：", profile.DisplayName))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK || prompt.ValueText.Length == 0) return;
                try
                {
                    store.Rename(profile, prompt.ValueText);
                    LoadProfiles();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "重命名失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteProfile(Profile profile)
        {
            if (busy) return;
            DialogResult confirm = MessageBox.Show(this,
                "删除账号配置 " + profile.Email + "？\n" +
                "只会删除本机保存的令牌快照，不会注销 Google 账号。",
                "删除账号", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            try
            {
                store.Delete(profile);
                LoadProfiles();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "删除失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InvokeOnUi(Action action)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired) Invoke(action);
                else action();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (refreshCts != null) refreshCts.Cancel();
            if (autoTimer != null) autoTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}
