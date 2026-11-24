using System;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>المصاريف (إيجار، كهرباء، رواتب...) بالليرة. تدخل في حساب صافي الربح.</summary>
    public class ExpensesForm : Form
    {
        readonly SQLiteConnection conn;
        readonly SQLiteDataAdapter ad;
        readonly SQLiteCommandBuilder cb;
        readonly DataTable tbl = new DataTable();
        readonly DataGridView grid = new DataGridView();
        readonly Label lblTotal = new Label();
        readonly UndoStack undo = new UndoStack();
        bool suspend;

        public ExpensesForm()
        {
            UI.Rtl(this);
            Text = "المصاريف";
            Width = 1000; Height = 700;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            UI.SetupGrid(grid, false);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            var btnSave = new Button { Text = "حفظ", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btnSave.Click += (a, b) => SaveChanges();
            var btnDel = new Button { Text = "حذف السطر الحالي", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btnDel.Click += (a, b) =>
            {
                if (grid.CurrentRow != null && !grid.CurrentRow.IsNewRow && UI.Confirm(this, "حذف هذا المصروف؟ (يمكنك التراجع بعده)"))
                { grid.Rows.Remove(grid.CurrentRow); SaveChanges(); }
            };
            bar.Controls.Add(btnSave);
            bar.Controls.Add(btnDel);
            bar.Controls.Add(UndoStack.MakeButton(DoUndo));
            lblTotal.AutoSize = true;
            lblTotal.Margin = new Padding(20, 14, 4, 0);
            lblTotal.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            bar.Controls.Add(lblTotal);

            var hint = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 10, 8, 0), Text = "التاريخ بصيغة 2026-11-21. المبلغ بالليرة. السطر الأخير الفارغ لإضافة مصروف جديد." };

            Controls.Add(grid);
            Controls.Add(hint);
            Controls.Add(bar);

            conn = Db.Open();
            ad = new SQLiteDataAdapter("SELECT Id,Date,Description,Category,Amount FROM Expenses ORDER BY Date DESC, Id DESC", conn);
            cb = new SQLiteCommandBuilder(ad);
            grid.DefaultValuesNeeded += (a, b) =>
            {
                b.Row.Cells["Date"].Value = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                b.Row.Cells["Amount"].Value = 0;
            };

            tbl.ColumnChanging += (s2, e2) =>
            {
                if (suspend) return;
                DataRow row = e2.Row; DataColumn col = e2.Column;
                if (row.RowState == DataRowState.Detached) return;
                object old = row[col];
                if (Equals(old, e2.ProposedValue)) return;
                undo.Push("تعديل (" + col.ColumnName + ")", () => { suspend = true; try { row[col] = old; } finally { suspend = false; } });
            };
            tbl.RowChanged += (s2, e2) =>
            {
                if (suspend || e2.Action != DataRowAction.Add) return;
                DataRow row = e2.Row;
                undo.Push("إضافة مصروف", () => { suspend = true; try { row.Delete(); } finally { suspend = false; } });
            };
            tbl.RowDeleting += (s2, e2) =>
            {
                if (suspend) return;
                DataRow row = e2.Row;
                undo.Push("حذف مصروف", () => { suspend = true; try { row.RejectChanges(); } finally { suspend = false; } });
            };

            Reload();
            KeyDown += (a, b) =>
            {
                if (b.Control && b.KeyCode == Keys.Z && !grid.IsCurrentCellInEditMode) { b.Handled = true; DoUndo(); }
            };
            FormClosed += (a, b) => conn.Dispose();
        }

        void DoUndo()
        {
            grid.EndEdit();
            string what = undo.Undo();
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            grid.Refresh();
            UpdateTotal();
        }

        void Reload()
        {
            suspend = true;
            try { tbl.Clear(); ad.Fill(tbl); }
            finally { suspend = false; }
            grid.DataSource = tbl;
            grid.Columns["Id"].Visible = false;
            grid.Columns["Date"].HeaderText = "التاريخ";
            grid.Columns["Description"].HeaderText = "البيان";
            grid.Columns["Category"].HeaderText = "النوع (إيجار/كهرباء/رواتب...)";
            grid.Columns["Amount"].HeaderText = "المبلغ (ل.س)";
            grid.Columns["Description"].FillWeight = 160;
            UpdateTotal();
        }

        void UpdateTotal()
        {
            double total = 0;
            foreach (DataRow r in tbl.Rows)
                if (r.RowState != DataRowState.Deleted) total += Db.D(r["Amount"]);
            lblTotal.Text = "إجمالي المصاريف المعروضة: " + total.ToString("N0") + " ل.س";
        }

        void SaveChanges()
        {
            try
            {
                grid.EndEdit();
                BindingContext[tbl].EndCurrentEdit();
                DataTable snap = Db.Snapshot("Expenses");
                suspend = true;
                try
                {
                    foreach (DataRow r in tbl.Rows)
                    {
                        if (r.RowState == DataRowState.Deleted || r.RowState == DataRowState.Unchanged || r.RowState == DataRowState.Detached) continue;
                        if (r["Amount"] == DBNull.Value || string.IsNullOrWhiteSpace(Convert.ToString(r["Amount"]))) r["Amount"] = 0;
                        if (r["Date"] == DBNull.Value || string.IsNullOrWhiteSpace(Convert.ToString(r["Date"])))
                            r["Date"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    ad.Update(tbl);
                }
                finally { suspend = false; }
                undo.Clear();
                undo.Push("حفظ المصاريف", () => { Db.Restore("Expenses", snap); Reload(); });
                Reload();
            }
            catch (Exception ex)
            {
                UI.Warn(this, "تعذّر الحفظ: " + ex.Message);
            }
        }
    }
}
