using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    public class MainForm : Form
    {
        readonly Label lblTitle = new Label();
        readonly Label lblToday = new Label();
        bool closingAfterHandover;

        public MainForm()
        {
            UI.Rtl(this);
            Text = "برنامج إدارة المحل";
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = 80;
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.Font = new Font("Segoe UI", 26, FontStyle.Bold);
            lblTitle.BackColor = Color.FromArgb(31, 78, 120);
            lblTitle.ForeColor = Color.White;

            lblToday.Dock = DockStyle.Top;
            lblToday.Height = 52;
            lblToday.TextAlign = ContentAlignment.MiddleCenter;
            lblToday.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            lblToday.BackColor = Color.FromArgb(221, 235, 247);

            panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(40), WrapContents = true };
            // كل زر مربوط بصلاحية (null = متاح للجميع). الزر غير المسموح يُخفى ومفتاحه لا يعمل.
            AddBtn(panel, "فاتورة بيع  (F1)", Color.FromArgb(31, 78, 120), Perm.Sell, Keys.F1, () => Open(new InvoiceForm(true)));
            AddBtn(panel, "فاتورة مشتريات  (F2)", Color.FromArgb(55, 86, 35), Perm.Purchases, Keys.F2, () => Open(new InvoiceForm(false)));
            AddBtn(panel, "الأصناف والمخزون  (F3)", Color.FromArgb(127, 96, 0), Perm.Products, Keys.F3, () => Open(new ProductsForm()));
            AddBtn(panel, "المصاريف  (F4)", Color.FromArgb(150, 60, 30), Perm.Expenses, Keys.F4, () => Open(new ExpensesForm()));
            AddBtn(panel, "الديون والمدفوعات  (F5)", Color.FromArgb(90, 50, 120), Perm.Payments, Keys.F5, () => Open(new DebtsForm()));
            AddBtn(panel, "الزبائن  (F8)", Color.FromArgb(0, 90, 160), Perm.Parties, Keys.F8, () => Open(new PartiesForm(true)));
            AddBtn(panel, "الموردون  (F9)", Color.FromArgb(120, 80, 20), Perm.Parties, Keys.F9, () => Open(new PartiesForm(false)));
            AddBtn(panel, "سجل فواتير المبيعات", Color.FromArgb(20, 80, 130), Perm.Sell, Keys.None, () => Open(new InvoicesLogForm(true)));
            AddBtn(panel, "سجل فواتير المشتريات", Color.FromArgb(70, 100, 40), Perm.Purchases, Keys.None, () => Open(new InvoicesLogForm(false)));
            AddBtn(panel, "مرتجعات المبيعات", Color.FromArgb(150, 90, 30), Perm.SaleReturns, Keys.None, () => Open(new ReturnsForm(true, 0)));
            AddBtn(panel, "مرتجعات المشتريات", Color.FromArgb(110, 90, 30), Perm.PurchaseReturns, Keys.None, () => Open(new ReturnsForm(false, 0)));
            AddBtn(panel, "النواقص", Color.FromArgb(160, 40, 40), null, Keys.None, () => Open(new ShortagesForm()));
            AddBtn(panel, "أفضل الأصناف للفترة", Color.FromArgb(40, 120, 70), Perm.Profits, Keys.None, () => Open(new TopItemsForm()));
            AddBtn(panel, "التقارير والأرباح  (F6)", Color.FromArgb(0, 110, 110), Perm.Profits, Keys.F6, () => Open(new ReportsForm()));
            AddBtn(panel, "الإعدادات  (F7)", Color.FromArgb(80, 80, 80), Perm.Admin, Keys.F7, () => Open(new SettingsForm()));
            bool sync = SyncConfig.Load().Enabled;
            AddBtn(panel, sync ? "إغلاق الوردية وتسليم" : "إغلاق الوردية", Color.FromArgb(150, 30, 60), null, Keys.None, () =>
            {
                if (Shift.Close(this) && sync) { closingAfterHandover = true; Close(); }
            });
            // زر تبديل المستخدم في الشريط العلوي (يبقى ظاهراً مهما كان عدد الأزرار)
            var btnSwitch = new Button
            {
                Text = "تبديل المستخدم", Dock = DockStyle.Left, Width = 200,
                Font = new Font("Segoe UI", 12, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 100, 150), ForeColor = Color.White
            };
            btnSwitch.Click += (a, e) => SwitchUser();
            lblTitle.Controls.Add(btnSwitch);
            ApplyPermissions();
            // عند إغلاق البرنامج والجهاز ما زال مستلماً: تذكير بالتسليم كي لا ينتظر الجهاز الآخر
            FormClosing += (a, e) =>
            {
                if (closingAfterHandover || !sync || !SyncConfig.Load().Handover().LoadState().Holding) return;
                var ans = MessageBox.Show(this, "لم تُسلّم الوردية بعد، فلن يستطيع الجهاز الآخر العمل حتى تسلّمها.\n\nنعم: إغلاق الوردية وتسليم الآن\nلا: إغلاق البرنامج دون تسليم (يتابع هذا الجهاز لاحقاً)",
                    "تسليم الوردية", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                if (ans == DialogResult.Cancel) { e.Cancel = true; return; }
                if (ans == DialogResult.Yes && !Shift.Close(this)) e.Cancel = true;
            };

            Controls.Add(panel);
            Controls.Add(lblToday);
            Controls.Add(lblTitle);

            KeyDown += (a, b) =>
            {
                foreach (var e in entries)
                    if (e.Key != Keys.None && e.Key == b.KeyCode)
                    {
                        b.Handled = true;
                        if (e.Perm == null || Session.Can(e.Perm)) Run(e.Act);
                        break;
                    }
            };
            RefreshHeader();
            Shown += (a, b) => CheckStaleDebts();
        }

        /// <summary>عند فتح البرنامج: ينبّه إلى الديون التي مضى عليها أكثر من N يوم بلا حركة ويجمّدها.</summary>
        void CheckStaleDebts()
        {
            try
            {
                DataTable t = Db.FreezeStaleDebts();
                if (t.Rows.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("تنبيه: " + t.Rows.Count + " دين مضى عليه أكثر من " + Db.FreezeDays + " يوماً بلا أي حركة. نُقل إلى قائمة الديون المجمّدة:");
                    sb.AppendLine();
                    int n = 0;
                    foreach (DataRow r in t.Rows)
                    {
                        if (n++ >= 15) { sb.AppendLine("... وغيرها"); break; }
                        sb.AppendLine("• " + r["Name"] + " : " + Db.D(r["Bal"]).ToString("N0") + " ل.س (آخر حركة " + r["LastDate"] + ")");
                    }
                    sb.AppendLine();
                    sb.AppendLine("تجدها في: الديون والمدفوعات ← ديون مجمّدة. وتعود جارية تلقائياً عند أي بيع أو دفعة.");
                    UI.Warn(this, sb.ToString());
                }
            }
            catch { }
            RefreshHeader();
        }

        sealed class Entry { public Button Btn; public string Perm; public Keys Key; public Action Act; }
        readonly System.Collections.Generic.List<Entry> entries = new System.Collections.Generic.List<Entry>();
        readonly FlowLayoutPanel panel;

        void AddBtn(FlowLayoutPanel p, string text, Color color, string perm, Keys key, Action act)
        {
            var b = new Button
            {
                Text = text, Width = 300, Height = 110, Margin = new Padding(16),
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            b.Click += (a, e) => Run(act);
            p.Controls.Add(b);
            entries.Add(new Entry { Btn = b, Perm = perm, Key = key, Act = act });
        }

        /// <summary>يُظهر فقط أزرار الصفحات المسموحة للمستخدم الحالي.</summary>
        void ApplyPermissions()
        {
            foreach (var e in entries) e.Btn.Visible = e.Perm == null || Session.Can(e.Perm);
        }

        /// <summary>يغلق كل الصفحات المفتوحة ويعرض شاشة الدخول لمستخدم آخر.</summary>
        void SwitchUser()
        {
            foreach (Form f in new System.Collections.Generic.List<Form>(Application.OpenForms.Cast<Form>()))
            {
                if (f == this) continue;
                f.Close();
                if (!f.IsDisposed && f.Visible) { UI.Warn(this, "أغلق الصفحات المفتوحة أولاً (يوجد عمل لم يُحفظ)."); return; }
            }
            Hide();
            bool ok = LoginForm.Run();
            if (!ok) { closingAfterHandover = false; Close(); return; }
            Show();
            ApplyPermissions();
            RefreshHeader();
        }

        /// <summary>يفتح الصفحة في نافذة مستقلة (يمكن تصغيرها وفتح صفحات أخرى معها).</summary>
        void Open(Form f)
        {
            UI.ShowPage(f, RefreshHeader);
        }

        void Run(Action act)
        {
            act();
            RefreshHeader();
        }

        void RefreshHeader()
        {
            lblTitle.Text = Db.Get("shop_name");
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            double sales = Db.D(Db.Scalar("SELECT IFNULL(SUM(Net),0) FROM Sales WHERE substr(Date,1,10)=@p0", today));
            long count = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM Sales WHERE substr(Date,1,10)=@p0", today));
            double cogs = Db.D(Db.Scalar("SELECT IFNULL(SUM(si.Qty*si.UnitCost),0) FROM SaleItems si JOIN Sales s ON s.Id=si.SaleId WHERE substr(s.Date,1,10)=@p0", today));
            long overLimit = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM (" + Sql.CustomerBaseSql() + ") WHERE Bal>" + Db.DebtLimit.ToString(CultureInfo.InvariantCulture)));
            lblToday.Text = (Session.User != null ? "المستخدم: " + Session.User.Name + "   |   " : "") +
                            (overLimit > 0 ? "⚠ ديون فوق الحد: " + overLimit + "   |   " : "") + "اليوم:  مبيعات " + sales.ToString("N0") + " ل.س   |   فواتير " + count +
                            (Session.Can(Perm.Profits) ? "   |   ربح تقريبي " + (sales - cogs).ToString("N0") + " ل.س" : "") +
                            "   |   سعر الصرف " + Db.Rate.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
