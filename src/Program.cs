using System;
using System.Net;
using System.Windows.Forms;

namespace AgyManager
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            }
            catch { }
            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--uismoke")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    using (MainForm form = new MainForm())
                    {
                        form.CreateControl();
                        form.PerformLayout();
                    }
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uismoke.log"),
                        "UISMOKE OK");
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uismoke.log"),
                        "UISMOKE FAILED\n" + ex);
                    Environment.ExitCode = 1;
                }
                return;
            }
            if (args != null && args.Length > 1 && args[0] == "--uishot")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable("AGY_UI_NO_REFRESH", "1");
                try
                {
                    using (MainForm form = new MainForm())
                    {
                        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(-20000, -20000);
                        int width = 880;
                        int height = 740;
                        if (args.Length > 3)
                        {
                            int.TryParse(args[2], out width);
                            int.TryParse(args[3], out height);
                            if (width < 760) width = 760;
                            if (height < 560) height = 560;
                        }
                        form.Size = new System.Drawing.Size(width, height);
                        form.Show();
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(400);
                        Application.DoEvents();
                        using (System.Drawing.Bitmap bitmap =
                            new System.Drawing.Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap,
                                new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                            bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Hide();
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uishot.log"),
                        "UISHOT FAILED\n" + ex);
                    Environment.ExitCode = 1;
                }
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--switchtest" &&
                Environment.GetEnvironmentVariable("AGY_TEST_MODE") == "1")
            {
                Environment.ExitCode = SwitchTest.Run();
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--keyringcheck")
            {
                Environment.ExitCode = KeyringCheck.Run();
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--savecheck")
            {
                Environment.ExitCode = SaveCheck.Run();
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--keyringwritetest" &&
                Environment.GetEnvironmentVariable("AGY_TEST_MODE") == "1")
            {
                Environment.ExitCode = KeyringWriteTest.Run();
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--quotacheck" &&
                Environment.GetEnvironmentVariable("AGY_TEST_MODE") == "1")
            {
                Environment.ExitCode = QuotaCheck.Run();
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--refreshcheck" &&
                Environment.GetEnvironmentVariable("AGY_TEST_MODE") == "1")
            {
                Environment.ExitCode = RefreshCheck.Run();
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--oauthcheck")
            {
                Environment.ExitCode = OAuthCheck.Run();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
