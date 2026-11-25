using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>إعدادات هذا الجهاز فقط (لا تنتقل مع ملف البيانات): مجلد المزامنة واسم الجهاز.</summary>
    public class SyncConfig
    {
        public string Folder { get; set; } = "";
        public string Machine { get; set; } = Environment.MachineName;

        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync_config.json"); } }
        public static string StatePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync_state.json"); } }
        public bool Enabled { get { return !string.IsNullOrWhiteSpace(Folder); } }

        public static SyncConfig Load()
        {
            try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(FilePath)) ?? new SyncConfig(); }
            catch { }
            return new SyncConfig();
        }

        public void Save() { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }

        public Handover Handover() { return new Handover(Db.DbPath, StatePath, Folder, Machine); }
    }

    /// <summary>الوردية: تقرير ما جرى منذ بدايتها (بأرقام الحركات لا بالوقت، فلا تختلط ورديتان في اليوم نفسه) وإغلاقها.</summary>
    public static class Shift
    {
        static readonly string[] Tables = { "Sales", "Payments", "Expenses", "SaleReturns", "Purchases", "PurchaseReturns" };

        static Handover.LocalState State(SyncConfig cfg) { return cfg.Handover().LoadState(); }

        /// <summary>يبدأ وردية جديدة: يسجّل آخر رقم في كل جدول، فكل ما بعده يُحسب على هذه الوردية.</summary>
        public static void Begin(SyncConfig cfg)
        {
            var h = cfg.Handover();
            var s = h.LoadState();
            s.ShiftStart = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            s.Marks = new Dictionary<string, long>();
            foreach (string t in Tables) s.Marks[t] = Convert.ToInt64(Db.Scalar("SELECT IFNULL(MAX(Id),0) FROM " + t));
            h.SaveState(s);
        }

        /// <summary>يبدأ وردية إن لم تكن هناك وردية مفتوحة على هذا الجهاز.</summary>
        public static void EnsureStarted(SyncConfig cfg)
        {
            if (string.IsNullOrEmpty(State(cfg).ShiftStart)) Begin(cfg);
        }

        public class Report
        {
            public string Start;
            public long Invoices;
            public double NetSales, SalesPaid, CustomerPayments, SupplierPayments, PurchasesPaid, Expenses, SaleRefunds, PurchaseRefunds;
            public double ExpectedCash { get { return SalesPaid + CustomerPayments - SupplierPayments - PurchasesPaid - Expenses - SaleRefunds + PurchaseRefunds; } }
        }

        public static Report Build(SyncConfig cfg)
        {
            var s = State(cfg);
            Func<string, long> m = t => s.Marks != null && s.Marks.ContainsKey(t) ? s.Marks[t] : 0;
            Func<string, string, double> sum = (sql, t) => Db.D(Db.Scalar(sql, m(t)));
            return new Report
            {
                Start = s.ShiftStart ?? "",
                Invoices = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM Sales WHERE Id>@p0", m("Sales"))),
                NetSales = sum("SELECT IFNULL(SUM(Net),0) FROM Sales WHERE Id>@p0", "Sales"),
                SalesPaid = sum("SELECT IFNULL(SUM(Paid),0) FROM Sales WHERE Id>@p0", "Sales"),
                CustomerPayments = sum("SELECT IFNULL(SUM(Amount),0) FROM Payments WHERE PartyType='C' AND Id>@p0", "Payments"),
                SupplierPayments = sum("SELECT IFNULL(SUM(CASE WHEN IFNULL(Currency,'SYP')='USD' THEN Amount*IFNULL(Rate,1) ELSE Amount END),0) FROM Payments WHERE PartyType='S' AND Id>@p0", "Payments"),
                PurchasesPaid = sum("SELECT IFNULL(SUM(PaidSYP),0) FROM Purchases WHERE Id>@p0", "Purchases"),
                Expenses = sum("SELECT IFNULL(SUM(Amount),0) FROM Expenses WHERE Id>@p0", "Expenses"),
                SaleRefunds = sum("SELECT IFNULL(SUM(RefundCash),0) FROM SaleReturns WHERE Id>@p0", "SaleReturns"),
                PurchaseRefunds = sum("SELECT IFNULL(SUM(CASE WHEN IFNULL(Currency,'SYP')='USD' THEN RefundCash*IFNULL(Rate,1) ELSE RefundCash END),0) FROM PurchaseReturns WHERE Id>@p0", "PurchaseReturns"),
            };
        }

        /// <summary>يسجّل الوردية في جدول Shifts (ينتقل مع ملف البيانات، فيراه صاحب المحل من أي جهاز).</summary>
        public static void Record(SyncConfig cfg, Report r, double? counted)
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Db.Exec("INSERT INTO Shifts(Machine,StartAt,EndAt,Invoices,NetSales,CashExpected,CashCounted) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                cfg.Machine, r.Start, now, r.Invoices, r.NetSales, r.ExpectedCash, counted.HasValue ? (object)counted.Value : null);
        }

        // ---------------- الواجهة ----------------

        /// <summary>عند تشغيل البرنامج: استلام ملف البيانات إن كانت المزامنة مفعّلة. يعيد false لإغلاق البرنامج.</summary>
        public static bool Startup()
        {
            var cfg = SyncConfig.Load();
            if (!cfg.Enabled) return true;
            // لا توجد نافذة رئيسية بعد: مالك خفي "دائماً في الأعلى" كي تظهر رسائل الاستلام أمام كل النوافذ ولا تختفي خلفها
            using (var top = new Form { TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), Size = new Size(1, 1) })
            {
                top.Show();
                return StartupDialogs(cfg, top);
            }
        }

        static bool StartupDialogs(SyncConfig cfg, Form top)
        {
            var h = cfg.Handover();
            bool force = false;
            while (true)
            {
                var r = h.CheckOut(force);
                switch (r.Status)
                {
                    case ShopManager.Handover.Status.Ok:
                        if (r.ConflictFile != null)
                            UI.Warn(top, "تم الاستلام بالقوة سابقاً من هذا الجهاز أو غيره، وكانت عندك تعديلات لم تُسلَّم.\nحُفظت في:\n" + r.ConflictFile + "\nراجعها مع صاحب المحل.");
                        if (r.Downloaded) { var s = h.LoadState(); s.ShiftStart = null; h.SaveState(s); }   // نسخة جديدة ← وردية جديدة
                        return true;
                    case ShopManager.Handover.Status.NotInitialized:
                        if (!UI.Confirm(top, "المجلد المشترك فارغ:\n" + cfg.Folder + "\n\nهل ترفع بيانات هذا الجهاز لتصبح النسخة المشتركة؟\n(افعل هذا على جهاز واحد فقط، أول مرة.)")) return false;
                        var init = h.Initialize();
                        if (init.Status == ShopManager.Handover.Status.Ok) return true;
                        UI.Warn(top, "تعذّر الرفع: " + init.Message); return false;
                    case ShopManager.Handover.Status.HeldByOther:
                        if (!UI.Confirm(top, "ملف البيانات مستلَم الآن على جهاز \"" + r.Ticket.Holder + "\" منذ " + r.Ticket.HolderSince + " ولم يُسلَّم بعد.\n\n" +
                                              "الأفضل: اطلب منه (إغلاق الوردية وتسليم)، ثم انتظر دقيقة لتصل المزامنة وأعد فتح البرنامج.\n\n" +
                                              "هل تريد الاستلام بالقوة؟ ما لم يُسلّمه الجهاز الآخر لن يدخل في بياناتك، وسيُحفظ عنده كنسخة تعارض.")) return false;
                        force = true; continue;
                    case ShopManager.Handover.Status.SyncIncomplete:
                        if (UI.Confirm(top, "المزامنة لم تكتمل بعد: ملف البيانات في المجلد المشترك لا يطابق آخر تسليم.\nتأكد من اتصال الإنترنت وأن برنامج المزامنة (Google Drive / OneDrive) يعمل.\n\nإعادة المحاولة الآن؟")) continue;
                        return false;
                    case ShopManager.Handover.Status.FolderMissing:
                        if (UI.Confirm(top, "مجلد المزامنة غير موجود:\n" + cfg.Folder + "\n\nإعادة المحاولة؟ (لإيقاف المزامنة احذف الملف sync_config.json بجانب البرنامج)")) continue;
                        return false;
                    default:
                        if (UI.Confirm(top, "تعذّر الاستلام: " + r.Message + "\n\nإعادة المحاولة؟")) continue;
                        return false;
                }
            }
        }

        /// <summary>إغلاق الوردية (وتسليم ملف البيانات إن كانت المزامنة مفعّلة). يعيد true إذا أُغلقت.</summary>
        public static bool Close(Form owner)
        {
            if (Application.OpenForms.Count > 1)
            {
                UI.Warn(owner, "أغلق كل النوافذ المفتوحة (الفواتير، الأصناف...) أولاً، كي لا يضيع تعديل غير محفوظ.");
                return false;
            }
            var cfg = SyncConfig.Load();
            Report r = Build(cfg);
            double? counted;
            if (!AskCounted(owner, r, cfg, out counted)) return false;
            Record(cfg, r, counted);
            if (!cfg.Enabled)
            {
                Begin(cfg);
                UI.Info(owner, "أُغلقت الوردية وبدأت وردية جديدة.");
                return true;
            }
            var res = cfg.Handover().HandOver();
            if (res.Status == ShopManager.Handover.Status.Ok)
            {
                var s = cfg.Handover().LoadState(); s.ShiftStart = null; cfg.Handover().SaveState(s);
                UI.Info(owner, "تم التسليم (النسخة رقم " + res.Ticket.Version + ").\nانتظر حتى يُكمل برنامج المزامنة رفع الملف قبل إطفاء الجهاز.\nسيُغلق البرنامج الآن.");
                return true;
            }
            if (res.Status == ShopManager.Handover.Status.Conflict)
            {
                UI.Warn(owner, "لم يُرفع ملفك لأن جهازاً آخر استلم البيانات بالقوة أثناء ورديتك.\nحُفظت ورديتك كاملة في:\n" + res.ConflictFile + "\nأبلغ صاحب المحل لمراجعتها. سيُغلق البرنامج الآن.");
                return true;
            }
            UI.Warn(owner, "تعذّر التسليم: " + res.Message + "\nبياناتك محفوظة على هذا الجهاز. تأكد من المجلد والإنترنت ثم أعد المحاولة.");
            return false;
        }

        static bool AskCounted(Form owner, Report r, SyncConfig cfg, out double? counted)
        {
            counted = null;
            using (var f = new Form())
            {
                UI.Rtl(f);
                f.Text = cfg.Enabled ? "إغلاق الوردية وتسليم" : "إغلاق الوردية";
                f.Width = 560; f.Height = 560;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false; f.MaximizeBox = false;
                Func<double, string> n = v => v.ToString("N0");
                var lbl = new Label
                {
                    Dock = DockStyle.Top, Height = 330, Padding = new Padding(16),
                    Text = "الجهاز: " + cfg.Machine + (Session.User != null ? "   |   المستخدم: " + Session.User.Name : "") + "\nبداية الوردية: " + r.Start + "\n\n" +
                           "فواتير البيع: " + r.Invoices + "   |   صافي المبيعات: " + n(r.NetSales) + " ل.س\n\n" +
                           "+ المقبوض من المبيعات: " + n(r.SalesPaid) + "\n" +
                           "+ دفعات الزبائن: " + n(r.CustomerPayments) + "\n" +
                           "− دفعات للموردين: " + n(r.SupplierPayments) + "\n" +
                           "− المدفوع في فواتير المشتريات: " + n(r.PurchasesPaid) + "\n" +
                           "− المصاريف: " + n(r.Expenses) + "\n" +
                           "− المردود نقداً للزبائن: " + n(r.SaleRefunds) + "\n" +
                           "+ المسترد نقداً من الموردين: " + n(r.PurchaseRefunds) + "\n\n" +
                           "= التغيّر المتوقع في الصندوق خلال الوردية: " + n(r.ExpectedCash) + " ل.س"
                };
                var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(12, 8, 12, 0) };
                var txt = new TextBox { Width = 160, Font = new Font("Segoe UI", 13) };
                row.Controls.Add(new Label { Text = "الصندوق الآن − مبلغ البداية (اختياري):", AutoSize = true, Margin = new Padding(4, 10, 4, 0) });
                row.Controls.Add(txt);
                var diff = new Label { Dock = DockStyle.Top, Height = 36, Padding = new Padding(16, 0, 16, 0), Font = new Font("Segoe UI", 12, FontStyle.Bold) };
                txt.TextChanged += (a, b) =>
                {
                    if (txt.Text.Trim() == "") { diff.Text = ""; return; }
                    double d = Db.Num(txt.Text) - r.ExpectedCash;
                    diff.Text = Math.Abs(d) < 0.5 ? "✔ الصندوق مطابق" : (d > 0 ? "زيادة: " : "نقص: ") + n(Math.Abs(d)) + " ل.س";
                    diff.ForeColor = Math.Abs(d) < 0.5 ? Color.DarkGreen : Color.Firebrick;
                };
                var ok = new Button { Text = cfg.Enabled ? "إغلاق الوردية وتسليم" : "إغلاق الوردية", Dock = DockStyle.Bottom, Height = 48, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "إلغاء", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };
                f.Controls.Add(diff); f.Controls.Add(row); f.Controls.Add(lbl);
                f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                if (txt.Text.Trim() != "") counted = Db.Num(txt.Text);
                return true;
            }
        }
    }
}
