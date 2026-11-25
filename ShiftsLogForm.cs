using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>سجل الورديات المغلقة: من، متى، كم بِيع، والمتوقع مقابل المعدود في الصندوق.</summary>
    public class ShiftsLogForm : Form
    {
        readonly DataGridView grid = new DataGridView();
        readonly DateTimePicker dtFrom = new DateTimePicker();
        readonly DateTimePicker dtTo = new DateTimePicker();
        readonly Label lblTotal = new Label();

        public ShiftsLogForm()
        {
            UI.Rtl(this);
            Text = "سجل الورديات";
            WindowState = FormWindowState.Maximized;

            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.CellDoubleClick += (a, b) => { if (b.RowIndex >= 0) Details(); };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            dtFrom.Format = DateTimePickerFormat.Short; dtTo.Format = DateTimePickerFormat.Short;
            dtFrom.Margin = new Padding(4, 10, 4, 0); dtTo.Margin = new Padding(4, 10, 4, 0);
            dtFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dtTo.Value = DateTime.Today;
            bar.Controls.Add(UI.Lbl("من:")); bar.Controls.Add(dtFrom);
            bar.Controls.Add(UI.Lbl("إلى:")); bar.Controls.Add(dtTo);
            var btnDet = new Button { Text = "تفاصيل الوردية", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btnDet.Click += (a, b) => Details();
            bar.Controls.Add(btnDet);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            lblTotal.Dock = DockStyle.Fill;
            lblTotal.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            lblTotal.Padding = new Padding(10, 10, 0, 0);
            bottom.Controls.Add(lblTotal);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(bar);

            Reload();
            dtFrom.ValueChanged += (a, b) => Reload();
            dtTo.ValueChanged += (a, b) => Reload();
        }

        void Reload()
        {
            string d1 = dtFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string d2 = dtTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var t = Db.Query(
                "SELECT sh.Id, substr(sh.StartAt,1,16) AS \"من\", substr(sh.EndAt,1,16) AS \"إلى\", IFNULL(u.Name,'') AS \"المستخدم\", sh.Machine AS \"الجهاز\", " +
                "sh.Invoices AS \"عدد الفواتير\", sh.NetSales AS \"صافي المبيعات\", sh.CashExpected AS \"المتوقع بالصندوق\", " +
                "sh.CashCounted AS \"المعدود\", CASE WHEN sh.CashCounted IS NULL THEN NULL ELSE sh.CashCounted - sh.CashExpected END AS \"الفرق\" " +
                "FROM Shifts sh LEFT JOIN Users u ON u.Id=sh.UserId " +
                "WHERE substr(sh.EndAt,1,10) BETWEEN @p0 AND @p1 ORDER BY sh.Id DESC", d1, d2);
            grid.DataSource = t;
            grid.Columns["Id"].Visible = false;
            foreach (var cn in new[] { "صافي المبيعات", "المتوقع بالصندوق", "المعدود", "الفرق" })
            {
                grid.Columns[cn].DefaultCellStyle.Format = "N0";
                grid.Columns[cn].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            grid.Columns["عدد الفواتير"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            double net = 0, diff = 0; int shortCount = 0;
            foreach (DataGridViewRow r in grid.Rows)
            {
                net += Db.D(r.Cells["صافي المبيعات"].Value);
                object dv = r.Cells["الفرق"].Value;
                if (dv == null || dv is DBNull) { r.Cells["الفرق"].Style.ForeColor = Color.Gray; continue; }
                double d = Db.D(dv);
                diff += d;
                var cell = r.Cells["الفرق"];
                if (d < -0.5) { cell.Style.ForeColor = Color.Firebrick; cell.Style.Font = new Font("Segoe UI", 12, FontStyle.Bold); shortCount++; }
                else if (d > 0.5) cell.Style.ForeColor = Color.DarkOrange;
                else cell.Style.ForeColor = Color.DarkGreen;
            }
            lblTotal.Text = "عدد الورديات: " + grid.Rows.Count + "   |   صافي المبيعات: " + net.ToString("N0") + " ل.س" +
                            "   |   مجموع فروقات الصندوق: " + diff.ToString("N0") + " ل.س" +
                            (shortCount > 0 ? "   |   ⚠ ورديات فيها نقص: " + shortCount : "");
        }

        void Details()
        {
            if (grid.CurrentRow == null) { UI.Warn(this, "اختر وردية أولاً."); return; }
            int id = Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);
            new ShiftDetailsForm(id).Show();
        }
    }

    /// <summary>كل حركات وردية واحدة وأثر كل منها على الصندوق.</summary>
    public class ShiftDetailsForm : Form
    {
        public ShiftDetailsForm(int shiftId)
        {
            UI.Rtl(this);
            Text = "تفاصيل الوردية";
            Width = 1100; Height = 680;
            StartPosition = FormStartPosition.CenterParent;

            DataRow sh = Db.Query("SELECT sh.*, IFNULL(u.Name,'') AS UName FROM Shifts sh LEFT JOIN Users u ON u.Id=sh.UserId WHERE sh.Id=@p0", shiftId).Rows[0];
            var ranges = ParseMarks(Convert.ToString(sh["Marks"]));

            var grid = new DataGridView();
            UI.SetupGrid(grid, true);
            Controls.Add(grid);   // قبل DataSource: الأعمدة لا تُنشأ والجدول بلا نافذة أم
            string note = "";
            DataTable t;
            if (ranges.Count > 0) t = Moves(ranges);
            else
            {
                // وردية قديمة قبل حفظ النطاقات: نعرض فواتير البيع بحسب الوقت فقط
                t = Db.Query("SELECT 'فاتورة بيع' AS \"النوع\", s.Id AS \"الرقم\", substr(s.Date,1,16) AS \"الوقت\", IFNULL(c.Name,'نقدي') AS \"البيان\", " +
                             "s.Net AS \"المبلغ\", s.Paid AS \"أثرها على الصندوق\", IFNULL(u.Name,'') AS \"المستخدم\" " +
                             "FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId LEFT JOIN Users u ON u.Id=s.UserId " +
                             "WHERE s.Date >= @p0 AND s.Date <= @p1 ORDER BY s.Id", Convert.ToString(sh["StartAt"]), Convert.ToString(sh["EndAt"]));
                note = "   (وردية قديمة: تظهر فواتير البيع فقط)";
            }
            grid.DataSource = t;
            foreach (var cn in new[] { "المبلغ", "أثرها على الصندوق" })
            {
                grid.Columns[cn].DefaultCellStyle.Format = "N0";
                grid.Columns[cn].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            grid.Columns["البيان"].FillWeight = 160;

            Func<object, string> n = v => (v == null || v is DBNull) ? "—" : Db.D(v).ToString("N0");
            string counted = n(sh["CashCounted"]);
            string diff = (sh["CashCounted"] is DBNull) ? "—" : (Db.D(sh["CashCounted"]) - Db.D(sh["CashExpected"])).ToString("N0");
            var lbl = new Label
            {
                Dock = DockStyle.Top, Height = 74, Padding = new Padding(10, 8, 10, 0), Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Text = "من " + sh["StartAt"] + " إلى " + sh["EndAt"] + "   |   المستخدم: " + sh["UName"] + "   |   الجهاز: " + sh["Machine"] + note + "\n" +
                       "فواتير: " + sh["Invoices"] + "   |   صافي المبيعات: " + n(sh["NetSales"]) + "   |   المتوقع بالصندوق: " + n(sh["CashExpected"]) +
                       "   |   المعدود: " + counted + "   |   الفرق: " + diff
            };
            Controls.Add(lbl);
        }

        static Dictionary<string, long[]> ParseMarks(string s)
        {
            var d = new Dictionary<string, long[]>();
            foreach (var part in (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var ft = kv[1].Split('-');
                long a, b;
                if (ft.Length == 2 && long.TryParse(ft[0], out a) && long.TryParse(ft[1], out b)) d[kv[0]] = new[] { a, b };
            }
            return d;
        }

        /// <summary>كل حركات الوردية في جدول واحد. مجموع "أثرها على الصندوق" = المتوقع بالصندوق.</summary>
        static DataTable Moves(Dictionary<string, long[]> r)
        {
            Func<string, long[]> R = t => r.ContainsKey(t) ? r[t] : new long[] { 0, 0 };
            long[] s = R("Sales"), p = R("Payments"), e = R("Expenses"), sr = R("SaleReturns"), pu = R("Purchases"), pr = R("PurchaseReturns");
            string sql =
                "SELECT 'فاتورة بيع' AS \"النوع\", s.Id AS \"الرقم\", substr(s.Date,1,16) AS \"الوقت\", IFNULL(c.Name,'نقدي') AS \"البيان\", s.Net AS \"المبلغ\", s.Paid AS \"أثرها على الصندوق\", IFNULL(u.Name,'') AS \"المستخدم\", 1 AS K, s.Id AS O " +
                "FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId LEFT JOIN Users u ON u.Id=s.UserId WHERE s.Id>@p0 AND s.Id<=@p1 " +
                "UNION ALL SELECT CASE WHEN py.PartyType='C' THEN 'دفعة من زبون' ELSE 'دفعة لمورد' END, py.Id, substr(py.Date,1,16), " +
                "IFNULL(CASE WHEN py.PartyType='C' THEN c.Name ELSE sp.Name END,'') || CASE WHEN IFNULL(py.Note,'')<>'' THEN ' — ' || py.Note ELSE '' END, " +
                "CASE WHEN IFNULL(py.Currency,'SYP')='USD' THEN py.Amount*IFNULL(py.Rate,1) ELSE py.Amount END, " +
                "CASE WHEN py.PartyType='C' THEN py.Amount ELSE -(CASE WHEN IFNULL(py.Currency,'SYP')='USD' THEN py.Amount*IFNULL(py.Rate,1) ELSE py.Amount END) END, IFNULL(u.Name,''), 2, py.Id " +
                "FROM Payments py LEFT JOIN Customers c ON py.PartyType='C' AND c.Id=py.PartyId LEFT JOIN Suppliers sp ON py.PartyType='S' AND sp.Id=py.PartyId LEFT JOIN Users u ON u.Id=py.UserId WHERE py.Id>@p2 AND py.Id<=@p3 " +
                "UNION ALL SELECT 'مصروف', x.Id, x.Date, IFNULL(x.Description,'') || CASE WHEN IFNULL(x.Category,'')<>'' THEN ' (' || x.Category || ')' ELSE '' END, x.Amount, -x.Amount, IFNULL(u.Name,''), 3, x.Id " +
                "FROM Expenses x LEFT JOIN Users u ON u.Id=x.UserId WHERE x.Id>@p4 AND x.Id<=@p5 " +
                "UNION ALL SELECT 'مرتجع مبيعات', rt.Id, substr(rt.Date,1,16), 'من الفاتورة ' || IFNULL(rt.SaleId,''), rt.Total, -IFNULL(rt.RefundCash,0), IFNULL(u.Name,''), 4, rt.Id " +
                "FROM SaleReturns rt LEFT JOIN Users u ON u.Id=rt.UserId WHERE rt.Id>@p6 AND rt.Id<=@p7 " +
                "UNION ALL SELECT 'فاتورة مشتريات', pu.Id, substr(pu.Date,1,16), IFNULL(sp.Name,'-'), IFNULL(pu.NetSYP,pu.Net), -IFNULL(pu.PaidSYP,0), IFNULL(u.Name,''), 5, pu.Id " +
                "FROM Purchases pu LEFT JOIN Suppliers sp ON sp.Id=pu.SupplierId LEFT JOIN Users u ON u.Id=pu.UserId WHERE pu.Id>@p8 AND pu.Id<=@p9 " +
                "UNION ALL SELECT 'مرتجع مشتريات', prt.Id, substr(prt.Date,1,16), 'من الفاتورة ' || IFNULL(prt.PurchaseId,''), " +
                "CASE WHEN IFNULL(prt.Currency,'SYP')='USD' THEN prt.Total*IFNULL(prt.Rate,1) ELSE prt.Total END, " +
                "CASE WHEN IFNULL(prt.Currency,'SYP')='USD' THEN IFNULL(prt.RefundCash,0)*IFNULL(prt.Rate,1) ELSE IFNULL(prt.RefundCash,0) END, IFNULL(u.Name,''), 6, prt.Id " +
                "FROM PurchaseReturns prt LEFT JOIN Users u ON u.Id=prt.UserId WHERE prt.Id>@p10 AND prt.Id<=@p11 " +
                "ORDER BY K, O";
            var t = Db.Query(sql, s[0], s[1], p[0], p[1], e[0], e[1], sr[0], sr[1], pu[0], pu[1], pr[0], pr[1]);
            t.PrimaryKey = new DataColumn[0];   // أعمدة الترتيب المساعدة قد تُعتبر مفتاحاً؛ نفكّه قبل الحذف
            t.Columns.Remove("K"); t.Columns.Remove("O");
            return t;
        }
    }
}
