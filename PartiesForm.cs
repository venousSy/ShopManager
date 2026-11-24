using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>صفحة الزبائن (customers=true) أو الموردين (false): إضافة، تعديل، حذف، دفعات، كشف حساب.
    /// الموردون: الرصيد منفصل بعملتين (دولار وليرة).</summary>
    public class PartiesForm : Form
    {
        const string ColSyp = "الرصيد (ل.س)";
        const string ColUsd = "الرصيد ($)";

        readonly bool cust;
        readonly string table;
        readonly string type;
        readonly DataGridView grid = new DataGridView();
        readonly TextBox txtSearch = new TextBox();
        readonly Label lblTotal = new Label();
        DataTable tbl = new DataTable();
        readonly UndoStack undo = new UndoStack();

        public PartiesForm(bool customers)
        {
            cust = customers;
            table = cust ? "Customers" : "Suppliers";
            type = cust ? "C" : "S";
            UI.Rtl(this);
            KeyPreview = true;
            Text = cust ? "الزبائن" : "الموردون";
            WindowState = FormWindowState.Maximized;

            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.CellDoubleClick += (a, b) => { if (b.RowIndex >= 0) Statement(); };
            grid.DataBindingComplete += (a, b) => FormatGrid();

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            txtSearch.Width = 220;
            txtSearch.Margin = new Padding(4, 10, 4, 0);
            txtSearch.PlaceholderText = "بحث بالاسم";
            txtSearch.TextChanged += (a, b) => ApplyFilter();
            bar.Controls.Add(UI.Lbl("بحث:"));
            bar.Controls.Add(txtSearch);
            bar.Controls.Add(Btn(cust ? "إضافة زبون" : "إضافة مورد", (a, b) => AddOrEdit(false)));
            bar.Controls.Add(Btn("تعديل", (a, b) => AddOrEdit(true)));
            bar.Controls.Add(Btn("حذف", (a, b) => DeleteCurrent()));
            bar.Controls.Add(Btn(cust ? "تسجيل دفعة من الزبون" : "تسجيل دفعة للمورد", (a, b) => Pay()));
            bar.Controls.Add(Btn("كشف حساب", (a, b) => Statement()));
            if (cust) bar.Controls.Add(Btn("فواتير الدين", (a, b) => DebtInvoices()));
            if (cust) bar.Controls.Add(Btn("استيراد زبائن وديون (CSV)", (a, b) => ImportCustomers()));
            bar.Controls.Add(UndoStack.MakeButton(DoUndo));
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z && !(ActiveControl is TextBox)) { b.Handled = true; DoUndo(); } };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            lblTotal.Dock = DockStyle.Fill;
            lblTotal.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            lblTotal.Padding = new Padding(10, 10, 0, 0);
            bottom.Controls.Add(lblTotal);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(bar);
            Reload();
        }

        static Button Btn(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            b.Click += h;
            return b;
        }

        void DoUndo()
        {
            string what = undo.Undo();
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            Reload();
        }

        void Reload()
        {
            tbl = Db.Query(Sql.PartyBalances(cust));
            grid.DataSource = tbl;
            ApplyFilter();
            FormatGrid();

            double sumSyp = 0, sumUsd = 0;
            foreach (DataRow r in tbl.Rows)
            {
                double s = Db.D(r[ColSyp]); if (s > 0) sumSyp += s;
                if (!cust) { double u = Db.D(r[ColUsd]); if (u > 0) sumUsd += u; }
            }
            lblTotal.Text = cust
                ? "إجمالي ما لنا عند الزبائن: " + sumSyp.ToString("N0") + " ل.س   |   العدد: " + tbl.Rows.Count
                : "إجمالي ما علينا للموردين:  " + sumUsd.ToString("N2") + " $   و   " + sumSyp.ToString("N0") + " ل.س   |   العدد: " + tbl.Rows.Count;
        }

        void ApplyFilter()
        {
            string t = txtSearch.Text.Trim().Replace("'", "''").Replace("[", "").Replace("]", "").Replace("%", "").Replace("*", "");
            tbl.DefaultView.RowFilter = t == "" ? "" : "[الاسم] LIKE '%" + t + "%'";
            FormatGrid();
        }

        /// <summary>إخفاء Id وتنسيق الأرقام وتلوين الصفوف. يُستدعى بعد كل ربط أو تصفية لأن الجدول يعيد بناء أعمدته.</summary>
        void FormatGrid()
        {
            if (grid.Columns["Id"] != null) grid.Columns["Id"].Visible = false;
            foreach (var cn in new[] { ColSyp, ColUsd })
            {
                if (grid.Columns[cn] == null) continue;
                grid.Columns[cn].DefaultCellStyle.Format = cn == ColUsd ? "N2" : "N0";
                grid.Columns[cn].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.Cells.Count == 0) continue;
                DataGridViewCell cSyp = r.Cells[ColSyp];
                if (cSyp == null) continue;
                bool owed = Db.D(cSyp.Value) > 0 || (!cust && r.Cells[ColUsd] != null && Db.D(r.Cells[ColUsd].Value) > 0);
                r.DefaultCellStyle.ForeColor = owed ? Color.Firebrick : Color.Black;
                if (cust && grid.Columns["تنبيه"] != null && Convert.ToString(r.Cells["تنبيه"].Value) != "")
                {
                    r.DefaultCellStyle.ForeColor = Color.DarkRed;
                    r.DefaultCellStyle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
                    r.DefaultCellStyle.BackColor = Color.FromArgb(255, 232, 232);
                }
                else if (cust && grid.Columns["الحالة"] != null && Convert.ToString(r.Cells["الحالة"].Value) == "مجمد")
                    r.DefaultCellStyle.BackColor = Color.FromArgb(232, 232, 240);
            }
        }

        bool Current(out int id, out string name)
        {
            id = 0; name = "";
            if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) { UI.Warn(this, "اختر سطراً أولاً."); return false; }
            id = Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);
            name = Convert.ToString(grid.CurrentRow.Cells["الاسم"].Value);
            return true;
        }

        void AddOrEdit(bool edit)
        {
            int id = 0; string oldName = ""; string oldPhone = "";
            if (edit)
            {
                if (!Current(out id, out oldName)) return;
                oldPhone = Convert.ToString(grid.CurrentRow.Cells["الهاتف"].Value);
            }
            string name = Prompt.Ask(edit ? "تعديل" : "إضافة", "الاسم:", oldName);
            if (name == null) return;
            name = name.Trim();
            if (name == "") { UI.Warn(this, "الاسم مطلوب."); return; }
            string phone = Prompt.Ask(edit ? "تعديل" : "إضافة", "الهاتف (اختياري):", oldPhone);
            if (phone == null) phone = oldPhone;
            try
            {
                var ch = Db.TableChange.Begin(table);
                if (edit) Db.Exec("UPDATE " + table + " SET Name=@p0, Phone=@p1 WHERE Id=@p2", name, phone.Trim(), id);
                else Db.Exec("INSERT INTO " + table + "(Name,Phone) VALUES(@p0,@p1)", name, phone.Trim());
                undo.Push(edit ? "تعديل " + oldName : "إضافة " + name, ch.End());
                Reload();
            }
            catch (Exception ex)
            {
                UI.Warn(this, "تعذّر الحفظ (الاسم مكرر غالباً): " + ex.Message);
            }
        }

        void DeleteCurrent()
        {
            int id; string name;
            if (!Current(out id, out name)) return;
            // الحركات تشمل الفواتير والدفعات والمرتجعات، وللزبون أيضاً الرصيد السابق المستورد
            long moves = cust
                ? Convert.ToInt64(Db.Scalar("SELECT (SELECT COUNT(*) FROM Sales WHERE CustomerId=@p0) + (SELECT COUNT(*) FROM Payments WHERE PartyType='C' AND PartyId=@p0)" +
                                            " + (SELECT COUNT(*) FROM SaleReturns WHERE CustomerId=@p0) + (SELECT COUNT(*) FROM Customers WHERE Id=@p0 AND IFNULL(OpeningBalance,0)<>0)", id))
                : Convert.ToInt64(Db.Scalar("SELECT (SELECT COUNT(*) FROM Purchases WHERE SupplierId=@p0) + (SELECT COUNT(*) FROM Payments WHERE PartyType='S' AND PartyId=@p0)" +
                                            " + (SELECT COUNT(*) FROM PurchaseReturns WHERE SupplierId=@p0)", id));
            if (moves > 0) { UI.Warn(this, "لا يمكن حذف \"" + name + "\" لأن له حركات أو رصيداً مسجّلاً. يمكنك تعديل اسمه بدل حذفه."); return; }
            if (!UI.Confirm(this, "حذف \"" + name + "\"؟")) return;
            var ch = Db.TableChange.Begin(table);
            Db.Exec("DELETE FROM " + table + " WHERE Id=@p0", id);
            undo.Push("حذف " + name, ch.End());
            Reload();
        }

        void Pay()
        {
            int id; string name;
            if (!Current(out id, out name)) return;
            double usd = cust ? 0 : Db.D(grid.CurrentRow.Cells[ColUsd].Value);
            double syp = Db.D(grid.CurrentRow.Cells[ColSyp].Value);
            long pid = PayHelper.Pay(this, cust, id, name, usd, syp);
            if (pid > 0)
            {
                undo.Push("دفعة " + name, () => Db.Exec("DELETE FROM Payments WHERE Id=@p0", pid));
                Reload();
            }
        }

        void ImportCustomers()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "CSV|*.csv";
                dlg.Title = "اختر ملف الزبائن وديونهم";
                string guess = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "customers_import.csv");
                if (File.Exists(guess)) { dlg.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory; dlg.FileName = "customers_import.csv"; }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!UI.Confirm(this, "استيراد الزبائن وأرصدتهم من الملف؟\nالأسماء الجديدة تُضاف، والموجودة يُحدَّث رصيدها الافتتاحي. (يمكنك التراجع بعده)")) return;

                string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                var ch = Db.TableChange.Begin("Customers");
                string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                int added = 0, updated = 0, failed = 0;
                double totalDebt = 0;
                using (var c = Db.Open())
                using (var tx = c.BeginTransaction())
                {
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i])) continue;
                        string[] f = Csv.ParseLine(lines[i]);
                        string name = f.Length > 0 ? f[0].Trim() : "";
                        if (name == "") continue;
                        try
                        {
                            string phone = f.Length > 1 ? f[1].Trim() : "";
                            double bal = f.Length > 2 ? Db.Num(f[2]) : 0;
                            string date = f.Length > 3 && f[3].Trim() != "" ? f[3].Trim() : today;
                            var id = Db.ScalarTx(c, tx, "SELECT Id FROM Customers WHERE Name=@p0", name);
                            if (id != null && !(id is DBNull))
                            {
                                Db.ExecTx(c, tx, "UPDATE Customers SET Phone=CASE WHEN @p1<>'' THEN @p1 ELSE Phone END, OpeningBalance=@p2, OpeningDate=@p3, Frozen=0, FrozenDate=NULL WHERE Id=@p0",
                                    Convert.ToInt32(id), phone, bal, date);
                                updated++;
                            }
                            else
                            {
                                Db.ExecTx(c, tx, "INSERT INTO Customers(Name,Phone,OpeningBalance,OpeningDate) VALUES(@p0,@p1,@p2,@p3)", name, phone, bal, date);
                                added++;
                            }
                            if (bal > 0) totalDebt += bal;
                        }
                        catch { failed++; }
                    }
                    tx.Commit();
                }
                DataTable frozen = Db.FreezeStaleDebts();
                undo.Push("استيراد الزبائن", ch.End());   // بعد التجميد ليُلغى معه
                double frozenSum = 0;
                foreach (DataRow r in frozen.Rows) frozenSum += Db.D(r["Bal"]);
                Reload();
                UI.Info(this, "تم الاستيراد.\nزبائن جدد: " + added + "\nزبائن محدّثة أرصدتهم: " + updated + (failed > 0 ? "\nسطور فشلت: " + failed : "") +
                              "\nإجمالي الديون المستوردة: " + totalDebt.ToString("N0") + " ل.س" +
                              (frozen.Rows.Count > 0 ? "\n\nنُقل " + frozen.Rows.Count + " دين قديم (بلا حركة منذ أكثر من " + Db.FreezeDays + " يوماً) إلى الديون المجمّدة، بمجموع " + frozenSum.ToString("N0") + " ل.س." : ""));
            }
        }

        void DebtInvoices()
        {
            int id; string name;
            if (!Current(out id, out name)) return;
            new DebtInvoicesForm(id, name).Show();
        }

        void Statement()
        {
            int id; string name;
            if (!Current(out id, out name)) return;
            new StatementForm(type, id, name).Show();
        }
    }

    /// <summary>كشف حساب زبون أو مورد. للمورد: رصيد تراكمي منفصل بالدولار وبالليرة.</summary>
    public class StatementForm : Form
    {
        public StatementForm(string type, int id, string name)
        {
            UI.Rtl(this);
            Text = "كشف حساب: " + name;
            Width = 1000; Height = 640;
            StartPosition = FormStartPosition.CenterParent;
            bool cust = type == "C";

            string sql = cust
                ? "SELECT Date AS d, 'فاتورة بيع رقم ' || Id AS t, 'SYP' AS cur, Net AS debit, Paid AS credit, Id AS inv FROM Sales WHERE CustomerId=@p0 " +
                  "UNION ALL SELECT Date, 'دفعة ' || IFNULL(Note,''), 'SYP', 0, Amount, 0 FROM Payments WHERE PartyType='C' AND PartyId=@p0 " +
                  "UNION ALL SELECT Date, 'مرتجع من فاتورة رقم ' || SaleId, 'SYP', 0, DebtReduction, SaleId FROM SaleReturns WHERE CustomerId=@p0 AND DebtReduction>0 " +
                  "UNION ALL SELECT IFNULL(OpeningDate,''), 'رصيد سابق (مستورد من الدفتر القديم)', 'SYP', OpeningBalance, 0, 0 FROM Customers WHERE Id=@p0 AND IFNULL(OpeningBalance,0)<>0 ORDER BY d"
                : "SELECT Date AS d, 'فاتورة مشتريات ' || IFNULL(InvoiceNo,'') AS t, IFNULL(Currency,'SYP') AS cur, Net AS debit, Paid AS credit, Id AS inv FROM Purchases WHERE SupplierId=@p0 " +
                  "UNION ALL SELECT Date, 'دفعة ' || IFNULL(Note,''), IFNULL(Currency,'SYP'), 0, Amount, 0 FROM Payments WHERE PartyType='S' AND PartyId=@p0 " +
                  "UNION ALL SELECT Date, 'مرتجع من فاتورة مشتريات رقم ' || PurchaseId, IFNULL(Currency,'SYP'), 0, DebtReduction, PurchaseId FROM PurchaseReturns WHERE SupplierId=@p0 AND DebtReduction>0 ORDER BY d";
            DataTable src = Db.Query(sql, id);

            var t = new DataTable();
            t.Columns.Add("رقم", typeof(long));
            t.Columns.Add("التاريخ", typeof(string));
            t.Columns.Add("البيان", typeof(string));
            t.Columns.Add("العملة", typeof(string));
            t.Columns.Add(cust ? "عليه (قيمة الفاتورة)" : "علينا (قيمة الفاتورة)", typeof(double));
            t.Columns.Add("المدفوع", typeof(double));
            if (!cust) t.Columns.Add("الرصيد ($)", typeof(double));
            t.Columns.Add("الرصيد (ل.س)", typeof(double));

            double runUsd = 0, runSyp = 0;
            foreach (DataRow r in src.Rows)
            {
                double debit = Db.D(r["debit"]), credit = Db.D(r["credit"]);
                string cur = Convert.ToString(r["cur"]);
                if (cur == "USD") runUsd += debit - credit; else runSyp += debit - credit;
                string d = Convert.ToString(r["d"]);
                if (d.Length > 16) d = d.Substring(0, 16);
                long inv = Convert.ToInt64(r["inv"]);
                if (cust) t.Rows.Add(inv, d, Convert.ToString(r["t"]), "ل.س", debit, credit, runSyp);
                else t.Rows.Add(inv, d, Convert.ToString(r["t"]), cur == "USD" ? "$" : "ل.س", debit, credit, runUsd, runSyp);
            }

            var grid = new DataGridView();
            UI.SetupGrid(grid, true);
            grid.DataSource = t;
            foreach (DataGridViewColumn c in grid.Columns)
            {
                if (c.ValueType != typeof(double)) continue;
                c.DefaultCellStyle.Format = "N2";
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            UI.Col(grid, 0).Visible = false;
            UI.Col(grid, 2).FillWeight = 160;
            grid.CellDoubleClick += (a, b) =>
            {
                if (b.RowIndex < 0) return;
                long invId = Convert.ToInt64(grid.Rows[b.RowIndex].Cells[0].Value);
                if (invId <= 0)
                {
                    string desc = Convert.ToString(grid.Rows[b.RowIndex].Cells[2].Value);
                    if (cust && desc.StartsWith("رصيد سابق")) UI.Info(this, "هذا رصيد سابق مستورد من دفترك القديم ولا توجد له فاتورة.");
                    return;
                }
                string h = (cust ? "فاتورة بيع رقم " : "فاتورة مشتريات رقم ") + invId;
                new InvoiceDetailsForm(cust, (int)invId, h).Show();
            };

            string head = name + "   |   الرصيد الحالي: " +
                (cust ? runSyp.ToString("N0") + " ل.س" : runUsd.ToString("N2") + " $   و   " + runSyp.ToString("N0") + " ل.س");
            bool owed = runSyp > 0 || (!cust && runUsd > 0);
            var lbl = new Label
            {
                Dock = DockStyle.Top, Height = 50, Font = new Font("Segoe UI", 13, FontStyle.Bold),
                Padding = new Padding(10, 12, 0, 0),
                Text = head + (owed ? (cust ? "   (مطلوب منه)" : "   (مطلوب منا)") : "")
            };
            Controls.Add(grid);
            Controls.Add(lbl);
        }
    }
}
