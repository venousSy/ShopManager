using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>
    /// ربط الباركود بالأصناف الموجودة بالمرور على الرفوف: امسح القطعة ← اكتب حرفين أو ثلاثة من اسمها ← Enter.
    /// خانة واحدة للمسح والبحث معاً، فلا حاجة للفأرة. مسح باركود جديد أثناء البحث يبدأ القطعة التالية.
    /// </summary>
    public class BarcodeLinkForm : Form
    {
        readonly TextBox txt = new TextBox();
        readonly Label lblPrompt = new Label();
        readonly Label lblResult = new Label();
        readonly Label lblCount = new Label();
        readonly DataGridView grid = new DataGridView();
        readonly ListBox lstDone = new ListBox();
        readonly UndoStack undo = new UndoStack();

        DataTable all;              // كل الأصناف (Id, Name, Category, Barcode, SellPrice)
        SmartSearch index;
        string pending;             // الباركود الممسوح الذي ينتظر اختيار صنفه (null = ننتظر مسحاً)

        public BarcodeLinkForm()
        {
            UI.Rtl(this);
            Text = "ربط الباركود بالأصناف";
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            var top = new Panel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(16, 10, 16, 6), BackColor = Color.FromArgb(235, 242, 250) };
            lblPrompt.Dock = DockStyle.Top; lblPrompt.Height = 44;
            lblPrompt.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            txt.Dock = DockStyle.Top;
            txt.Font = new Font("Segoe UI", 22);
            lblResult.Dock = DockStyle.Bottom; lblResult.Height = 40;
            lblResult.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            top.Controls.Add(txt);
            top.Controls.Add(lblPrompt);
            top.Controls.Add(lblResult);

            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.TabStop = false;
            grid.CellDoubleClick += (a, e) => { if (e.RowIndex >= 0) LinkSelected(); };

            var side = new Panel { Dock = DockStyle.Left, Width = 420, Padding = new Padding(8) };
            lblCount.Dock = DockStyle.Top; lblCount.Height = 70;
            lblCount.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            var lblDone = new Label { Text = "آخر ما رُبط (Ctrl+Z للتراجع عن الأخير):", Dock = DockStyle.Top, Height = 32 };
            lstDone.Dock = DockStyle.Fill;
            lstDone.Font = new Font("Segoe UI", 11);
            lstDone.TabStop = false;
            var btnUndo = UndoStack.MakeButton(DoUndo);
            btnUndo.Dock = DockStyle.Bottom; btnUndo.AutoSize = false; btnUndo.Height = 44;
            side.Controls.Add(lstDone);
            side.Controls.Add(lblDone);
            side.Controls.Add(lblCount);
            side.Controls.Add(btnUndo);

            var help = new Label
            {
                Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10, 6, 10, 0),
                Text = "امسح القطعة ← اكتب حرفين أو ثلاثة من اسمها ← Enter يربط الصنف الأول (أو اختر بالأسهم ↑↓). " +
                       "Esc يلغي القطعة الحالية. الأصناف التي عليها باركود تظهر بالرمادي."
            };

            Controls.Add(grid);
            Controls.Add(side);
            Controls.Add(help);
            Controls.Add(top);

            txt.TextChanged += (a, b) => { if (pending != null) Filter(); };
            txt.KeyDown += OnKey;
            grid.KeyDown += OnKey;
            KeyDown += (a, b) =>
            {
                if (b.Control && b.KeyCode == Keys.Z) { b.Handled = b.SuppressKeyPress = true; DoUndo(); }
            };
            grid.CellFormatting += (a, e) =>
            {
                if (e.RowIndex < 0 || grid.Columns["Barcode"] == null) return;
                if (Convert.ToString(grid.Rows[e.RowIndex].Cells["Barcode"].Value) != "") e.CellStyle.ForeColor = Color.Gray;
            };
            Shown += (a, b) => txt.Focus();
            Activated += (a, b) => txt.Focus();

            LoadProducts();
            WaitForScan("");
        }

        void LoadProducts()
        {
            all = Db.Query("SELECT Id, Name, IFNULL(Category,'') AS Category, IFNULL(Barcode,'') AS Barcode, SellPrice FROM Products ORDER BY Category, Name");
            index = new SmartSearch(all.DefaultView.ToTable(false, "Id", "Name"));   // البحث بالاسم فقط؛ الباركود يُفحص من القاعدة
            UpdateCount();
        }

        void UpdateCount()
        {
            long left = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM Products WHERE IFNULL(Barcode,'')=''"));
            long total = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM Products"));
            lblCount.Text = "أصناف بلا باركود: " + left + " من " + total + "\nرُبط في هذه الجلسة: " + lstDone.Items.Count;
        }

        // ---------------- الحالتان: انتظار مسح، واختيار الصنف ----------------
        void WaitForScan(string message, bool ok = true)
        {
            pending = null;
            lblPrompt.Text = "① امسح باركود القطعة";
            lblPrompt.ForeColor = Color.FromArgb(31, 78, 120);
            txt.BackColor = Color.White;
            txt.Clear();
            ShowResult(message, ok);
            grid.DataSource = null;
            txt.Focus();
        }

        void ShowResult(string message, bool ok)
        {
            lblResult.Text = message;
            lblResult.ForeColor = ok ? Color.DarkGreen : Color.Firebrick;
        }

        /// <summary>باركود ممسوح: إن كان مسجلاً نعرض صنفه، وإلا ننتقل لاختيار الصنف.</summary>
        void Scanned(string code)
        {
            code = code.Trim();
            if (code == "") return;
            var t = Db.Query("SELECT Name FROM Products WHERE Barcode=@p0", code);
            if (t.Rows.Count > 0)
            {
                System.Media.SystemSounds.Asterisk.Play();
                WaitForScan("✓ الباركود " + code + " مسجّل مسبقاً على: " + t.Rows[0]["Name"]);
                return;
            }
            pending = code;
            lblPrompt.Text = "② الباركود " + code + " جديد: اكتب اسم الصنف ثم Enter";
            lblPrompt.ForeColor = Color.FromArgb(160, 80, 0);
            txt.BackColor = Color.FromArgb(255, 248, 225);
            txt.Clear();
            ShowResult("", true);
            Filter();
            txt.Focus();
        }

        void Filter()
        {
            string q = txt.Text.Trim();
            DataTable view = all.Clone();
            if (q != "" && !LooksLikeBarcode(q))
            {
                var byId = new Dictionary<int, DataRow>();
                foreach (DataRow r in all.Rows) byId[Convert.ToInt32(r["Id"])] = r;
                // الأصناف التي لا باركود لها تتقدّم قليلاً، فهي غالباً المقصودة
                var hits = index.Search(q, 100)
                    .Select(h => new { h.Id, Score = h.Score * (Convert.ToString(byId[h.Id]["Barcode"]) == "" ? 1.0 : 0.9) })
                    .OrderByDescending(h => h.Score);
                foreach (var h in hits) view.ImportRow(byId[h.Id]);
            }
            grid.DataSource = view;
            if (grid.Columns["Id"] != null) grid.Columns["Id"].Visible = false;
            var heads = new Dictionary<string, string> { { "Name", "الصنف" }, { "Category", "القسم" }, { "Barcode", "الباركود الحالي" }, { "SellPrice", "سعر البيع" } };
            foreach (var kv in heads) if (grid.Columns[kv.Key] != null) grid.Columns[kv.Key].HeaderText = kv.Value;
            if (grid.Columns["Name"] != null) grid.Columns["Name"].FillWeight = 260;
            if (grid.Columns["SellPrice"] != null) grid.Columns["SellPrice"].DefaultCellStyle.Format = "N0";
        }

        /// <summary>الماسح يكتب أرقاماً فقط بسرعة؛ 6 أرقام فأكثر بلا حروف = باركود.</summary>
        static bool LooksLikeBarcode(string s)
        {
            s = s.Trim();
            return s.Length >= 6 && s.All(char.IsDigit);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                string s = txt.Text.Trim();
                // في انتظار المسح، أو مسح قطعة جديدة أثناء البحث: نبدأ القطعة الجديدة
                if (pending == null || LooksLikeBarcode(s)) { Scanned(s); return; }
                LinkSelected();
            }
            else if (e.KeyCode == Keys.Escape && pending != null)
            {
                e.Handled = e.SuppressKeyPress = true;
                WaitForScan("أُلغيت القطعة (" + pending + ").", false);
            }
            else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && sender == txt && grid.Rows.Count > 0)
            {
                e.Handled = e.SuppressKeyPress = true;
                int i = grid.CurrentRow == null ? 0 : grid.CurrentRow.Index + (e.KeyCode == Keys.Down ? 1 : -1);
                i = Math.Max(0, Math.Min(grid.Rows.Count - 1, i));
                grid.CurrentCell = grid.Rows[i].Cells["Name"];
            }
        }

        void LinkSelected()
        {
            if (pending == null) return;
            if (grid.CurrentRow == null) { ShowResult("لا يوجد صنف مطابق. جرّب حروفاً أخرى من الاسم، أو Esc للتخطي.", false); txt.Focus(); return; }
            int id = Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);
            string name = Convert.ToString(grid.CurrentRow.Cells["Name"].Value);
            string old = Convert.ToString(Db.Scalar("SELECT IFNULL(Barcode,'') FROM Products WHERE Id=@p0", id));
            if (old != "" && !UI.Confirm(this, "الصنف \"" + name + "\" عليه باركود: " + old + "\nاستبداله بالباركود الجديد " + pending + "؟"))
            { txt.Focus(); return; }
            // قد يكون الباركود رُبط من نافذة أخرى منذ المسح
            var other = Db.Scalar("SELECT Name FROM Products WHERE Barcode=@p0 AND Id<>@p1", pending, id);
            if (other != null && !(other is DBNull)) { WaitForScan("الباركود " + pending + " صار مسجلاً على: " + other, false); return; }

            string code = pending;
            var ch = Db.TableChange.Begin("Products");
            Db.Exec("UPDATE Products SET Barcode=@p0 WHERE Id=@p1", code, id);
            Action undoIt = ch.End();
            undo.Push(name, () => { undoIt(); });
            foreach (DataRow r in all.Rows) if (Convert.ToInt32(r["Id"]) == id) r["Barcode"] = code;
            lstDone.Items.Insert(0, name + "  ←  " + code);
            System.Media.SystemSounds.Beep.Play();
            UpdateCount();
            WaitForScan("✓ رُبط " + code + " بالصنف: " + name);
        }

        void DoUndo()
        {
            string what = undo.Undo();
            if (what == null) { ShowResult("لا يوجد ما يمكن التراجع عنه.", false); txt.Focus(); return; }
            if (lstDone.Items.Count > 0) lstDone.Items.RemoveAt(0);
            LoadProducts();
            WaitForScan("تم التراجع عن ربط: " + what, false);
        }
    }
}
