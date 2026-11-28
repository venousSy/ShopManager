using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    public class ImportItem
    {
        public string Key;                 // الاسم أو الباركود كما كتبه المورد
        public double Qty, Cost;
        public int SugId;                  // أقرب صنف مقترح
        public string SugName = "";
        public double SugScore;
        public bool Auto;                  // اقتراح قوي يُختار افتراضياً
    }

    public class ImportDecision
    {
        public int Index;                  // موقع الصنف في قائمة المراجعة
        public string Action;              // skip | map | new
        public int ProductId;              // للربط
        public string NewName, Category;   // للجديد
    }

    /// <summary>مراجعة الأصناف غير المعروفة عند استيراد فاتورة مشتريات: تخطٍّ، أو ربط بصنف موجود (اسم المورد مختلف)، أو إضافة كجديد.</summary>
    public class ImportReviewForm : Form
    {
        const string ActSkip = "تخطي";
        const string ActMap = "ربط بصنف موجود";
        const string ActNew = "إضافة كصنف جديد";

        readonly List<ImportItem> items;
        readonly DataGridView grid = new DataGridView();
        readonly CheckBox chkRemember = new CheckBox();
        public List<ImportDecision> Decisions = new List<ImportDecision>();
        public bool Remember { get { return chkRemember.Checked; } }

        public ImportReviewForm(List<ImportItem> unknownItems)
        {
            items = unknownItems;
            UI.Rtl(this);
            Text = "مراجعة الأصناف غير الموجودة في القائمة";
            WindowState = FormWindowState.Maximized;

            UI.SetupGrid(grid, false);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;

            grid.Columns.Add("Key", "الاسم عند المورد"); grid.Columns["Key"].ReadOnly = true; grid.Columns["Key"].FillWeight = 120;
            grid.Columns.Add("Qty", "الكمية"); grid.Columns["Qty"].ReadOnly = true; grid.Columns["Qty"].FillWeight = 45;
            grid.Columns.Add("Cost", "السعر"); grid.Columns["Cost"].ReadOnly = true; grid.Columns["Cost"].FillWeight = 45;

            var act = new DataGridViewComboBoxColumn { Name = "Act", HeaderText = "ماذا تفعل؟", FlatStyle = FlatStyle.Flat, FillWeight = 100 };
            act.Items.AddRange(new object[] { ActSkip, ActMap, ActNew });
            grid.Columns.Add(act);

            grid.Columns.Add("MapName", "الصنف الموجود المقابل (للربط)"); grid.Columns["MapName"].ReadOnly = true; grid.Columns["MapName"].FillWeight = 130;
            var pick = new DataGridViewButtonColumn { Name = "Pick", HeaderText = "", Text = "اختيار صنف", UseColumnTextForButtonValue = true, FillWeight = 60 };
            grid.Columns.Add(pick);
            grid.Columns.Add("NewName", "اسم الصنف الجديد (للإضافة)"); grid.Columns["NewName"].FillWeight = 130;

            var cat = new DataGridViewComboBoxColumn { Name = "Cat", HeaderText = "القسم (للإضافة)", FlatStyle = FlatStyle.Flat, FillWeight = 90 };
            cat.Items.Add("");
            foreach (DataRow r in Db.Query("SELECT DISTINCT Category FROM Products WHERE IFNULL(Category,'')<>'' ORDER BY Category").Rows)
                cat.Items.Add(Convert.ToString(r[0]));
            grid.Columns.Add(cat);

            grid.Columns.Add("PId", "PId"); grid.Columns["PId"].Visible = false;

            foreach (var it in items)
            {
                bool good = it.SugId > 0 && it.Auto;
                bool isCode = it.Key.Length >= 6 && it.Key.Trim().Trim('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') == "";
                int idx = grid.Rows.Add(it.Key, it.Qty.ToString("0.##", CultureInfo.InvariantCulture), it.Cost >= 0 ? it.Cost.ToString("0.##", CultureInfo.InvariantCulture) : "",
                                        good ? ActMap : ActSkip,
                                        it.SugId > 0 ? it.SugName + (good ? "" : "  (اقتراح ضعيف)") : "",
                                        "اختيار صنف",
                                        isCode ? "صنف جديد " + it.Key : it.Key, "", it.SugId > 0 ? it.SugId : 0);
                if (good) grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 215);
            }

            grid.CellContentClick += OnCellClick;
            grid.EditingControlShowing += (a, b) =>
            {
                var cb = b.Control as ComboBox;
                if (cb != null && grid.CurrentCell != null && grid.Columns[grid.CurrentCell.ColumnIndex].Name == "Cat")
                    cb.DropDownStyle = ComboBoxStyle.DropDown;     // يسمح بكتابة قسم جديد
            };
            grid.CellValidating += (a, b) =>
            {
                if (grid.Columns[b.ColumnIndex].Name != "Cat") return;
                string t = Convert.ToString(b.FormattedValue).Trim();
                if (t != "" && !cat.Items.Contains(t)) cat.Items.Add(t);
            };

            var note = new Label
            {
                Dock = DockStyle.Top, Height = 74, Padding = new Padding(10, 8, 10, 0),
                Text = "هذه الأصناف في فاتورة المورد غير موجودة بنفس اسمها في قائمتك. لكل صنف اختر: تخطّي، أو ربطه بصنف موجود (إن كان المورد يسمّيه باسم مختلف)، " +
                       "أو إضافته كصنف جديد مع تحديد قسمه. الصفوف الصفراء لها اقتراح تلقائي بأقرب اسم. لا يُضاف شيء لا تختاره."
            };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            var ok = new Button { Text = "تطبيق", Height = 42, Width = 130, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(8, 4, 4, 0) };
            ok.Click += (a, b) => Apply();
            var skipAll = new Button { Text = "تخطي الكل", AutoSize = true, Margin = new Padding(6, 10, 4, 0) };
            skipAll.Click += (a, b) => { foreach (DataGridViewRow r in grid.Rows) r.Cells["Act"].Value = ActSkip; };
            var acceptSug = new Button { Text = "قبول كل الاقتراحات", AutoSize = true, Margin = new Padding(6, 10, 4, 0) };
            acceptSug.Click += (a, b) =>
            {
                foreach (DataGridViewRow r in grid.Rows)
                    if (Convert.ToInt32(r.Cells["PId"].Value) > 0) r.Cells["Act"].Value = ActMap;
            };
            var cancel = new Button { Text = "إلغاء (تجاهل الأصناف غير الموجودة)", AutoSize = true, Margin = new Padding(6, 10, 4, 0), DialogResult = DialogResult.Cancel };
            chkRemember.Text = "تذكّر الربط لأسماء المورد في الفواتير القادمة";
            chkRemember.Checked = true;
            chkRemember.AutoSize = true;
            chkRemember.Margin = new Padding(16, 14, 4, 0);
            bar.Controls.Add(ok);
            bar.Controls.Add(skipAll);
            bar.Controls.Add(acceptSug);
            bar.Controls.Add(cancel);
            bar.Controls.Add(chkRemember);
            CancelButton = cancel;

            Controls.Add(grid);
            Controls.Add(bar);
            Controls.Add(note);
        }

        void OnCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Pick") return;
            DataGridViewRow r = grid.Rows[e.RowIndex];
            string start = Convert.ToString(r.Cells["MapName"].Value);
            if (start == "") start = Convert.ToString(r.Cells["Key"].Value);
            using (var pk = new ProductPicker(start))
            {
                if (pk.ShowDialog(this) != DialogResult.OK) return;
                DataTable t = Db.Query("SELECT Id, Name FROM Products WHERE Id=@p0", pk.SelectedId);
                if (t.Rows.Count == 0) return;
                r.Cells["PId"].Value = Convert.ToInt32(t.Rows[0]["Id"]);
                r.Cells["MapName"].Value = Convert.ToString(t.Rows[0]["Name"]);
                r.Cells["Act"].Value = ActMap;
            }
        }

        void Apply()
        {
            grid.EndEdit();
            var result = new List<ImportDecision>();
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                DataGridViewRow r = grid.Rows[i];
                string act = Convert.ToString(r.Cells["Act"].Value);
                string key = Convert.ToString(r.Cells["Key"].Value);
                if (act == ActMap)
                {
                    int pid = Convert.ToInt32(r.Cells["PId"].Value);
                    if (pid <= 0) { UI.Warn(this, "اختر الصنف المقابل للصنف: " + key + " (زر اختيار صنف)."); return; }
                    result.Add(new ImportDecision { Index = i, Action = "map", ProductId = pid });
                }
                else if (act == ActNew)
                {
                    string name = Convert.ToString(r.Cells["NewName"].Value).Trim();
                    string cat = Convert.ToString(r.Cells["Cat"].Value).Trim();
                    if (name == "") { UI.Warn(this, "اكتب اسم الصنف الجديد للصف: " + key); return; }
                    object exists = Db.Scalar("SELECT Id FROM Products WHERE Name=@p0", name);
                    if (exists != null && !(exists is DBNull))
                    { UI.Warn(this, "الاسم \"" + name + "\" موجود في القائمة. اختر (ربط بصنف موجود) أو غيّر الاسم."); return; }
                    result.Add(new ImportDecision { Index = i, Action = "new", NewName = name, Category = cat == "" ? null : cat });   // القسم اختياري
                }
                else result.Add(new ImportDecision { Index = i, Action = "skip" });
            }
            Decisions = result;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>أسماء المورد المحفوظة (ربط اسم المورد بصنف في قائمتك): عرض وحذف.</summary>
    public class AliasesForm : Form
    {
        readonly DataGridView grid = new DataGridView();
        readonly UndoStack undo = new UndoStack();

        public AliasesForm()
        {
            UI.Rtl(this);
            Text = "أسماء المورد المحفوظة";
            Width = 900; Height = 640;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z) { b.Handled = true; DoUndo(); } };

            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            var del = new Button { Text = "حذف الربط المحدد", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            del.Click += (a, b) => DeleteSelected();
            bar.Controls.Add(del);
            bar.Controls.Add(UndoStack.MakeButton(DoUndo));
            var note = new Label { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8, 10, 8, 0), Text = "هذه الأسماء تُطبَّق تلقائياً عند استيراد فواتير المشتريات القادمة، فلا تُسأل عنها مرة ثانية." };

            Controls.Add(grid);
            Controls.Add(note);
            Controls.Add(bar);
            Reload();
        }

        void Reload()
        {
            grid.DataSource = Db.Query(
                "SELECT a.Id, a.Alias AS \"الاسم عند المورد\", IFNULL(p.Name,'(صنف محذوف)') AS \"الصنف في قائمتي\" " +
                "FROM ProductAliases a LEFT JOIN Products p ON p.Id=a.ProductId ORDER BY a.Alias");
            if (grid.Columns["Id"] != null) grid.Columns["Id"].Visible = false;
        }

        void DoUndo()
        {
            string what = undo.Undo();
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            Reload();
        }

        void DeleteSelected()
        {
            if (grid.CurrentRow == null) { UI.Warn(this, "اختر سطراً أولاً."); return; }
            int id = Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);
            var ch = Db.TableChange.Begin("ProductAliases");
            Db.Exec("DELETE FROM ProductAliases WHERE Id=@p0", id);
            undo.Push("حذف ربط", ch.End());
            Reload();
        }
    }
}
