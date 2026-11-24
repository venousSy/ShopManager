using System;
using System.IO;
using System.Windows.Forms;

namespace ShopManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);
            Db.Init();
            Application.Run(new MainForm());
        }

        /// <summary>يسجّل الخطأ في errors.log بجانب البرنامج ويعرض موضعه بدقة، دون إغلاق البرنامج.</summary>
        static void ReportError(Exception ex)
        {
            if (ex == null) return;
            string full = ex.ToString();
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "errors.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + full + "\r\n\r\n");
            }
            catch { }
            string shown = full.Length > 1800 ? full.Substring(0, 1800) + "..." : full;
            MessageBox.Show("حدث خطأ غير متوقع (سُجّل في الملف errors.log بجانب البرنامج). أرسل هذا النص للمساعدة:\n\n" + shown,
                "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
    }
}
