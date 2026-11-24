using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ShopManager
{
    public static class UI
    {
        public static void Rtl(Form f)
        {
            f.RightToLeft = RightToLeft.Yes;
            f.RightToLeftLayout = true;
            f.Font = new Font("Segoe UI", 11);
            // زر التصغير: بدل الإخفاء الكامل تصبح النافذة نصف الشاشة على اليسار
            f.Resize += (a, b) =>
            {
                if (f.WindowState == FormWindowState.Minimized && f.MinimizeBox && f.IsHandleCreated)
                    f.BeginInvoke(new Action(() => SnapLeftHalf(f)));
            };
        }

        static void SnapLeftHalf(Form f)
        {
            if (f.IsDisposed) return;
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            f.WindowState = FormWindowState.Normal;
            f.StartPosition = FormStartPosition.Manual;
            f.Bounds = new Rectangle(wa.Left, wa.Top, wa.Width / 2, wa.Height);
        }

        public static void Warn(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }

        public static void Info(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, "معلومة", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }

        public static bool Confirm(IWin32Window owner, string msg)
        {
            return MessageBox.Show(owner, msg, "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == DialogResult.Yes;
        }

        public static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(8, 10, 4, 0) };
        }

        public static void SetupGrid(DataGridView g, bool readOnly)
        {
            g.Dock = DockStyle.Fill;
            g.ReadOnly = readOnly;
            g.AllowUserToAddRows = !readOnly;
            g.AllowUserToDeleteRows = false;
            g.RowHeadersVisible = false;
            g.BackgroundColor = Color.White;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.Font = new Font("Segoe UI", 12);
            // الارتفاعات تُحسب من حجم الخط ليتوافق مع تكبير الشاشة (DPI) فلا يقصّ النص
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = g.ColumnHeadersDefaultCellStyle.Font.Height + 30;
            g.RowTemplate.Height = g.Font.Height + 24;
            g.DefaultCellStyle.Padding = new Padding(4, 4, 4, 4);
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(180, 205, 235);
            g.DefaultCellStyle.SelectionForeColor = Color.Black;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 249, 253);
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            g.RightToLeft = RightToLeft.Yes;
            g.DataError += (a, b) => { b.ThrowException = false; };
        }

        /// <summary>عمود بالرقم دون خطأ إن لم يوجد (يعيد عموداً مؤقتاً فتمرّ الإعدادات بسلام).</summary>
        public static DataGridViewColumn Col(DataGridView g, int i)
        {
            return (i >= 0 && i < g.Columns.Count) ? g.Columns[i] : new DataGridViewTextBoxColumn();
        }

        public static string FormatSyp(double v) { return v.ToString("N0"); }

        /// <summary>يفتح صفحة غير حاصرة: يمكن تصغيرها والتنقل بينها وفتح صفحات أخرى.</summary>
        public static void ShowPage(Form f, Action onClosed = null)
        {
            if (onClosed != null) f.FormClosed += (a, b) => onClosed();
            f.Show();
        }
    }

    /// <summary>نافذة إدخال نص/رقم بسيطة.</summary>
    public static class Prompt
    {
        public static string Ask(string title, string label, string def = "")
        {
            using (var f = new Form())
            {
                UI.Rtl(f);
                f.Text = title;
                f.Width = 460; f.Height = 190;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false;
                var lbl = new Label { Text = label, Dock = DockStyle.Top, Height = 50, Padding = new Padding(10) };
                var txt = new TextBox { Text = def, Dock = DockStyle.Top, Font = new Font("Segoe UI", 14) };
                var ok = new Button { Text = "موافق", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 40 };
                f.Controls.Add(ok);
                f.Controls.Add(txt);
                f.Controls.Add(lbl);
                f.AcceptButton = ok;
                f.Shown += (a, b) => { txt.Focus(); txt.SelectAll(); };
                return f.ShowDialog() == DialogResult.OK ? txt.Text : null;
            }
        }
    }

    /// <summary>نافذة البحث عن صنف بالاسم أو الباركود.</summary>
    public class ProductPicker : Form
    {
        public int SelectedId = 0;
        readonly TextBox txt = new TextBox();
        readonly DataGridView grid = new DataGridView();

        public ProductPicker(string initial)
        {
            UI.Rtl(this);
            Text = "بحث عن صنف";
            Width = 800; Height = 540;
            StartPosition = FormStartPosition.CenterParent;

            txt.Dock = DockStyle.Top;
            txt.Font = new Font("Segoe UI", 15);
            txt.Text = initial ?? "";
            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;

            Controls.Add(grid);
            Controls.Add(txt);

            txt.TextChanged += (a, b) => Reload();
            txt.KeyDown += (a, b) =>
            {
                if (b.KeyCode == Keys.Down && grid.Rows.Count > 0) { grid.Focus(); }
                else if (b.KeyCode == Keys.Enter) { b.SuppressKeyPress = true; Choose(); }
            };
            grid.CellDoubleClick += (a, b) => Choose();
            grid.KeyDown += (a, b) =>
            {
                if (b.KeyCode == Keys.Enter) { b.SuppressKeyPress = true; Choose(); }
            };
            Shown += (a, b) => { txt.Focus(); txt.SelectionStart = txt.Text.Length; };
            Reload();
        }

        void Reload()
        {
            string q = "%" + txt.Text.Trim().Replace("%", "") + "%";
            grid.DataSource = Db.Query(
                "SELECT Id, Name AS \"الصنف\", Barcode AS \"الباركود\", SellPrice AS \"سعر البيع\", Stock AS \"المخزون\" " +
                "FROM Products WHERE Name LIKE @p0 OR Barcode LIKE @p0 ORDER BY Name LIMIT 200", q);
            if (grid.Columns["Id"] != null) grid.Columns["Id"].Visible = false;
        }

        void Choose()
        {
            if (grid.CurrentRow == null) return;
            SelectedId = Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public static class Csv
    {
        public static string[] ParseLine(string line)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else q = false;
                    }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '"') q = true;
                    else if (ch == ',') { res.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
            }
            res.Add(sb.ToString());
            return res.ToArray();
        }
    }


    /// <summary>مكدّس تراجع: كل إجراء يسجّل عملية عكسية، وزر "تراجع" ينفّذ آخرها.</summary>
    public class UndoStack
    {
        readonly System.Collections.Generic.Stack<System.Collections.Generic.KeyValuePair<string, Action>> st =
            new System.Collections.Generic.Stack<System.Collections.Generic.KeyValuePair<string, Action>>();

        public void Push(string desc, Action undo) { st.Push(new System.Collections.Generic.KeyValuePair<string, Action>(desc, undo)); }
        public void Clear() { st.Clear(); }
        public bool CanUndo { get { return st.Count > 0; } }
        public string PeekDesc() { return st.Count == 0 ? null : st.Peek().Key; }

        /// <summary>ينفّذ آخر تراجع. يعيد وصفه، أو null إن لم يوجد شيء.</summary>
        public string Undo()
        {
            if (st.Count == 0) return null;
            var e = st.Pop();
            e.Value();
            return e.Key;
        }

        /// <summary>زر جاهز للشريط العلوي.</summary>
        public static Button MakeButton(Action onClick)
        {
            var b = new Button { Text = "تراجع (Ctrl+Z)", AutoSize = true, Margin = new Padding(6, 8, 4, 0), BackColor = Color.FromArgb(255, 235, 200) };
            b.Click += (a, e) => onClick();
            return b;
        }
    }

    /// <summary>استعلامات مشتركة لأرصدة الزبائن والموردين (الموردون بعملتين: دولار وليرة). المرتجعات محسوبة.</summary>
    public static class Sql
    {
        /// <summary>رصيد الزبون = المتبقي من فواتيره − دفعاته − ما خُصم من دينه بالمرتجعات.</summary>
        public static string CustBal(string idExpr)
        {
            return "(IFNULL((SELECT OpeningBalance FROM Customers WHERE Id=" + idExpr + "),0)" +
                   " + IFNULL((SELECT SUM(Remaining) FROM Sales WHERE CustomerId=" + idExpr + "),0)" +
                   " - IFNULL((SELECT SUM(Amount) FROM Payments WHERE PartyType='C' AND PartyId=" + idExpr + "),0)" +
                   " - IFNULL((SELECT SUM(DebtReduction) FROM SaleReturns WHERE CustomerId=" + idExpr + "),0))";
        }

        /// <summary>تاريخ آخر حركة للزبون (فاتورة أو دفعة أو مرتجع).</summary>
        public static string CustLast(string idExpr)
        {
            return "MAX(IFNULL((SELECT MAX(substr(Date,1,10)) FROM Sales WHERE CustomerId=" + idExpr + "),'')," +
                   "IFNULL((SELECT MAX(substr(Date,1,10)) FROM Payments WHERE PartyType='C' AND PartyId=" + idExpr + "),'')," +
                   "IFNULL((SELECT MAX(substr(Date,1,10)) FROM SaleReturns WHERE CustomerId=" + idExpr + "),'')," +
                   "IFNULL((SELECT IFNULL(OpeningDate,'') FROM Customers WHERE Id=" + idExpr + "),''))";
        }

        const string CustomerBase =
            "SELECT c.Id AS Id, c.Name AS Name, IFNULL(c.Phone,'') AS Phone, IFNULL(c.Frozen,0) AS Frozen, " +
            "{BAL} AS Bal, {LAST} AS LastDate FROM Customers c";

        public static string CustomerBaseSql()
        {
            return CustomerBase.Replace("{BAL}", CustBal("c.Id")).Replace("{LAST}", CustLast("c.Id"));
        }

        /// <summary>frozenMode: -1 الكل، 0 الجارية، 1 المجمّدة. order: Name أو Bal DESC.</summary>
        public static string CustomerBalances(int frozenMode, string order)
        {
            string where = frozenMode < 0 ? "" : " WHERE Frozen=" + frozenMode;
            string lim = Db.DebtLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return "SELECT Id, Name AS \"الاسم\", Phone AS \"الهاتف\", Bal AS \"الرصيد (ل.س)\", LastDate AS \"آخر حركة\", " +
                   "CASE WHEN Frozen=1 THEN 'مجمد' ELSE '' END AS \"الحالة\", " +
                   "CASE WHEN Bal>" + lim + " THEN '⚠ فوق الحد' ELSE '' END AS \"تنبيه\" " +
                   "FROM (" + CustomerBaseSql() + ")" + where + " ORDER BY " + order;
        }

        /// <summary>ديون مضى عليها أكثر من N يوم بلا حركة وغير مجمّدة بعد. المعامل @p0 مثل "-20 day".</summary>
        public static string StaleDebts()
        {
            return "SELECT Id, Name, Bal, LastDate FROM (" + CustomerBaseSql() + ") " +
                   "WHERE Frozen=0 AND Bal>0 AND LastDate<>'' AND LastDate<=date('now','localtime',@p0)";
        }

        /// <summary>رصيد مورد بعملة محددة (مرتجعات المشتريات محسوبة).</summary>
        public static string SupBalExpr(string idExpr, string cur)
        {
            return "(IFNULL((SELECT SUM(Remaining) FROM Purchases WHERE SupplierId=" + idExpr + " AND IFNULL(Currency,'SYP')='" + cur + "'),0)" +
                   " - IFNULL((SELECT SUM(Amount) FROM Payments WHERE PartyType='S' AND PartyId=" + idExpr + " AND IFNULL(Currency,'SYP')='" + cur + "'),0)" +
                   " - IFNULL((SELECT SUM(DebtReduction) FROM PurchaseReturns WHERE SupplierId=" + idExpr + " AND IFNULL(Currency,'SYP')='" + cur + "'),0))";
        }

        public static string PartyBalances(bool customers)
        {
            if (customers) return CustomerBalances(-1, "Name");
            return "SELECT p.Id, p.Name AS \"الاسم\", IFNULL(p.Phone,'') AS \"الهاتف\", " +
                   SupBalExpr("p.Id", "USD") + " AS \"الرصيد ($)\", " + SupBalExpr("p.Id", "SYP") + " AS \"الرصيد (ل.س)\" " +
                   "FROM Suppliers p ORDER BY p.Name";
        }
    }

    /// <summary>توحيد الأسماء العربية والمقارنة التقريبية (لاقتراح الصنف المقابل لاسم المورد).</summary>
    public static class TextUtil
    {
        public static string Norm(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder();
            foreach (char ch in s.ToLowerInvariant())
            {
                char c = ch;
                if (c == '\u0623' || c == '\u0625' || c == '\u0622') c = '\u0627';        // أ إ آ → ا
                else if (c == '\u0629') c = '\u0647';                                       // ة → ه
                else if (c == '\u0649' || c == '\u0626') c = '\u064A';                     // ى ئ → ي
                else if (c == '\u0624') c = '\u0648';                                       // ؤ → و
                if (c >= '\u064B' && c <= '\u065F') continue;                               // التشكيل
                if (c == '\u0640') continue;                                                 // التطويل
                if (c >= '\u0660' && c <= '\u0669') c = (char)('0' + (c - '\u0660'));      // أرقام عربية
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            string t = Regex.Replace(sb.ToString(), @"(?<=\d)(?=\p{L})|(?<=\p{L})(?=\d)", " ");   // 700غ → 700 غ
            return Regex.Replace(t, @"\s+", " ").Trim();
        }

        /// <summary>كلمات الاسم بعد التوحيد (بلا تكرار، وبحذف "ال" من أولها).</summary>
        public static List<string> TokenList(string norm)
        {
            var list = new List<string>();
            foreach (string w in norm.Split(' '))
            {
                if (w == "") continue;
                string t = (w.StartsWith("\u0627\u0644") && w.Length > 3) ? w.Substring(2) : w;
                if (!list.Contains(t)) list.Add(t);
            }
            return list;
        }
    }

    /// <summary>فهرس أسماء الأصناف لاقتراح أقرب صنف لاسم المورد. الكلمة النادرة أثقل من الكلمة الشائعة (سائل، كيلو...).</summary>
    public class NameIndex
    {
        readonly List<int> ids = new List<int>();
        readonly List<string> names = new List<string>();
        readonly List<string> norms = new List<string>();
        readonly List<HashSet<string>> toks = new List<HashSet<string>>();
        readonly Dictionary<string, int> df = new Dictionary<string, int>();
        readonly int n;

        public NameIndex(DataTable products)
        {
            foreach (DataRow r in products.Rows)
            {
                string name = Convert.ToString(r["Name"]);
                string norm = TextUtil.Norm(name);
                var ts = new HashSet<string>(TextUtil.TokenList(norm));
                ids.Add(Convert.ToInt32(r["Id"]));
                names.Add(name);
                norms.Add(norm);
                toks.Add(ts);
                foreach (string t in ts) { int d; df.TryGetValue(t, out d); df[t] = d + 1; }
            }
            n = ids.Count;
        }

        double Idf(string t)
        {
            int d; df.TryGetValue(t, out d);
            return Math.Log(1.0 + n / (1.0 + d));
        }

        /// <summary>أفضل صنف مقترح ودرجته، ودرجة ثاني أفضل صنف.</summary>
        public void Match(string key, out int id, out string name, out double score, out double second)
        {
            id = 0; name = ""; score = 0; second = 0;
            string qn = TextUtil.Norm(key);
            var qt = new HashSet<string>();
            foreach (string t in TextUtil.TokenList(qn)) if (df.ContainsKey(t)) qt.Add(t);
            if (qt.Count == 0) return;
            for (int i = 0; i < ids.Count; i++)
            {
                double sc;
                if (norms[i] == qn) sc = 1;
                else
                {
                    double wi = 0, wu = 0;
                    foreach (string t in qt) if (toks[i].Contains(t)) wi += Idf(t);
                    if (wi <= 0) continue;
                    var all = new HashSet<string>(qt);
                    all.UnionWith(toks[i]);
                    foreach (string t in all) wu += Idf(t);
                    sc = wi / wu;
                    if (norms[i].Length > 0 && qn.Length > 0 && (norms[i].Contains(qn) || qn.Contains(norms[i]))) sc = Math.Max(sc, 0.6);
                }
                if (sc > score) { second = score; score = sc; id = ids[i]; name = names[i]; }
                else if (sc > second) second = sc;
            }
        }
    }

    /// <summary>اختيار قسم (مطلوب) لأصناف جديدة. يعيد اسم القسم أو null عند الإلغاء.</summary>
    public static class CategoryPrompt
    {
        public static string Ask(IWin32Window owner, string info)
        {
            using (var f = new Form())
            {
                UI.Rtl(f);
                f.Text = "اختيار القسم";
                f.Width = 540; f.Height = 280;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false;
                var lbl = new Label { Text = info, Dock = DockStyle.Top, Height = 90, Padding = new Padding(12) };
                var cmb = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown, Font = new Font("Segoe UI", 13) };
                foreach (DataRow r in Db.Query("SELECT DISTINCT Category FROM Products WHERE IFNULL(Category,'')<>'' ORDER BY Category").Rows)
                    cmb.Items.Add(Convert.ToString(r[0]));
                var ok = new Button { Text = "موافق", Dock = DockStyle.Bottom, Height = 44 };
                string result = null;
                ok.Click += (a, b) =>
                {
                    if (cmb.Text.Trim() == "") { UI.Warn(f, "اختر القسم أولاً."); return; }
                    result = cmb.Text.Trim();
                    f.DialogResult = DialogResult.OK;
                };
                f.Controls.Add(ok);
                f.Controls.Add(cmb);
                f.Controls.Add(lbl);
                f.AcceptButton = ok;
                return f.ShowDialog(owner) == DialogResult.OK ? result : null;
            }
        }
    }

    /// <summary>إضافة صنف جديد من فاتورة المشتريات.</summary>
    public static class NewProductDialog
    {
        /// <summary>يعيد معرّف الصنف الجديد أو 0 عند الإلغاء.</summary>
        public static int Ask(IWin32Window owner, string name, string barcode, string cur)
        {
            using (var f = new Form())
            {
                UI.Rtl(f);
                f.Text = "إضافة صنف جديد إلى القائمة";
                f.Width = 560; f.Height = 470;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false;

                var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16) };
                tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
                tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
                var txtName = new TextBox { Text = name };
                var txtBar = new TextBox { Text = barcode };
                var cmbCat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                foreach (DataRow r in Db.Query("SELECT DISTINCT Category FROM Products WHERE IFNULL(Category,'')<>'' ORDER BY Category").Rows)
                    cmbCat.Items.Add(Convert.ToString(r[0]));
                var txtMargin = new TextBox { Text = "0.15" };
                var txtSell = new TextBox();
                Action<string, Control> row = (label, c) =>
                {
                    tl.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 10) });
                    c.Dock = DockStyle.Fill; c.Margin = new Padding(3, 6, 3, 6);
                    tl.Controls.Add(c);
                };
                row("اسم الصنف", txtName);
                row("الباركود (اختياري)", txtBar);
                row("القسم (مطلوب)", cmbCat);
                row("نسبة الربح (0.15 = 15%)", txtMargin);
                row("أو سعر بيع ثابت (ل.س)", txtSell);
                var note = new Label
                {
                    Dock = DockStyle.Top, Height = 80, Padding = new Padding(16, 6, 16, 0),
                    Text = "اختر القسم ليُوضع الصنف مع أصنافه في القائمة. عملة الشراء: " + (cur == "USD" ? "دولار" : "ليرة") + ". إن كتبت سعر بيع ثابتاً تُهمَل نسبة الربح. وإلا يُحسب سعر البيع تلقائياً من سعر الشراء الذي تكتبه في الفاتورة."
                };
                var ok = new Button { Text = "إضافة الصنف", Dock = DockStyle.Bottom, Height = 44 };
                int newId = 0;
                ok.Click += (a, b) =>
                {
                    string n = txtName.Text.Trim();
                    if (n == "") { UI.Warn(f, "اسم الصنف مطلوب."); return; }
                    if (cmbCat.Text.Trim() == "") { UI.Warn(f, "اختر القسم أولاً ليوضع الصنف مع أصنافه (يمكنك كتابة قسم جديد)."); return; }
                    double margin = Db.Num(txtMargin.Text);
                    double sell = Db.Num(txtSell.Text);
                    if (sell > 0) margin = -1;
                    else if (margin < 0) margin = 0.15;
                    string bc = txtBar.Text.Trim();
                    try
                    {
                        Db.Exec("INSERT INTO Products(Name,Barcode,Category,CostPrice,CostCurrency,SellPrice,Stock,MinStock,Margin) VALUES(@p0,@p1,@p2,0,@p3,@p4,0,0,@p5)",
                            n, bc == "" ? null : bc, cmbCat.Text.Trim(), cur, sell, margin);
                        newId = Convert.ToInt32(Db.Scalar("SELECT Id FROM Products WHERE Name=@p0", n));
                        f.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { UI.Warn(f, "تعذّرت الإضافة (الاسم أو الباركود مكرر غالباً): " + ex.Message); }
                };
                f.Controls.Add(ok);
                f.Controls.Add(note);
                f.Controls.Add(tl);
                f.AcceptButton = ok;
                return f.ShowDialog(owner) == DialogResult.OK ? newId : 0;
            }
        }
    }

    /// <summary>نافذة دفعة: المبلغ + العملة (للموردين).</summary>
    public static class PayDialog
    {
        public static bool Ask(string title, string info, bool showCurrency, string defCur, double balUsd, double balSyp, out double amount, out string currency)
        {
            amount = 0; currency = "SYP";
            using (var f = new Form())
            {
                UI.Rtl(f);
                f.Text = title;
                f.Width = 480; f.Height = showCurrency ? 300 : 250;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false;
                var lbl = new Label { Text = info, Dock = DockStyle.Top, Height = 90, Padding = new Padding(10) };
                var txt = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 14) };
                var cmb = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 12), Visible = showCurrency };
                cmb.Items.AddRange(new object[] { "USD", "SYP" });
                cmb.SelectedItem = defCur;
                if (cmb.SelectedIndex < 0) cmb.SelectedIndex = 1;
                Action fill = () =>
                {
                    double b = cmb.Text == "USD" ? balUsd : balSyp;
                    txt.Text = b > 0 ? b.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
                    txt.SelectAll();
                };
                cmb.SelectedIndexChanged += (a, b2) => fill();
                var ok = new Button { Text = "موافق", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 42 };
                f.Controls.Add(ok);
                f.Controls.Add(cmb);
                f.Controls.Add(txt);
                f.Controls.Add(lbl);
                f.AcceptButton = ok;
                fill();
                f.Shown += (a, b2) => { txt.Focus(); txt.SelectAll(); };
                if (f.ShowDialog() != DialogResult.OK) return false;
                amount = Db.Num(txt.Text);
                currency = showCurrency ? cmb.Text : "SYP";
                return true;
            }
        }
    }

    public static class PayHelper
    {
        /// <summary>يسجّل دفعة من زبون أو إلى مورد. يعيد معرّف الدفعة أو 0 إن لم تُسجَّل.</summary>
        public static long Pay(IWin32Window owner, bool customer, int id, string name, double balUsd, double balSyp)
        {
            string defCur = (!customer && balUsd > 0) ? "USD" : "SYP";
            string info = "الدفعة " + (customer ? "من " : "إلى ") + name + "\nالرصيد الحالي: " +
                          (customer ? balSyp.ToString("N0") + " ل.س" : balUsd.ToString("N2") + " $   |   " + balSyp.ToString("N0") + " ل.س");
            double amount; string cur;
            if (!PayDialog.Ask(customer ? "دفعة من زبون" : "دفعة لمورد", info, !customer, defCur, balUsd, balSyp, out amount, out cur)) return 0;
            if (amount <= 0) { UI.Warn(owner, "المبلغ غير صحيح."); return 0; }
            double rate = cur == "USD" ? Db.Rate : 1;
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                Db.ExecTx(c, tx, "INSERT INTO Payments(Date,PartyType,PartyId,Amount,Note,Currency,Rate) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    customer ? "C" : "S", id, amount, "", cur, rate);
                long pid = Db.LastId(c, tx);
                tx.Commit();
                return pid;
            }
        }
    }
}
