using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>سجل فواتير المبيعات (isSales=true) أو المشتريات (false): بحث، تفاصيل، طباعة، إلغاء.</summary>
    public class InvoicesLogForm : Form
    {
        readonly bool sales;
        readonly DataGridView grid = new DataGridView();
        readonly DateTimePicker dtFrom = new DateTimePicker();
        readonly DateTimePicker dtTo = new DateTimePicker();
        readonly TextBox txtSearch = new TextBox();
        readonly Label lblTotal = new Label();
        DataTable tbl = new DataTable();
        readonly UndoStack undo = new UndoStack();

        const string SalesSql =
            "SELECT s.Id AS \"رقم الفاتورة\", substr(s.Date,1,16) AS \"التاريخ\", IFNULL(c.Name,'نقدي') AS \"الزبون\", IFNULL(s.PrevBalance,0) AS \"الرصيد السابق\", " +
            "s.Total AS \"المجموع\", s.Discount AS \"الخصم\", s.Net AS \"الصافي\", s.Paid AS \"المدفوع\", s.Remaining AS \"المتبقي\", IFNULL(u.Name,'') AS \"المستخدم\" " +
            "FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId LEFT JOIN Users u ON u.Id=s.UserId " +
            "WHERE substr(s.Date,1,10) BETWEEN @p0 AND @p1 AND (CAST(s.Id AS TEXT) LIKE @p2 OR IFNULL(c.Name,'نقدي') LIKE @p2) " +
            "ORDER BY s.Id DESC";

        const string PurchSql =
            "SELECT p.Id AS \"رقم\", substr(p.Date,1,16) AS \"التاريخ\", IFNULL(s.Name,'-') AS \"المورد\", IFNULL(p.InvoiceNo,'') AS \"رقم فاتورة المورد\", " +
            "IFNULL(p.Currency,'SYP') AS \"العملة\", p.Total AS \"المجموع\", p.Discount AS \"الخصم\", p.Net AS \"الصافي\", p.Paid AS \"المدفوع\", p.Remaining AS \"المتبقي\", IFNULL(u.Name,'') AS \"المستخدم\" " +
            "FROM Purchases p LEFT JOIN Suppliers s ON s.Id=p.SupplierId LEFT JOIN Users u ON u.Id=p.UserId " +
            "WHERE substr(p.Date,1,10) BETWEEN @p0 AND @p1 AND (CAST(p.Id AS TEXT) LIKE @p2 OR IFNULL(s.Name,'') LIKE @p2 OR IFNULL(p.InvoiceNo,'') LIKE @p2) " +
            "ORDER BY p.Id DESC";

        public InvoicesLogForm(bool isSales)
        {
            sales = isSales;
            UI.Rtl(this);
            KeyPreview = true;
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z && !(ActiveControl is TextBox) && Session.Can(Perm.EditInvoices)) { b.Handled = true; DoUndo(); } };
            Text = sales ? "سجل فواتير المبيعات" : "سجل فواتير المشتريات";
            WindowState = FormWindowState.Maximized;

            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.CellDoubleClick += (a, b) => { if (b.RowIndex >= 0) Details(); };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            dtFrom.Format = DateTimePickerFormat.Short;
            dtTo.Format = DateTimePickerFormat.Short;
            dtFrom.Margin = new Padding(4, 10, 4, 0);
            dtTo.Margin = new Padding(4, 10, 4, 0);
            dtFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dtTo.Value = DateTime.Today;
            txtSearch.Width = 220;
            txtSearch.Margin = new Padding(4, 10, 4, 0);
            txtSearch.PlaceholderText = sales ? "رقم الفاتورة أو اسم الزبون" : "رقم أو اسم المورد أو رقم فاتورته";

            bar.Controls.Add(UI.Lbl("من:")); bar.Controls.Add(dtFrom);
            bar.Controls.Add(UI.Lbl("إلى:")); bar.Controls.Add(dtTo);
            bar.Controls.Add(UI.Lbl("بحث:")); bar.Controls.Add(txtSearch);
            bar.Controls.Add(Btn("عرض", (a, b) => Reload()));
            bar.Controls.Add(Btn("تفاصيل الفاتورة", (a, b) => Details()));
            bool canEdit = Session.Can(Perm.EditInvoices);
            bool canReturn = Session.Can(sales ? Perm.SaleReturns : Perm.PurchaseReturns);
            if (canEdit) bar.Controls.Add(Btn("فتح الفاتورة للتعديل", (a, b) => EditCurrent()));
            bar.Controls.Add(Btn("طباعة الفاتورة", (a, b) => PrintCurrent()));
            if (canReturn) bar.Controls.Add(Btn("مرتجع من الفاتورة", (a, b) => OpenReturn()));
            if (canEdit) bar.Controls.Add(Btn("إلغاء الفاتورة", (a, b) => CancelInvoice()));
            if (canEdit) bar.Controls.Add(UndoStack.MakeButton(DoUndo));

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
            txtSearch.TextChanged += (a, b) => Reload();
        }

        static Button Btn(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            b.Click += h;
            return b;
        }

        void Reload()
        {
            string d1 = dtFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string d2 = dtTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string q = "%" + txtSearch.Text.Trim().Replace("%", "") + "%";
            tbl = Db.Query(sales ? SalesSql : PurchSql, d1, d2, q);
            grid.DataSource = tbl;
            string fmt = sales ? "N0" : "N2";
            foreach (var cn in new[] { "الرصيد السابق", "المجموع", "الخصم", "الصافي", "المدفوع", "المتبقي" })
            {
                if (grid.Columns[cn] == null) continue;
                grid.Columns[cn].DefaultCellStyle.Format = fmt;
                grid.Columns[cn].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            foreach (DataGridViewRow r in grid.Rows)
                if (Db.D(r.Cells["المتبقي"].Value) > 0) r.DefaultCellStyle.ForeColor = Color.Firebrick;
            if (grid.Rows.Count > 0) { grid.CurrentCell = grid.Rows[0].Cells[0]; grid.Rows[0].Selected = true; }

            if (sales)
            {
                double net = 0, paid = 0, rem = 0;
                foreach (DataRow r in tbl.Rows) { net += Db.D(r["الصافي"]); paid += Db.D(r["المدفوع"]); rem += Db.D(r["المتبقي"]); }
                lblTotal.Text = "عدد الفواتير: " + tbl.Rows.Count + "   |   الصافي: " + net.ToString("N0") +
                                "   |   المدفوع: " + paid.ToString("N0") + "   |   المتبقي (ديون): " + rem.ToString("N0") + " ل.س";
            }
            else
            {
                var sums = new Dictionary<string, double[]>();
                foreach (DataRow r in tbl.Rows)
                {
                    string cur = Convert.ToString(r["العملة"]);
                    if (!sums.ContainsKey(cur)) sums[cur] = new double[3];
                    sums[cur][0] += Db.D(r["الصافي"]); sums[cur][1] += Db.D(r["المدفوع"]); sums[cur][2] += Db.D(r["المتبقي"]);
                }
                string txt = "عدد الفواتير: " + tbl.Rows.Count;
                foreach (var kv in sums)
                    txt += "   ||   " + (kv.Key == "USD" ? "دولار" : "ليرة") + ": صافي " + kv.Value[0].ToString("N2") +
                           "  مدفوع " + kv.Value[1].ToString("N2") + "  متبقٍ " + kv.Value[2].ToString("N2");
                lblTotal.Text = txt;
            }
        }

        bool CurrentId(out int id)
        {
            id = 0;
            if (grid.Rows.Count == 0) { UI.Warn(this, "لا توجد فواتير في الفترة المحددة. غيّر التاريخ أو البحث."); return false; }
            if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) { UI.Warn(this, "اختر فاتورة أولاً (انقر على سطرها)."); return false; }
            id = Convert.ToInt32(grid.CurrentRow.Cells[0].Value);
            return true;
        }

        void Details()
        {
            int id;
            if (!CurrentId(out id)) return;
            try
            {
            var c = grid.CurrentRow.Cells;
            string head = sales
                ? "فاتورة بيع رقم " + id + "   |   " + c[1].Value + "   |   الزبون: " + c[2].Value +
                  "   |   الصافي: " + Db.D(c["الصافي"].Value).ToString("N0") + "   المدفوع: " + Db.D(c["المدفوع"].Value).ToString("N0") + "   المتبقي: " + Db.D(c["المتبقي"].Value).ToString("N0")
                : "فاتورة مشتريات رقم " + id + "   |   " + c[1].Value + "   |   المورد: " + c[2].Value + "   |   فاتورته: " + c[3].Value +
                  "   |   العملة: " + c[4].Value + "   |   الصافي: " + Db.D(c["الصافي"].Value).ToString("N2") + "   المدفوع: " + Db.D(c["المدفوع"].Value).ToString("N2") + "   المتبقي: " + Db.D(c["المتبقي"].Value).ToString("N2");
            new InvoiceDetailsForm(sales, id, head).Show();
            }
            catch (Exception ex) { UI.Warn(this, "تعذّر فتح التفاصيل: " + ex.Message); }
        }

        void PrintCurrent()
        {
            int id;
            if (!CurrentId(out id)) return;
            if (sales) InvoicePrinter.ShowSale(id); else InvoicePrinter.ShowPurchase(id);
        }

        void EditCurrent()
        {
            int id;
            if (!CurrentId(out id)) return;
            UI.ShowPage(new InvoiceForm(sales, id), Reload);
        }

        void OpenReturn()
        {
            int id;
            if (!CurrentId(out id)) return;
            UI.ShowPage(new ReturnsForm(sales, id), Reload);
        }

        void DoUndo()
        {
            string what;
            try { what = undo.Undo(); }
            catch (Exception ex) { UI.Warn(this, "تعذّر التراجع: " + ex.Message); return; }
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            Reload();
        }

        void CancelInvoice()
        {
            int id;
            if (!CurrentId(out id)) return;
            string msg = sales
                ? "إلغاء فاتورة البيع رقم " + id + "؟\nيعود المخزون كما كان وتُحذف الفاتورة من التقارير.\n(دفعات الزبون المسجّلة لا تتأثر.)"
                : "إلغاء فاتورة المشتريات رقم " + id + "؟\nتُنقَص كمياتها من المخزون وتُحذف من التقارير.\n(لا يُعاد سعر الشراء السابق للأصناف، ودفعات المورد المسجّلة لا تتأثر.)";
            if (!UI.Confirm(this, msg)) return;

            DataTable hdr = Db.Query("SELECT * FROM " + (sales ? "Sales" : "Purchases") + " WHERE Id=@p0", id);
            DataTable itm = Db.Query("SELECT * FROM " + (sales ? "SaleItems WHERE SaleId" : "PurchaseItems WHERE PurchaseId") + "=@p0", id);
            try
            {
                Db.CancelInvoice(sales, id);
            }
            catch (Exception ex)
            {
                UI.Warn(this, "تعذّر الإلغاء: " + ex.Message);
                return;
            }
            bool wasSales = sales;
            undo.Push("إلغاء فاتورة رقم " + id, () => Db.RestoreInvoice(wasSales, hdr, itm));
            Reload();
        }
    }

    /// <summary>أصناف فاتورة واحدة.</summary>
    public class InvoiceDetailsForm : Form
    {
        public InvoiceDetailsForm(bool sale, int id, string head)
        {
            UI.Rtl(this);
            Text = sale ? "تفاصيل فاتورة بيع" : "تفاصيل فاتورة مشتريات";
            Width = 960; Height = 560;
            StartPosition = FormStartPosition.CenterParent;

            string sql = sale
                ? "SELECT Name AS \"الصنف\", Qty AS \"الكمية\", UnitPrice AS \"السعر\", Qty*UnitPrice AS \"المجموع\" FROM SaleItems WHERE SaleId=@p0 ORDER BY Id"
                : "SELECT IFNULL(pr.Name,'(صنف محذوف)') AS \"الصنف\", pi.Qty AS \"الكمية\", pi.UnitCost AS \"سعر الشراء\", pi.Qty*pi.UnitCost AS \"المجموع\" " +
                  "FROM PurchaseItems pi LEFT JOIN Products pr ON pr.Id=pi.ProductId WHERE pi.PurchaseId=@p0 ORDER BY pi.Id";
            var grid = new DataGridView();
            UI.SetupGrid(grid, true);
            grid.DataSource = Db.Query(sql, id);
            UI.Col(grid, 0).FillWeight = 160;
            for (int i = 1; i < grid.Columns.Count; i++)
            {
                UI.Col(grid, i).DefaultCellStyle.Format = sale ? "N0" : "N2";
                UI.Col(grid, i).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            UI.Col(grid, 1).DefaultCellStyle.Format = "0.##";

            var lbl = new Label { Dock = DockStyle.Top, Height = 70, Padding = new Padding(10, 10, 10, 0), Text = head, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            Controls.Add(grid);
            Controls.Add(lbl);
        }
    }
}
