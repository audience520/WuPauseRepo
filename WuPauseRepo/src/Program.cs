using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace WuPause
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 任何未捕获异常都不许闪退，必须弹出来给人看
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show("发生错误：\n" + e.Exception.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                MessageBox.Show("发生严重错误：\n" + e.ExceptionObject, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                // 启动阶段失败也不能静默退出
                MessageBox.Show("程序启动失败：\n" + ex, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private Label lblStatus;
        private Label lblDetail;
        private NumericUpDown numDays;
        private Button btnPause;
        private Button btnResume;
        private Button btnRefresh;
        private TextBox txtLog;

        private const string BACKUP_DIR = "WuPause_Backup";

        public MainForm()
        {
            BuildUi();
            RefreshState();
        }

        private void BuildUi()
        {
            Text = "Windows 更新暂停工具";
            ClientSize = new Size(620, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;

            int pad = 16;
            int y = pad;

            Label title = new Label
            {
                Text = "Windows 更新暂停",
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(pad, y)
            };
            Controls.Add(title);
            y += 36;

            lblStatus = new Label
            {
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(ClientSize.Width - pad * 2, 24),
                Location = new Point(pad, y)
            };
            Controls.Add(lblStatus);
            y += 28;

            lblDetail = new Label
            {
                Font = new Font("Consolas", 9F),
                AutoSize = false,
                Size = new Size(ClientSize.Width - pad * 2, 100),
                Location = new Point(pad, y),
                ForeColor = Color.FromArgb(64, 64, 64)
            };
            Controls.Add(lblDetail);
            y += 110;

            Label lblDays = new Label
            {
                Text = "暂停天数：",
                AutoSize = true,
                Location = new Point(pad, y + 6)
            };
            Controls.Add(lblDays);

            numDays = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 36500,
                Value = 365,
                Width = 130,
                Location = new Point(pad + 80, y),
                Font = new Font("Consolas", 10F)
            };
            Controls.Add(numDays);

            Label lblHint = new Label
            {
                Text = "（1 - 36500 天；36500 ≈ 100 年）",
                AutoSize = true,
                ForeColor = Color.Gray,
                Location = new Point(pad + 220, y + 6)
            };
            Controls.Add(lblHint);
            y += 42;

            btnPause = MakeButton("设为暂停", pad, y, Color.FromArgb(0, 120, 212));
            btnPause.Click += BtnPause_Click;

            btnResume = MakeButton("取消暂停", pad + 160, y, Color.FromArgb(200, 60, 60));
            btnResume.Click += BtnResume_Click;

            btnRefresh = MakeButton("刷新状态", pad + 320, y, Color.FromArgb(100, 100, 100));
            btnRefresh.BackColor = Color.White;
            btnRefresh.ForeColor = Color.Black;
            btnRefresh.Click += delegate { RefreshState(); };
            y += 52;

            Label lblLog = new Label
            {
                Text = "操作记录",
                AutoSize = true,
                Location = new Point(pad, y)
            };
            Controls.Add(lblLog);
            y += 22;

            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                Location = new Point(pad, y),
                Size = new Size(ClientSize.Width - pad * 2, ClientSize.Height - y - pad),
                BackColor = Color.FromArgb(250, 250, 250)
            };
            Controls.Add(txtLog);
        }

        private Button MakeButton(string text, int x, int y, Color back)
        {
            Button b = new Button
            {
                Text = text,
                Size = new Size(150, 38),
                Location = new Point(x, y),
                BackColor = back,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand
            };
            Controls.Add(b);
            return b;
        }

        private void Log(string s)
        {
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
        }

        private void RefreshState()
        {
            try
            {
                int? cap = WuRegistry.ReadMaxPauseDays();
                int? featSt = WuRegistry.ReadStatus("PausedFeatureStatus");
                int? qualSt = WuRegistry.ReadStatus("PausedQualityStatus");
                DateTime? exp = WuRegistry.ReadExpiryUtc();

                bool paused = (featSt.HasValue && featSt.Value == 1)
                           || (qualSt.HasValue && qualSt.Value == 1);

                if (paused && exp.HasValue)
                {
                    DateTime expLocal = exp.Value.ToLocalTime();
                    if (expLocal > DateTime.Now)
                    {
                        int days = (int)(expLocal - DateTime.Now).TotalDays;
                        lblStatus.ForeColor = Color.FromArgb(20, 130, 60);
                        lblStatus.Text = "状态：已暂停（剩余约 " + days + " 天，至 "
                                      + expLocal.ToString("yyyy-MM-dd") + "）";
                    }
                    else
                    {
                        lblStatus.ForeColor = Color.FromArgb(200, 120, 0);
                        lblStatus.Text = "状态：状态位为暂停，但已过期";
                    }
                }
                else if (paused)
                {
                    lblStatus.ForeColor = Color.FromArgb(200, 120, 0);
                    lblStatus.Text = "状态：状态位为暂停，但无有效到期时间";
                }
                else
                {
                    lblStatus.ForeColor = Color.FromArgb(180, 40, 40);
                    lblStatus.Text = "状态：未暂停（更新会正常下载安装）";
                }

                lblDetail.Text =
                    "FlightSettingsMaxPauseDays = " + (cap.HasValue ? cap.Value.ToString() : "未设置") + Environment.NewLine +
                    "PausedFeatureStatus       = " + Fmt(featSt) + Environment.NewLine +
                    "PausedQualityStatus       = " + Fmt(qualSt) + Environment.NewLine +
                    "暂停到期时间               = "
                        + (exp.HasValue ? exp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未设置");
            }
            catch (Exception ex)
            {
                lblStatus.ForeColor = Color.FromArgb(180, 40, 40);
                lblStatus.Text = "读取状态失败";
                lblDetail.Text = ex.Message;
            }
        }

        private static string Fmt(int? v)
        {
            if (!v.HasValue) return "不存在";
            if (v.Value == 0) return "0（未暂停）";
            if (v.Value == 1) return "1（暂停中）";
            return v.Value.ToString();
        }

        // ---------------------------------------------------------------

        private void BtnPause_Click(object sender, EventArgs e)
        {
            int days = (int)numDays.Value;
            string backupPath = Path.Combine(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BACKUP_DIR),
                "before_pause_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".reg");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                WuRegistry.Backup(backupPath);
                Log("已备份注册表 -> " + backupPath);

                DateTime endUtc = WuRegistry.ApplyPause(days);
                Log("已写入暂停态：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " 起，暂停 " + days + " 天");
                Log("暂停到期（本地时间）：" + endUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

                Verify();

                Log("完成。请重启'设置'应用查看效果（设置 -> Windows 更新）。");
                MessageBox.Show(this,
                    "已设为暂停 " + days + " 天。\n\n到期：" + endUtc.ToLocalTime().ToString("yyyy-MM-dd")
                    + "\n\n若设置界面未立即显示，请完全关闭'设置'窗口后重新打开。\n\n备份：\n"
                    + backupPath,
                    "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

                RefreshState();
            }
            catch (UnauthorizedAccessException)
            {
                Log("失败：权限不足。请右键 -> 以管理员身份运行");
                MessageBox.Show(this, "权限不足，请以管理员身份运行本程序。", "失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Log("失败：" + ex.Message);
                MessageBox.Show(this, "写入失败：\n" + ex.Message, "失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Verify()
        {
            try
            {
                int? cap = WuRegistry.ReadMaxPauseDays();
                int? featSt = WuRegistry.ReadStatus("PausedFeatureStatus");
                int? qualSt = WuRegistry.ReadStatus("PausedQualityStatus");
                string ts = WuRegistry.ReadTimestamp("PauseUpdatesExpiryTime");

                Log("--- 回读验证 ---");
                Log("  FlightSettingsMaxPauseDays = " + (cap.HasValue ? cap.Value.ToString() : "未设置"));
                Log("  PausedFeatureStatus       = " + Fmt(featSt));
                Log("  PausedQualityStatus       = " + Fmt(qualSt));
                Log("  PauseUpdatesExpiryTime    = " + (ts ?? "(不存在)"));

                if (featSt != 1 && qualSt != 1)
                    Log("  [警告] 状态位未成功写入 1，暂停可能不生效");
                if (string.IsNullOrEmpty(ts))
                    Log("  [警告] 时间戳未写入，暂停一定不生效");
                else
                {
                    DateTime utc;
                    if (WuRegistry.TryParseWuTime(ts, out utc))
                        Log("  [OK] 暂停到期 = " + utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "（本地）");
                    else
                        Log("  [警告] 时间戳格式无法解析：" + ts);
                }
            }
            catch (Exception ex)
            {
                Log("  回读验证异常：" + ex.Message);
            }
        }

        private void BtnResume_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show(this,
                "确定要取消暂停吗？\n\n将清除暂停状态位和时间戳，恢复正常更新。",
                "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;

            try
            {
                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BACKUP_DIR);
                try
                {
                    Directory.CreateDirectory(backupDir);
                    WuRegistry.Backup(Path.Combine(backupDir,
                        "before_resume_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".reg"));
                }
                catch (Exception bex)
                {
                    Log("备份失败（继续）：" + bex.Message);
                }

                WuRegistry.ResumeNow();
                Log("已清除暂停态，恢复正常更新");
                RefreshState();
                MessageBox.Show(this, "已取消暂停。可在 设置 -> Windows 更新 点'检查更新'。", "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("失败：" + ex.Message);
                MessageBox.Show(this, "取消失败：\n" + ex.Message, "失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}