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

            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(40), WrapContents = true };
            AddBtn(panel, "فاتورة بيع  (F1)", Color.FromArgb(31, 78, 120), () => Open(new InvoiceForm(true)));
            AddBtn(panel, "فاتورة مشتريات  (F2)", Color.FromArgb(55, 86, 35), () => Open(new InvoiceForm(false)));
            AddBtn(panel, "الأصناف والمخزون  (F3)", Color.FromArgb(127, 96, 0), () => Open(new ProductsForm()));
            AddBtn(panel, "المصاريف  (F4)", Color.FromArgb(150, 60, 30), () => Open(new ExpensesForm()));
            AddBtn(panel, "الديون والمدفوعات  (F5)", Color.FromArgb(90, 50, 120), () => Open(new DebtsForm()));
            AddBtn(panel, "الزبائن  (F8)", Color.FromArgb(0, 90, 160), () => Open(new PartiesForm(true)));
            AddBtn(panel, "الموردون  (F9)", Color.FromArgb(120, 80, 20), () => Open(new PartiesForm(false)));
            AddBtn(panel, "سجل فواتير المبيعات", Color.FromArgb(20, 80, 130), () => Open(new InvoicesLogForm(true)));
            AddBtn(panel, "سجل فواتير المشتريات", Color.FromArgb(70, 100, 40), () => Open(new InvoicesLogForm(false)));
            AddBtn(panel, "مرتجعات المبيعات", Color.FromArgb(150, 90, 30), () => Open(new ReturnsForm(true, 0)));
            AddBtn(panel, "مرتجعات المشتريات", Color.FromArgb(110, 90, 30), () => Open(new ReturnsForm(false, 0)));
            AddBtn(panel, "النواقص", Color.FromArgb(160, 40, 40), () => Open(new ShortagesForm()));
            AddBtn(panel, "أفضل الأصناف للفترة", Color.FromArgb(40, 120, 70), () => Open(new TopItemsForm()));
            AddBtn(panel, "التقارير والأرباح  (F6)", Color.FromArgb(0, 110, 110), () => Open(new ReportsForm()));
            AddBtn(panel, "الإعدادات  (F7)", Color.FromArgb(80, 80, 80), () => Open(new SettingsForm()));

            Controls.Add(panel);
            Controls.Add(lblToday);
            Controls.Add(lblTitle);

            KeyDown += (a, b) =>
            {
                switch (b.KeyCode)
                {
                    case Keys.F1: Run(() => Open(new InvoiceForm(true))); break;
                    case Keys.F2: Run(() => Open(new InvoiceForm(false))); break;
                    case Keys.F3: Run(() => Open(new ProductsForm())); break;
                    case Keys.F4: Run(() => Open(new ExpensesForm())); break;
                    case Keys.F5: Run(() => Open(new DebtsForm())); break;
                    case Keys.F6: Run(() => Open(new ReportsForm())); break;
                    case Keys.F7: Run(() => Open(new SettingsForm())); break;
                    case Keys.F8: Run(() => Open(new PartiesForm(true))); break;
                    case Keys.F9: Run(() => Open(new PartiesForm(false))); break;
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

        void AddBtn(FlowLayoutPanel p, string text, Color color, Action act)
        {
            var b = new Button
            {
                Text = text, Width = 300, Height = 110, Margin = new Padding(16),
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            b.Click += (a, e) => Run(act);
            p.Controls.Add(b);
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
            lblToday.Text = (overLimit > 0 ? "⚠ ديون فوق الحد: " + overLimit + "   |   " : "") + "اليوم:  مبيعات " + sales.ToString("N0") + " ل.س   |   فواتير " + count +
                            "   |   ربح تقريبي " + (sales - cogs).ToString("N0") + " ل.س   |   سعر الصرف " + Db.Rate.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
