using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>ديون الزبائن الجارية، والديون المجمّدة (أكثر من N يوم بلا حركة)، وديون الموردين (دولار وليرة).</summary>
    public class DebtsForm : Form
    {
        const string ColSyp = "الرصيد (ل.س)";
        const string ColUsd = "الرصيد ($)";

        readonly DataGridView gridC = new DataGridView();
        readonly DataGridView gridF = new DataGridView();
        readonly DataGridView gridS = new DataGridView();
        readonly Label lblC = new Label();
        readonly Label lblF = new Label();
        readonly Label lblS = new Label();
        readonly UndoStack undo = new UndoStack();

        public DebtsForm()
        {
            UI.Rtl(this);
            KeyPreview = true;
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z && Session.Can(Perm.EditInvoices)) { b.Handled = true; DoUndo(); } };
            Text = "الديون والمدفوعات";
            Width = 1050; Height = 700;
            StartPosition = FormStartPosition.CenterParent;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildTab("ديون الزبائن (جارية)", gridC, lblC, 0));
            tabs.TabPages.Add(BuildTab("ديون مجمّدة", gridF, lblF, 1));
            tabs.TabPages.Add(BuildTab("ديون الموردين - دولار وليرة", gridS, lblS, 2));
            Controls.Add(tabs);

            try { Db.FreezeStaleDebts(); } catch { }
            RefreshAll();
        }

        TabPage BuildTab(string title, DataGridView g, Label lbl, int kind)   // 0 جارية، 1 مجمدة، 2 موردون
        {
            var page = new TabPage(title);
            UI.SetupGrid(g, true);
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            var btn = new Button { Text = kind == 2 ? "تسجيل دفعة للمورد المحدد" : "تسجيل دفعة من الزبون المحدد", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btn.Click += (a, b) => Pay(g, kind != 2);
            bar.Controls.Add(btn);
            if (kind != 2)
            {
                var bi = new Button { Text = "فواتير الدين (رابط الفاتورة)", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
                bi.Click += (a, b) => ShowInvoices(g);
                bar.Controls.Add(bi);
            }
            if (kind == 1)
            {
                var bu = new Button { Text = "إلغاء التجميد", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
                bu.Click += (a, b) => UnfreezeSelected();
                bar.Controls.Add(bu);
            }
            // حذف دفعة مسجّلة (بالتراجع) يحتاج صلاحية "تعديل وإلغاء الفواتير والتراجع عنها"
            if (Session.Can(Perm.EditInvoices)) bar.Controls.Add(UndoStack.MakeButton(DoUndo));
            lbl.AutoSize = true;
            lbl.Margin = new Padding(20, 14, 4, 0);
            lbl.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            bar.Controls.Add(lbl);
            page.Controls.Add(g);
            page.Controls.Add(bar);
            return page;
        }

        void DoUndo()
        {
            string what = undo.Undo();
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            RefreshAll();
        }

        void RefreshAll()
        {
            gridC.DataSource = Db.Query(Sql.CustomerBalances(0, "Bal DESC"));
            gridF.DataSource = Db.Query(Sql.CustomerBalances(1, "Bal DESC"));
            gridS.DataSource = Db.Query(Sql.PartyBalances(false));
            foreach (var g in new[] { gridC, gridF, gridS })
            {
                if (g.Columns["Id"] != null) g.Columns["Id"].Visible = false;
                if (g.Columns[ColSyp] != null) g.Columns[ColSyp].DefaultCellStyle.Format = "N0";
                if (g.Columns[ColUsd] != null) g.Columns[ColUsd].DefaultCellStyle.Format = "N2";
                foreach (DataGridViewRow r in g.Rows)
                {
                    bool over = g.Columns["تنبيه"] != null && Convert.ToString(r.Cells["تنبيه"].Value) != "";
                    if (over) { r.DefaultCellStyle.ForeColor = Color.DarkRed; r.DefaultCellStyle.Font = new Font("Segoe UI", 12, FontStyle.Bold); }
                }
            }
            int days = Db.FreezeDays;
            lblC.Text = "إجمالي الديون الجارية: " + SumPositive(gridC, ColSyp).ToString("N0") + " ل.س   |   تُجمَّد تلقائياً بعد " + days + " يوماً بلا حركة   |   ⚠ = فوق " + Db.DebtLimit.ToString("N0");
            lblF.Text = "إجمالي الديون المجمّدة: " + SumPositive(gridF, ColSyp).ToString("N0") + " ل.س   |   تعود جارية عند أي بيع أو دفعة";
            lblS.Text = "إجمالي ما علينا للموردين: " + SumPositive(gridS, ColUsd).ToString("N2") + " $   و   " + SumPositive(gridS, ColSyp).ToString("N0") + " ل.س";
        }

        static double SumPositive(DataGridView g, string col)
        {
            double t = 0;
            foreach (DataGridViewRow r in g.Rows) { double v = Db.D(r.Cells[col].Value); if (v > 0) t += v; }
            return t;
        }

        bool Row(DataGridView g, out int id, out string name)
        {
            id = 0; name = "";
            if (g.CurrentRow == null) { UI.Warn(this, "اختر سطراً أولاً."); return false; }
            id = Convert.ToInt32(g.CurrentRow.Cells["Id"].Value);
            name = Convert.ToString(g.CurrentRow.Cells["الاسم"].Value);
            return true;
        }

        void Pay(DataGridView g, bool customer)
        {
            int id; string name;
            if (!Row(g, out id, out name)) return;
            double usd = customer ? 0 : Db.D(g.CurrentRow.Cells[ColUsd].Value);
            double syp = Db.D(g.CurrentRow.Cells[ColSyp].Value);
            long pid = PayHelper.Pay(this, customer, id, name, usd, syp);
            if (pid > 0)
            {
                undo.Push("دفعة " + name, () => Db.Exec("DELETE FROM Payments WHERE Id=@p0", pid));
                RefreshAll();
            }
        }

        void ShowInvoices(DataGridView g)
        {
            int id; string name;
            if (!Row(g, out id, out name)) return;
            new DebtInvoicesForm(id, name).Show();
        }

        void UnfreezeSelected()
        {
            int id; string name;
            if (!Row(gridF, out id, out name)) return;
            Db.Unfreeze(id);
            undo.Push("إلغاء تجميد " + name, () => Db.Exec("UPDATE Customers SET Frozen=1, FrozenDate=@p1 WHERE Id=@p0", id, DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            RefreshAll();
        }
    }
}
