using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>مرتجعات المبيعات (sale=true) أو مرتجعات المشتريات (false).
    /// يُرجِع أصنافاً من فاتورة محفوظة: يعدّل المخزون، ويخصم القيمة من دين الزبون/المورد و/أو يردّها نقداً.</summary>
    public class ReturnsForm : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly bool sale;
        readonly TextBox txtInv = new TextBox();
        readonly Label lblHead = new Label();
        readonly DataGridView grid = new DataGridView();
        readonly DataGridView logGrid = new DataGridView();
        readonly TextBox txtDebt = new TextBox();
        readonly Label lblTotal = new Label();
        readonly Label lblCash = new Label();
        readonly UndoStack undo = new UndoStack();

        int curId, partyId;
        double factor = 1, partyBal, purRate = 1;
        string cur = "SYP";
        bool busy, debtTouched;

        public ReturnsForm(bool isSale, int preload)
        {
            sale = isSale;
            UI.Rtl(this);
            Text = sale ? "مرتجعات المبيعات" : "مرتجعات المشتريات";
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z && !grid.IsCurrentCellInEditMode && !(ActiveControl is TextBox) && Session.Can(Perm.EditInvoices)) { b.Handled = true; DoUndo(); } };

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var pNew = new TabPage("مرتجع جديد");
            var pLog = new TabPage("سجل المرتجعات");

            // ----- مرتجع جديد -----
            UI.SetupGrid(grid, false);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.Columns.Add("PId", "PId"); grid.Columns["PId"].Visible = false;
            grid.Columns.Add("Name", "الصنف"); grid.Columns["Name"].ReadOnly = true; grid.Columns["Name"].FillWeight = 160;
            grid.Columns.Add("Sold", sale ? "الكمية المباعة" : "الكمية المشتراة"); grid.Columns["Sold"].ReadOnly = true;
            grid.Columns.Add("Prev", "مرتجع سابقاً"); grid.Columns["Prev"].ReadOnly = true;
            grid.Columns.Add("Qty", "الكمية المرتجعة");
            grid.Columns.Add("Price", sale ? "سعر البيع (بعد الخصم)" : "سعر الشراء"); grid.Columns["Price"].ReadOnly = true;
            grid.Columns.Add("Value", "القيمة"); grid.Columns["Value"].ReadOnly = true;
            grid.Columns.Add("Cost", "Cost"); grid.Columns["Cost"].Visible = false;
            foreach (var n in new[] { "Sold", "Prev", "Qty", "Price", "Value" })
                grid.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["Qty"].DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 204);
            grid.CellValueChanged += (a, b) => Recalc();

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            txtInv.Width = 140; txtInv.Font = new Font("Segoe UI", 14); txtInv.Margin = new Padding(4, 8, 4, 0);
            txtInv.KeyDown += (a, b) => { if (b.KeyCode == Keys.Enter) { b.SuppressKeyPress = true; LoadInvoice(); } };
            var btnLoad = new Button { Text = "تحميل الفاتورة", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btnLoad.Click += (a, b) => LoadInvoice();
            top.Controls.Add(UI.Lbl(sale ? "رقم فاتورة البيع:" : "رقم فاتورة المشتريات (الداخلي):"));
            top.Controls.Add(txtInv);
            top.Controls.Add(btnLoad);
            bool canEdit = Session.Can(Perm.EditInvoices);   // إلغاء المرتجعات والتراجع عنها
            if (canEdit) top.Controls.Add(UndoStack.MakeButton(DoUndo));

            lblHead.Dock = DockStyle.Top; lblHead.Height = 54; lblHead.Padding = new Padding(10, 12, 0, 0);
            lblHead.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            lblHead.Text = "اكتب رقم الفاتورة ثم Enter. (يمكنك فتح هذه الصفحة أيضاً من سجل الفواتير مع رقم الفاتورة.)";

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            lblTotal.AutoSize = false; lblTotal.Width = 200; lblTotal.Height = 38; lblTotal.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            lblTotal.TextAlign = ContentAlignment.MiddleCenter; lblTotal.BorderStyle = BorderStyle.FixedSingle; lblTotal.BackColor = Color.White; lblTotal.Text = "0";
            lblTotal.Margin = new Padding(4, 4, 12, 0);
            txtDebt.Width = 150; txtDebt.Font = new Font("Segoe UI", 14); txtDebt.Margin = new Padding(4, 4, 12, 0);
            txtDebt.TextChanged += (a, b) => { if (!busy) { debtTouched = true; Recalc(); } };
            lblCash.AutoSize = false; lblCash.Width = 200; lblCash.Height = 38; lblCash.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            lblCash.TextAlign = ContentAlignment.MiddleCenter; lblCash.BorderStyle = BorderStyle.FixedSingle; lblCash.BackColor = Color.White; lblCash.Text = "0";
            lblCash.Margin = new Padding(4, 4, 12, 0);
            var btnSave = new Button { Text = "حفظ المرتجع", Height = 44, Width = 150, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(8, 2, 4, 0) };
            btnSave.Click += (a, b) => SaveReturn();
            bottom.Controls.Add(UI.Lbl("قيمة المرتجع:")); bottom.Controls.Add(lblTotal);
            bottom.Controls.Add(UI.Lbl(sale ? "يُخصم من دين الزبون:" : "يُخصم من دين المورد:")); bottom.Controls.Add(txtDebt);
            bottom.Controls.Add(UI.Lbl(sale ? "يُردّ نقداً للزبون:" : "يُستردّ نقداً من المورد:")); bottom.Controls.Add(lblCash);
            bottom.Controls.Add(btnSave);

            pNew.Controls.Add(grid);
            pNew.Controls.Add(bottom);
            pNew.Controls.Add(lblHead);
            pNew.Controls.Add(top);

            // ----- السجل -----
            UI.SetupGrid(logGrid, true);
            logGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            logGrid.MultiSelect = false;
            var lbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            var btnCancel = new Button { Text = "إلغاء المرتجع المحدد", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btnCancel.Click += (a, b) => CancelSelected();
            var btnDet = new Button { Text = "تفاصيل المرتجع", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btnDet.Click += (a, b) => ShowReturnDetails();
            lbar.Controls.Add(btnDet);
            if (canEdit) lbar.Controls.Add(btnCancel);
            if (canEdit) lbar.Controls.Add(UndoStack.MakeButton(DoUndo));
            logGrid.CellDoubleClick += (a, b) => { if (b.RowIndex >= 0) ShowReturnDetails(); };
            pLog.Controls.Add(logGrid);
            pLog.Controls.Add(lbar);

            tabs.TabPages.Add(pNew);
            tabs.TabPages.Add(pLog);
            Controls.Add(tabs);

            ReloadLog();
            if (preload > 0) { txtInv.Text = preload.ToString(); LoadInvoice(); }
            Shown += (a, b) => txtInv.Focus();
        }

        void DoUndo()
        {
            grid.EndEdit();
            string what;
            try { what = undo.Undo(); }
            catch (Exception ex) { UI.Warn(this, "تعذّر التراجع: " + ex.Message); return; }
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            if (curId > 0) LoadInvoice();
            ReloadLog();
        }

        // ---------------- تحميل الفاتورة ----------------
        void LoadInvoice()
        {
            int id = (int)Db.Num(txtInv.Text);
            grid.Rows.Clear();
            curId = 0; partyId = 0; partyBal = 0; factor = 1; cur = "SYP"; purRate = 1; debtTouched = false;
            if (id <= 0) { UI.Warn(this, "اكتب رقم الفاتورة."); return; }

            DataTable h, it;
            if (sale)
            {
                h = Db.Query("SELECT s.Id, s.Date, IFNULL(s.CustomerId,0) AS PartyId, s.Total, s.Net, IFNULL(c.Name,'نقدي') AS PName FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId WHERE s.Id=@p0", id);
                it = Db.Query("SELECT si.ProductId, si.Name, si.Qty, si.UnitPrice, si.UnitCost, " +
                              "IFNULL((SELECT SUM(ri.Qty) FROM SaleReturnItems ri JOIN SaleReturns r ON r.Id=ri.ReturnId WHERE r.SaleId=si.SaleId AND ri.ProductId=si.ProductId),0) AS Ret " +
                              "FROM SaleItems si WHERE si.SaleId=@p0 ORDER BY si.Id", id);
            }
            else
            {
                h = Db.Query("SELECT p.Id, p.Date, IFNULL(p.SupplierId,0) AS PartyId, p.Total, p.Net, IFNULL(s.Name,'-') AS PName, IFNULL(p.Currency,'SYP') AS Cur, IFNULL(p.Rate,1) AS Rate FROM Purchases p LEFT JOIN Suppliers s ON s.Id=p.SupplierId WHERE p.Id=@p0", id);
                it = Db.Query("SELECT pi.ProductId, IFNULL(pr.Name,'(صنف محذوف)') AS Name, pi.Qty, pi.UnitCost AS UnitPrice, pi.UnitCost AS UnitCost, " +
                              "IFNULL((SELECT SUM(ri.Qty) FROM PurchaseReturnItems ri JOIN PurchaseReturns r ON r.Id=ri.ReturnId WHERE r.PurchaseId=pi.PurchaseId AND ri.ProductId=pi.ProductId),0) AS Ret " +
                              "FROM PurchaseItems pi LEFT JOIN Products pr ON pr.Id=pi.ProductId WHERE pi.PurchaseId=@p0 ORDER BY pi.Id", id);
            }
            if (h.Rows.Count == 0) { lblHead.Text = "لا توجد فاتورة بهذا الرقم."; return; }
            DataRow hr = h.Rows[0];
            curId = id;
            partyId = Convert.ToInt32(hr["PartyId"]);
            double total = Db.D(hr["Total"]), net = Db.D(hr["Net"]);
            factor = (sale && total > 0) ? net / total : 1;        // توزيع خصم الفاتورة على الأسعار
            if (!sale) { cur = Convert.ToString(hr["Cur"]); purRate = Db.D(hr["Rate"]); if (purRate <= 0) purRate = 1; }

            if (partyId > 0)
                partyBal = sale ? Db.CustomerBalance(partyId)
                                : Db.D(Db.Scalar("SELECT " + Sql.SupBalExpr("@p0", cur), partyId));

            string fmt = (sale || cur == "SYP") ? "N0" : "N2";
            lblHead.Text = (sale ? "فاتورة بيع رقم " : "فاتورة مشتريات رقم ") + id + "   |   " + Convert.ToString(hr["Date"]).Substring(0, Math.Min(16, Convert.ToString(hr["Date"]).Length)) +
                           "   |   " + (sale ? "الزبون: " : "المورد: ") + hr["PName"] + "   |   الصافي: " + net.ToString(fmt) + (sale ? " ل.س" : (cur == "USD" ? " $" : " ل.س")) +
                           (partyId > 0 ? "   |   رصيده الحالي: " + partyBal.ToString(fmt) : "");

            foreach (DataRow r in it.Rows)
            {
                double sold = Db.D(r["Qty"]), prev = Db.D(r["Ret"]), price = Db.D(r["UnitPrice"]) * factor;
                grid.Rows.Add(r["ProductId"], Convert.ToString(r["Name"]), sold.ToString("0.##", Inv), prev.ToString("0.##", Inv), "0",
                              price.ToString("0.##", Inv), "0", Db.D(r["UnitCost"]).ToString(Inv));
            }
            Recalc();
        }

        // ---------------- الحساب ----------------
        void Recalc()
        {
            if (busy) return;
            busy = true;
            try
            {
                double total = 0;
                string fmt = (sale || cur == "SYP") ? "N0" : "N2";
                foreach (DataGridViewRow r in grid.Rows)
                {
                    double q = Db.Num(Convert.ToString(r.Cells["Qty"].Value));
                    double p = Db.Num(Convert.ToString(r.Cells["Price"].Value));
                    double v = q * p;
                    r.Cells["Value"].Value = v.ToString(fmt);
                    total += v;
                }
                if (!debtTouched)
                    txtDebt.Text = (partyId > 0 && partyBal > 0) ? Math.Min(total, partyBal).ToString("0.##", Inv) : "0";
                double debt = Db.Num(txtDebt.Text);
                lblTotal.Text = total.ToString(fmt);
                lblCash.Text = Math.Max(total - debt, 0).ToString(fmt);
            }
            finally { busy = false; }
        }

        // ---------------- الحفظ ----------------
        void SaveReturn()
        {
            grid.EndEdit();
            if (curId <= 0) { UI.Warn(this, "حمّل فاتورة أولاً."); return; }
            string fmt = (sale || cur == "SYP") ? "N0" : "N2";
            var lines = new System.Collections.Generic.List<double[]>();   // ProductId, Qty, Price, Cost
            var names = new System.Collections.Generic.List<string>();
            double total = 0;
            foreach (DataGridViewRow r in grid.Rows)
            {
                double q = Db.Num(Convert.ToString(r.Cells["Qty"].Value));
                if (q <= 0) continue;
                double sold = Db.Num(Convert.ToString(r.Cells["Sold"].Value));
                double prev = Db.Num(Convert.ToString(r.Cells["Prev"].Value));
                if (q > sold - prev + 1e-9)
                { UI.Warn(this, "الكمية المرتجعة للصنف \"" + r.Cells["Name"].Value + "\" أكبر من المتاح للإرجاع (" + (sold - prev).ToString("0.##") + ")."); return; }
                double price = Db.Num(Convert.ToString(r.Cells["Price"].Value));
                lines.Add(new[] { Convert.ToDouble(r.Cells["PId"].Value, Inv), q, price, Db.Num(Convert.ToString(r.Cells["Cost"].Value)) });
                names.Add(Convert.ToString(r.Cells["Name"].Value));
                total += q * price;
            }
            if (lines.Count == 0) { UI.Warn(this, "اكتب الكمية المرتجعة لصنف واحد على الأقل."); return; }

            double debt = Db.Num(txtDebt.Text);
            if (partyId == 0 && debt > 0) { UI.Warn(this, "لا يوجد " + (sale ? "زبون" : "مورد") + " مسجّل على هذه الفاتورة، فيُردّ المبلغ نقداً بالكامل."); return; }
            if (debt < 0 || debt > total + 1e-9) { UI.Warn(this, "المبلغ المخصوم من الدين غير صحيح (يجب أن يكون بين 0 وقيمة المرتجع)."); return; }
            double cash = total - debt;
            if (!UI.Confirm(this, "حفظ مرتجع بقيمة " + total.ToString(fmt) + "؟\nيُخصم من الدين: " + debt.ToString(fmt) + "\nيُردّ/يُستردّ نقداً: " + cash.ToString(fmt))) return;

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            long rid;
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    object pid = partyId == 0 ? (object)null : partyId;
                    if (sale)
                        Db.ExecTx(c, tx, "INSERT INTO SaleReturns(Date,SaleId,CustomerId,Total,DebtReduction,RefundCash) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", now, curId, pid, total, debt, cash);
                    else
                        Db.ExecTx(c, tx, "INSERT INTO PurchaseReturns(Date,PurchaseId,SupplierId,Currency,Rate,Total,DebtReduction,RefundCash) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)", now, curId, pid, cur, purRate, total, debt, cash);
                    rid = Db.LastId(c, tx);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        int prodId = (int)lines[i][0];
                        if (sale)
                        {
                            Db.ExecTx(c, tx, "INSERT INTO SaleReturnItems(ReturnId,ProductId,Name,Qty,UnitPrice,UnitCost) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", rid, prodId, names[i], lines[i][1], lines[i][2], lines[i][3]);
                            Db.ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)+@p0 WHERE Id=@p1", lines[i][1], prodId);
                        }
                        else
                        {
                            Db.ExecTx(c, tx, "INSERT INTO PurchaseReturnItems(ReturnId,ProductId,Qty,UnitCost) VALUES(@p0,@p1,@p2,@p3)", rid, prodId, lines[i][1], lines[i][2]);
                            Db.ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)-@p0 WHERE Id=@p1", lines[i][1], prodId);
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex) { tx.Rollback(); UI.Warn(this, "تعذّر الحفظ: " + ex.Message); return; }
            }
            if (sale && partyId > 0) Db.Unfreeze(partyId);
            bool wasSale = sale;
            long rr = rid;
            undo.Push("مرتجع رقم " + rr, () => Db.CancelReturn(wasSale, rr));
            UI.Info(this, "تم حفظ المرتجع رقم " + rid + ".");
            LoadInvoice();
            ReloadLog();
        }

        // ---------------- السجل ----------------
        void ReloadLog()
        {
            logGrid.DataSource = Db.Query(sale
                ? "SELECT r.Id AS \"رقم المرتجع\", substr(r.Date,1,16) AS \"التاريخ\", r.SaleId AS \"الفاتورة\", IFNULL(c.Name,'نقدي') AS \"الزبون\", " +
                  "r.Total AS \"القيمة\", r.DebtReduction AS \"خُصم من الدين\", r.RefundCash AS \"رُدّ نقداً\" " +
                  "FROM SaleReturns r LEFT JOIN Customers c ON c.Id=r.CustomerId ORDER BY r.Id DESC"
                : "SELECT r.Id AS \"رقم المرتجع\", substr(r.Date,1,16) AS \"التاريخ\", r.PurchaseId AS \"الفاتورة\", IFNULL(s.Name,'-') AS \"المورد\", " +
                  "IFNULL(r.Currency,'SYP') AS \"العملة\", r.Total AS \"القيمة\", r.DebtReduction AS \"خُصم من الدين\", r.RefundCash AS \"استُرد نقداً\" " +
                  "FROM PurchaseReturns r LEFT JOIN Suppliers s ON s.Id=r.SupplierId ORDER BY r.Id DESC");
            foreach (DataGridViewColumn c in logGrid.Columns)
                if (c.ValueType == typeof(double)) { c.DefaultCellStyle.Format = "N2"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; }
        }

        void ShowReturnDetails()
        {
            if (logGrid.Rows.Count == 0) { UI.Warn(this, "لا توجد مرتجعات مسجّلة بعد."); return; }
            if (logGrid.CurrentRow == null) { UI.Warn(this, "اختر مرتجعاً من السجل (انقر على سطره)."); return; }
            long id = Convert.ToInt64(logGrid.CurrentRow.Cells[0].Value);
            try { new ReturnDetailsForm(sale, id).Show(); }
            catch (Exception ex) { UI.Warn(this, "تعذّر فتح التفاصيل: " + ex.Message); }
        }

        void CancelSelected()
        {
            if (logGrid.CurrentRow == null) { UI.Warn(this, "اختر مرتجعاً من السجل."); return; }
            long id = Convert.ToInt64(logGrid.CurrentRow.Cells[0].Value);
            if (!UI.Confirm(this, "إلغاء المرتجع رقم " + id + "؟ (يُعاد المخزون كما كان قبله، ويمكنك التراجع بعده)")) return;
            DataTable hdr = Db.Query("SELECT * FROM " + (sale ? "SaleReturns" : "PurchaseReturns") + " WHERE Id=@p0", id);
            DataTable itm = Db.Query("SELECT * FROM " + (sale ? "SaleReturnItems" : "PurchaseReturnItems") + " WHERE ReturnId=@p0", id);
            try { Db.CancelReturn(sale, id); }
            catch (Exception ex) { UI.Warn(this, "تعذّر الإلغاء: " + ex.Message); return; }
            bool wasSale = sale;
            undo.Push("إلغاء المرتجع رقم " + id, () => Db.RestoreReturn(wasSale, hdr, itm));
            ReloadLog();
            if (curId > 0) LoadInvoice();
        }
    }
}
