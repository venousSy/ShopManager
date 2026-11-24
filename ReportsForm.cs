using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>الأرباح (للفترة) + رأس المال (الحالي) + المخزون الافتتاحي. للقراءة فقط.
    /// أفضل الأصناف والنواقص في صفحتين مستقلتين.</summary>
    public class ReportsForm : Form
    {
        readonly DateTimePicker dtFrom = new DateTimePicker();
        readonly DateTimePicker dtTo = new DateTimePicker();
        readonly DataGridView grid = new DataGridView();

        public ReportsForm()
        {
            UI.Rtl(this);
            Text = "التقارير والأرباح";
            WindowState = FormWindowState.Maximized;

            UI.SetupGrid(grid, true);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            dtFrom.Format = DateTimePickerFormat.Short;
            dtTo.Format = DateTimePickerFormat.Short;
            dtFrom.Margin = new Padding(4, 10, 4, 0);
            dtTo.Margin = new Padding(4, 10, 4, 0);
            dtFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dtTo.Value = DateTime.Today;
            var btn = new Button { Text = "عرض التقرير", AutoSize = true, Margin = new Padding(12, 8, 4, 0) };
            btn.Click += (a, b) => ReloadAll();
            bar.Controls.Add(UI.Lbl("من:")); bar.Controls.Add(dtFrom);
            bar.Controls.Add(UI.Lbl("إلى:")); bar.Controls.Add(dtTo);
            bar.Controls.Add(btn);

            Controls.Add(grid);
            Controls.Add(bar);
            ReloadAll();
        }

        static string Fmt(double v) { return v.ToString("N0"); }

        void ReloadAll()
        {
            string d1 = dtFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string d2 = dtTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            double rate = Db.Rate;

            // ---- الأرباح للفترة ----
            double revenue = Db.D(Db.Scalar("SELECT IFNULL(SUM(Net),0) FROM Sales WHERE substr(Date,1,10) BETWEEN @p0 AND @p1", d1, d2));
            long invoices = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM Sales WHERE substr(Date,1,10) BETWEEN @p0 AND @p1", d1, d2));
            double cogs = Db.D(Db.Scalar("SELECT IFNULL(SUM(si.Qty*si.UnitCost),0) FROM SaleItems si JOIN Sales s ON s.Id=si.SaleId WHERE substr(s.Date,1,10) BETWEEN @p0 AND @p1", d1, d2));
            double expenses = Db.D(Db.Scalar("SELECT IFNULL(SUM(Amount),0) FROM Expenses WHERE substr(Date,1,10) BETWEEN @p0 AND @p1", d1, d2));
            double retTotal = Db.D(Db.Scalar("SELECT IFNULL(SUM(Total),0) FROM SaleReturns WHERE substr(Date,1,10) BETWEEN @p0 AND @p1", d1, d2));
            double retCost = Db.D(Db.Scalar("SELECT IFNULL(SUM(ri.Qty*ri.UnitCost),0) FROM SaleReturnItems ri JOIN SaleReturns r ON r.Id=ri.ReturnId WHERE substr(r.Date,1,10) BETWEEN @p0 AND @p1", d1, d2));
            revenue -= retTotal;
            cogs -= retCost;
            double gross = revenue - cogs;
            double netProfit = gross - expenses;

            // ---- رأس المال (حتى الآن) ----
            double stockValue = Db.D(Db.Scalar("SELECT IFNULL(SUM(Stock*CostPrice*CASE WHEN CostCurrency='USD' THEN @p0 ELSE 1 END),0) FROM Products WHERE Stock>0", rate));
            long stockItems = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM Products WHERE Stock>0"));
            double salesPaid = Db.D(Db.Scalar("SELECT IFNULL(SUM(Paid),0) FROM Sales"));
            double salesRem = Db.D(Db.Scalar("SELECT IFNULL(SUM(Remaining),0) FROM Sales"));
            double payC = Db.D(Db.Scalar("SELECT IFNULL(SUM(Amount),0) FROM Payments WHERE PartyType='C'"));
            double purPaid = Db.D(Db.Scalar("SELECT IFNULL(SUM(PaidSYP),0) FROM Purchases"));
            double payS = Db.D(Db.Scalar("SELECT IFNULL(SUM(CASE WHEN IFNULL(Currency,'SYP')='USD' THEN Amount*IFNULL(Rate,1) ELSE Amount END),0) FROM Payments WHERE PartyType='S'"));
            double supUsd = Db.D(Db.Scalar("SELECT IFNULL(SUM(Remaining),0) FROM Purchases WHERE IFNULL(Currency,'SYP')='USD'"))
                          - Db.D(Db.Scalar("SELECT IFNULL(SUM(Amount),0) FROM Payments WHERE PartyType='S' AND IFNULL(Currency,'SYP')='USD'"));
            double supSyp = Db.D(Db.Scalar("SELECT IFNULL(SUM(Remaining),0) FROM Purchases WHERE IFNULL(Currency,'SYP')='SYP'"))
                          - Db.D(Db.Scalar("SELECT IFNULL(SUM(Amount),0) FROM Payments WHERE PartyType='S' AND IFNULL(Currency,'SYP')='SYP'"));
            double expAll = Db.D(Db.Scalar("SELECT IFNULL(SUM(Amount),0) FROM Expenses"));
            double openCash = Db.Num(Db.Get("opening_cash", "0"));
            double openCapSet = Db.Num(Db.Get("opening_capital", "0"));
            double openStock = Db.Num(Db.Get("opening_stock_value", "0"));
            string openStockDate = Db.Get("opening_stock_date", "");
            string openStockItems = Db.Get("opening_stock_items", "");

            double retDebtC = Db.D(Db.Scalar("SELECT IFNULL(SUM(DebtReduction),0) FROM SaleReturns"));
            double retCashC = Db.D(Db.Scalar("SELECT IFNULL(SUM(RefundCash),0) FROM SaleReturns"));
            double retCashS = Db.D(Db.Scalar("SELECT IFNULL(SUM(CASE WHEN IFNULL(Currency,'SYP')='USD' THEN RefundCash*IFNULL(Rate,1) ELSE RefundCash END),0) FROM PurchaseReturns"));
            double retDebtUsd = Db.D(Db.Scalar("SELECT IFNULL(SUM(DebtReduction),0) FROM PurchaseReturns WHERE IFNULL(Currency,'SYP')='USD'"));
            double retDebtSyp = Db.D(Db.Scalar("SELECT IFNULL(SUM(DebtReduction),0) FROM PurchaseReturns WHERE IFNULL(Currency,'SYP')='SYP'"));
            supUsd -= retDebtUsd;
            supSyp -= retDebtSyp;
            double cash = openCash + salesPaid + payC - purPaid - payS - expAll - retCashC + retCashS;
            double openingDebts = Db.D(Db.Scalar("SELECT IFNULL(SUM(OpeningBalance),0) FROM Customers"));
            double custDebt = salesRem - payC - retDebtC + openingDebts;
            double supDebt = supUsd * rate + supSyp;
            double capital = stockValue + cash + custDebt - supDebt;
            double openCap = openCapSet > 0 ? openCapSet : openCash + openStock + openingDebts;

            var t = new DataTable();
            t.Columns.Add("البند");
            t.Columns.Add("القيمة");
            t.Rows.Add("الأرباح عن الفترة: " + d1 + " إلى " + d2, "");
            t.Rows.Add("عدد فواتير البيع", invoices.ToString());
            t.Rows.Add("صافي المبيعات (بعد الخصم وبعد خصم المرتجعات)", Fmt(revenue) + " ل.س");
            if (retTotal > 0) t.Rows.Add("منها مرتجعات مبيعات مخصومة", Fmt(retTotal) + " ل.س");
            t.Rows.Add("تكلفة البضاعة المباعة", Fmt(cogs) + " ل.س");
            t.Rows.Add("مجمل الربح", Fmt(gross) + " ل.س");
            t.Rows.Add("المصاريف", Fmt(expenses) + " ل.س");
            t.Rows.Add("صافي الربح", Fmt(netProfit) + " ل.س");
            t.Rows.Add("هامش صافي الربح", revenue > 0 ? (netProfit / revenue * 100).ToString("0.#") + " %" : "-");

            t.Rows.Add("المخزون الافتتاحي (عند بدء العمل)", "");
            if (openStock > 0)
                t.Rows.Add("قيمة المخزون الافتتاحي" + (openStockDate != "" ? " (معتمد بتاريخ " + openStockDate + (openStockItems != "" ? "، " + openStockItems + " صنف" : "") + ")" : ""), Fmt(openStock) + " ل.س");
            else
                t.Rows.Add("قيمة المخزون الافتتاحي", "غير معتمد بعد: من شاشة الأصناف اضغط (اعتماد المخزون كمخزون افتتاحي) بعد إدخال الجرد");
            t.Rows.Add("النقد الافتتاحي في الصندوق", Fmt(openCash) + " ل.س");
            if (openingDebts != 0) t.Rows.Add("ديون الزبائن المستوردة من الدفتر القديم", Fmt(openingDebts) + " ل.س");
            t.Rows.Add("رأس المال المبدئي" + (openCapSet > 0 ? " (من الإعدادات)" : " (محسوب: النقد الافتتاحي + المخزون الافتتاحي + الديون المستوردة)"), Fmt(openCap) + " ل.س");

            t.Rows.Add("رأس المال (حتى الآن)", "");
            t.Rows.Add("قيمة المخزون الحالي بسعر الشراء (" + stockItems + " صنف)", Fmt(stockValue) + " ل.س");
            t.Rows.Add("النقد في الصندوق (محسوب)", Fmt(cash) + " ل.س");
            t.Rows.Add("ديون الزبائن (لنا)", Fmt(custDebt) + " ل.س");
            double frozenDebt = 0;
            foreach (DataRow fr in Db.Query(Sql.CustomerBalances(1, "Name")).Rows) { double bv = Db.D(fr["الرصيد (ل.س)"]); if (bv > 0) frozenDebt += bv; }
            t.Rows.Add("   منها ديون مجمّدة (مضى عليها أكثر من " + Db.FreezeDays + " يوماً بلا حركة)", Fmt(frozenDebt) + " ل.س");
            t.Rows.Add("ديون الموردين بالدولار (علينا)", supUsd.ToString("N2") + " $");
            t.Rows.Add("ديون الموردين بالليرة (علينا)", Fmt(supSyp) + " ل.س");
            t.Rows.Add("ديون الموردين (المعادل بالليرة بسعر الصرف الحالي)", Fmt(supDebt) + " ل.س");
            t.Rows.Add("رأس المال الحالي", Fmt(capital) + " ل.س");
            t.Rows.Add("النمو (الحالي − المبدئي)", Fmt(capital - openCap) + " ل.س");
            t.Rows.Add("رأس المال الحالي = المخزون + النقد + ديون الزبائن − ديون الموردين (الدولار بسعر الصرف الحالي). النقد يُحسب من الرصيد الافتتاحي والحركات المسجّلة فقط. المخزون بسعر الشراء الأخير.", "");
            grid.DataSource = t;
            UI.Col(grid, 0).FillWeight = 70;
            UI.Col(grid, 1).FillWeight = 30;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            foreach (DataGridViewRow r in grid.Rows)
            {
                string label = Convert.ToString(r.Cells[0].Value);
                bool header = Convert.ToString(r.Cells[1].Value) == "";
                if (header) { r.DefaultCellStyle.BackColor = Color.LightSteelBlue; r.DefaultCellStyle.Font = new Font("Segoe UI", 12, FontStyle.Bold); }
                if (label == "صافي الربح" || label == "رأس المال الحالي")
                { r.DefaultCellStyle.Font = new Font("Segoe UI", 14, FontStyle.Bold); r.DefaultCellStyle.BackColor = Color.LightYellow; }
            }
            grid.ClearSelection();
        }
    }

    /// <summary>صفحة مستقلة: أفضل الأصناف مبيعاً وربحاً عن فترة.</summary>
    public class TopItemsForm : Form
    {
        readonly DateTimePicker dtFrom = new DateTimePicker();
        readonly DateTimePicker dtTo = new DateTimePicker();
        readonly DataGridView grid = new DataGridView();

        public TopItemsForm()
        {
            UI.Rtl(this);
            Text = "أفضل الأصناف للفترة";
            WindowState = FormWindowState.Maximized;
            UI.SetupGrid(grid, true);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            dtFrom.Format = DateTimePickerFormat.Short;
            dtTo.Format = DateTimePickerFormat.Short;
            dtFrom.Margin = new Padding(4, 10, 4, 0);
            dtTo.Margin = new Padding(4, 10, 4, 0);
            dtFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dtTo.Value = DateTime.Today;
            var btn = new Button { Text = "عرض", AutoSize = true, Margin = new Padding(12, 8, 4, 0) };
            btn.Click += (a, b) => Reload();
            bar.Controls.Add(UI.Lbl("من:")); bar.Controls.Add(dtFrom);
            bar.Controls.Add(UI.Lbl("إلى:")); bar.Controls.Add(dtTo);
            bar.Controls.Add(btn);
            var note = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 10, 8, 0), Text = "مرتّبة حسب الربح. الربح محسوب قبل خصم الفاتورة. يظهر أفضل 100 صنف." };

            Controls.Add(grid);
            Controls.Add(note);
            Controls.Add(bar);
            Reload();
            dtFrom.ValueChanged += (a, b) => Reload();
            dtTo.ValueChanged += (a, b) => Reload();
        }

        void Reload()
        {
            string d1 = dtFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string d2 = dtTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            grid.DataSource = Db.Query(
                "SELECT si.Name AS \"الصنف\", SUM(si.Qty) AS \"الكمية المباعة\", SUM(si.Qty*si.UnitPrice) AS \"المبيعات\", SUM(si.Qty*(si.UnitPrice-si.UnitCost)) AS \"الربح\" " +
                "FROM SaleItems si JOIN Sales s ON s.Id=si.SaleId WHERE substr(s.Date,1,10) BETWEEN @p0 AND @p1 " +
                "GROUP BY si.Name ORDER BY 4 DESC LIMIT 100", d1, d2);
            UI.Col(grid, 0).FillWeight = 160;
            foreach (var n in new[] { "المبيعات", "الربح" })
                if (grid.Columns[n] != null) { grid.Columns[n].DefaultCellStyle.Format = "N0"; grid.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; }
            if (grid.Columns["الكمية المباعة"] != null) { grid.Columns["الكمية المباعة"].DefaultCellStyle.Format = "0.##"; grid.Columns["الكمية المباعة"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; }
        }
    }

    /// <summary>صفحة مستقلة: النواقص (الأصناف التي وصلت للحد الأدنى أو نفدت).</summary>
    public class ShortagesForm : Form
    {
        readonly DataGridView grid = new DataGridView();
        readonly TextBox txtSearch = new TextBox();

        public ShortagesForm()
        {
            UI.Rtl(this);
            Text = "النواقص";
            WindowState = FormWindowState.Maximized;
            UI.SetupGrid(grid, true);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            txtSearch.Width = 240;
            txtSearch.Margin = new Padding(4, 10, 4, 0);
            txtSearch.PlaceholderText = "بحث بالاسم";
            txtSearch.TextChanged += (a, b) => Reload();
            bar.Controls.Add(UI.Lbl("بحث:"));
            bar.Controls.Add(txtSearch);
            var note = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 10, 8, 0), Text = "تظهر الأصناف التي مخزونها أقل من أو يساوي الحد الأدنى. حدّد الحد الأدنى لكل صنف من شاشة الأصناف." };

            Controls.Add(grid);
            Controls.Add(note);
            Controls.Add(bar);
            Reload();
        }

        void Reload()
        {
            string q = "%" + txtSearch.Text.Trim().Replace("%", "") + "%";
            grid.DataSource = Db.Query(
                "SELECT Name AS \"الصنف\", IFNULL(Category,'') AS \"القسم\", Stock AS \"المخزون الحالي\", MinStock AS \"الحد الأدنى\", MinStock-Stock AS \"النقص عن الحد\" " +
                "FROM Products WHERE MinStock>0 AND Stock<=MinStock AND Name LIKE @p0 ORDER BY Category, Name", q);
            UI.Col(grid, 0).FillWeight = 160;
            foreach (var n in new[] { "المخزون الحالي", "الحد الأدنى", "النقص عن الحد" })
                if (grid.Columns[n] != null) { grid.Columns[n].DefaultCellStyle.Format = "0.##"; grid.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; }
        }
    }
}
