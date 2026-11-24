using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>فاتورة بيع (isSale=true) أو فاتورة مشتريات (isSale=false).</summary>
    public class InvoiceForm : Form
    {
        class Line { public int Id; public string Name; public double Qty, Price, Prev; }
        class Draft { public List<object[]> Rows = new List<object[]>(); public string Party, Discount, Paid, InvNo; }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly bool sale;
        readonly TextBox txtBarcode = new TextBox();
        readonly ComboBox cmbParty = new ComboBox();
        readonly ComboBox cmbCur = new ComboBox();
        readonly TextBox txtRate = new TextBox();
        readonly TextBox txtInvNo = new TextBox();
        readonly DataGridView grid = new DataGridView();
        readonly TextBox txtDiscount = new TextBox();
        readonly TextBox txtPaid = new TextBox();
        readonly Label lblSub = new Label();
        readonly Label lblNet = new Label();
        readonly Label lblRemain = new Label();
        readonly Label lblStatus = new Label();
        bool busy;
        int lastId;
        double lastRemaining;
        double lastExtra;          // ما دفعه الزبون زيادة على الفاتورة وسُجّل دفعةً من دينه السابق
        long lastExtraPayId;
        readonly int editId;
        int lastSavedId;
        bool closeOnShow;
        double cachedPrev, editPrevBal;
        string editPartyName = "";
        readonly Label lblPrev = new Label();
        readonly Label lblDue = new Label();
        int lastChanged;
        readonly UndoStack undo = new UndoStack();

        public InvoiceForm(bool isSale, int editInvoiceId = 0)
        {
            sale = isSale;
            editId = editInvoiceId;
            UI.Rtl(this);
            Text = sale ? "فاتورة بيع" : "فاتورة مشتريات";
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            BuildUi();
            LoadParties();
            if (editId > 0) LoadForEdit();
            KeyDown += OnFormKeyDown;
            Shown += (a, b) => { if (closeOnShow) { Close(); return; } txtBarcode.Focus(); };
        }

        // ---------------- الواجهة ----------------
        void BuildUi()
        {
            UI.SetupGrid(grid, false);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = true;
            grid.Font = new Font("Segoe UI", 13);
            grid.RowTemplate.Height = grid.Font.Height + 26;
            grid.Columns.Add("Id", "Id"); grid.Columns["Id"].Visible = false;
            grid.Columns.Add("Name", "الصنف"); grid.Columns["Name"].ReadOnly = true; grid.Columns["Name"].FillWeight = 45;
            grid.Columns.Add("Qty", "الكمية"); grid.Columns["Qty"].FillWeight = 12;
            grid.Columns.Add("Price", sale ? "سعر البيع (ل.س)" : "سعر الشراء"); grid.Columns["Price"].FillWeight = 18;
            grid.Columns.Add("Total", "المجموع"); grid.Columns["Total"].ReadOnly = true; grid.Columns["Total"].FillWeight = 18;
            grid.Columns.Add("Stock", "المخزون الحالي"); grid.Columns["Stock"].ReadOnly = true; grid.Columns["Stock"].FillWeight = 12;
            grid.Columns.Add("Prev", "سعر الشراء السابق"); grid.Columns["Prev"].ReadOnly = true; grid.Columns["Prev"].FillWeight = 16; grid.Columns["Prev"].Visible = !sale;
            foreach (var n in new[] { "Qty", "Price", "Total", "Stock", "Prev" })
                grid.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.CellValueChanged += (a, b) => Recalc();
            grid.RowsRemoved += (a, b) => Recalc();
            grid.CellEndEdit += (a, b) => txtBarcode.Focus();
            grid.CellBeginEdit += (a, b) => PushDraft("تعديل سطر");
            grid.UserDeletingRow += (a, b) => PushDraft("حذف سطر");

            // الشريط العلوي
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            txtBarcode.Width = 300;
            txtBarcode.Font = new Font("Segoe UI", 15);
            txtBarcode.PlaceholderText = "امسح الباركود أو اكتب اسم الصنف ثم Enter";
            txtBarcode.KeyDown += (a, b) =>
            {
                if (b.KeyCode == Keys.Enter) { b.SuppressKeyPress = true; ScanEnter(); }
            };
            top.Controls.Add(UI.Lbl("الباركود:"));
            top.Controls.Add(txtBarcode);
            var btnFind = new Button { Text = "بحث (F3)", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
            btnFind.Click += (a, b) => OpenPicker(txtBarcode.Text);
            top.Controls.Add(btnFind);

            top.Controls.Add(UI.Lbl(sale ? "الزبون:" : "المورد:"));
            cmbParty.Width = 220;
            cmbParty.Margin = new Padding(4, 8, 4, 0);
            cmbParty.DropDownStyle = ComboBoxStyle.DropDown;
            cmbParty.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cmbParty.AutoCompleteSource = AutoCompleteSource.ListItems;
            top.Controls.Add(cmbParty);
            if (sale)
            {
                Big(lblPrev, Color.DarkRed);
                lblPrev.Width = 140;
                lblPrev.Margin = new Padding(4, 6, 8, 0);
                top.Controls.Add(UI.Lbl("الرصيد السابق للزبون:"));
                top.Controls.Add(lblPrev);
                cmbParty.TextChanged += (a, b) => UpdatePrev();
            }

            if (!sale)
            {
                cmbCur.Items.AddRange(new object[] { "USD", "SYP" });
                cmbCur.DropDownStyle = ComboBoxStyle.DropDownList;
                cmbCur.Width = 70;
                cmbCur.Margin = new Padding(4, 8, 4, 0);
                txtRate.Width = 70;
                txtRate.Margin = new Padding(4, 8, 4, 0);
                txtRate.Text = Db.Rate.ToString("0.##", Inv);
                cmbCur.SelectedIndexChanged += (a, b) =>
                {
                    bool usd = cmbCur.Text == "USD";
                    txtRate.Enabled = usd;
                    txtRate.Text = usd ? Db.Rate.ToString("0.##", Inv) : "1";
                };
                cmbCur.SelectedIndex = 0;
                txtInvNo.Width = 110;
                txtInvNo.Margin = new Padding(4, 8, 4, 0);
                var btnNewProd = new Button { Text = "صنف جديد", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
                btnNewProd.Click += (a, b) => { CreateNewProduct("", ""); txtBarcode.Focus(); };
                top.Controls.Add(btnNewProd);
                var btnImport = new Button { Text = "استيراد فاتورة (CSV)", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
                btnImport.Click += (a, b) => { ImportPurchaseCsv(); txtBarcode.Focus(); };
                top.Controls.Add(btnImport);
                top.Controls.Add(UI.Lbl("العملة:")); top.Controls.Add(cmbCur);
                top.Controls.Add(UI.Lbl("سعر الصرف:")); top.Controls.Add(txtRate);
                top.Controls.Add(UI.Lbl("رقم فاتورة المورد:")); top.Controls.Add(txtInvNo);
            }

            // الشريط السفلي
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            Big(lblSub, Color.Black);
            Big(lblNet, Color.Navy);
            Big(lblRemain, Color.DarkGreen);
            lblNet.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            lblNet.Width = 170; lblNet.Height = 44;
            txtDiscount.Width = 100; txtDiscount.Font = new Font("Segoe UI", 14); txtDiscount.PlaceholderText = "0";
            txtPaid.Width = 130; txtPaid.Font = new Font("Segoe UI", 14); txtPaid.PlaceholderText = "(كامل)";
            txtDiscount.TextChanged += (a, b) => Recalc();
            txtPaid.TextChanged += (a, b) => Recalc();
            bottom.Controls.Add(UI.Lbl("المجموع:")); bottom.Controls.Add(lblSub);
            bottom.Controls.Add(UI.Lbl("الخصم:")); bottom.Controls.Add(txtDiscount);
            bottom.Controls.Add(UI.Lbl("الصافي:")); bottom.Controls.Add(lblNet);
            bottom.Controls.Add(UI.Lbl(sale ? "المدفوع:" : "المدفوع للمورد:")); bottom.Controls.Add(txtPaid);
            bottom.Controls.Add(UI.Lbl(sale ? "المتبقي (دين):" : "المتبقي للمورد:")); bottom.Controls.Add(lblRemain);
            if (sale)
            {
                Big(lblDue, Color.DarkRed);
                lblDue.Width = 150;
                bottom.Controls.Add(UI.Lbl("إجمالي المستحق (السابق + المتبقي):"));
                bottom.Controls.Add(lblDue);
            }

            var btnSave = new Button { Text = "حفظ (F2)", Height = 44, Width = 120, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(12, 2, 4, 0) };
            btnSave.Click += (a, b) => DoSave(false);
            bottom.Controls.Add(btnSave);
            if (sale)
            {
                var btnPrint = new Button { Text = "حفظ وطباعة (F9)", Height = 44, Width = 170, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(4, 2, 4, 0) };
                btnPrint.Click += (a, b) => DoSave(true);
                bottom.Controls.Add(btnPrint);
            }
            var btnDel = new Button { Text = "حذف السطر (Del)", Height = 44, Width = 150, Font = new Font("Segoe UI", 12), Margin = new Padding(4, 2, 4, 0) };
            btnDel.Click += (a, b) => DeleteLine();
            bottom.Controls.Add(btnDel);
            var btnUndo = new Button { Text = "تراجع (Ctrl+Z)", Height = 44, Width = 150, Font = new Font("Segoe UI", 12), BackColor = Color.FromArgb(255, 235, 200), Margin = new Padding(4, 2, 4, 0) };
            btnUndo.Click += (a, b) => DoUndo();
            bottom.Controls.Add(btnUndo);
            var btnReopen = new Button { Text = "إعادة فتح الفاتورة الأخيرة", Height = 44, Width = 240, Font = new Font("Segoe UI", 12), BackColor = Color.FromArgb(220, 240, 255), Margin = new Padding(4, 2, 4, 0), Visible = editId == 0 };
            btnReopen.Click += (a, b) => ReopenLast();
            bottom.Controls.Add(btnReopen);
            var btnNew = new Button { Text = "جديد (F5)", Height = 44, Width = 110, Font = new Font("Segoe UI", 12), Margin = new Padding(4, 2, 4, 0) };
            btnNew.Click += (a, b) => ClearForm();
            bottom.Controls.Add(btnNew);

            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 28;
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            lblStatus.Padding = new Padding(8, 0, 8, 0);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(lblStatus);
            Controls.Add(top);
        }

        static void Big(Label l, Color c)
        {
            l.AutoSize = false;
            l.Width = 120; l.Height = 36;
            l.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            l.ForeColor = c;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.BorderStyle = BorderStyle.FixedSingle;
            l.BackColor = Color.White;
            l.Margin = new Padding(4, 4, 8, 0);
            l.Text = "0";
        }

        void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2) { e.Handled = true; DoSave(false); }
            else if (e.KeyCode == Keys.F9 && sale) { e.Handled = true; DoSave(true); }
            else if (e.KeyCode == Keys.F3) { e.Handled = true; OpenPicker(txtBarcode.Text); }
            else if (e.KeyCode == Keys.F5) { e.Handled = true; ClearForm(); }
            else if (e.Control && e.KeyCode == Keys.Z && !grid.IsCurrentCellInEditMode && (ActiveControl != txtBarcode || txtBarcode.Text == "") && !(ActiveControl is ComboBox))
            { e.Handled = true; e.SuppressKeyPress = true; DoUndo(); }
        }

        void LoadParties()
        {
            string current = cmbParty.Text;
            cmbParty.Items.Clear();
            var t = Db.Query("SELECT Name FROM " + (sale ? "Customers" : "Suppliers") + " ORDER BY Name");
            foreach (DataRow r in t.Rows) cmbParty.Items.Add(Convert.ToString(r[0]));
            cmbParty.Text = current;
        }

        // ---------------- الأصناف ----------------
        void ScanEnter()
        {
            string code = txtBarcode.Text.Trim();
            txtBarcode.Clear();
            if (code == "") return;

            var t = Db.Query("SELECT * FROM Products WHERE Barcode=@p0", code);
            if (t.Rows.Count == 1) { Status("", false); AddProduct(t.Rows[0]); return; }

            bool numeric = code.Length >= 6 && code.Trim().Trim('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') == "";
            if (numeric)
            {
                if (!sale && OfferNewProduct(code, "")) return;
                Status("الباركود غير مسجّل: " + code + (sale ? "  (أضفه من شاشة الأصناف)" : ""), true);
                return;
            }

            // بحث ذكي بالاسم. يُضاف الصنف مباشرة فقط إن كان تطابقاً واضحاً وحيداً
            // (كل الكلمات موجودة بلا أخطاء إملائية)، وإلا تُفتح قائمة مرتبة ليختار المستخدم.
            var hits = SmartSearch.FromDb().Search(code);
            var clear = SmartSearch.Clear(hits);
            if (clear != null)
            {
                t = Db.Query("SELECT * FROM Products WHERE Id=@p0", clear.Id);
                if (t.Rows.Count == 1) { Status("", false); AddProduct(t.Rows[0]); return; }
            }
            if (!sale && hits.Count == 0 && OfferNewProduct("", code)) return;
            OpenPicker(code);
        }

        void OpenPicker(string initial)
        {
            using (var pk = new ProductPicker(initial))
            {
                if (pk.ShowDialog(this) == DialogResult.OK)
                {
                    var t = Db.Query("SELECT * FROM Products WHERE Id=@p0", pk.SelectedId);
                    if (t.Rows.Count > 0) AddProduct(t.Rows[0]);
                }
            }
            txtBarcode.Clear();
            txtBarcode.Focus();
        }

        double PurchaseCost(DataRow p)
        {
            double cost = Db.D(p["CostPrice"]);
            string pc = Convert.ToString(p["CostCurrency"]);
            string cur = cmbCur.Text;
            double rate = Db.Num(txtRate.Text);
            if (pc == cur) return cost;
            if (pc == "USD" && cur == "SYP") return cost * Db.Rate;
            if (pc == "SYP" && cur == "USD") return rate > 0 ? cost / rate : 0;
            return cost;
        }

        void AddProduct(DataRow p, double qty = 1, double cost = -1, bool record = true)
        {
            int id = Convert.ToInt32(p["Id"]);
            if (record) PushDraft("إضافة صنف");
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (Convert.ToInt32(r.Cells["Id"].Value) == id)
                {
                    double q = Db.Num(Convert.ToString(r.Cells["Qty"].Value));
                    r.Cells["Qty"].Value = (q + qty).ToString("0.##", Inv);
                    if (cost >= 0) r.Cells["Price"].Value = cost.ToString("0.##", Inv);
                    ScrollTo(r.Index);
                    return;
                }
            }
            double defPrice = sale ? Db.D(p["SellPrice"]) : PurchaseCost(p);
            double price = (cost >= 0) ? cost : defPrice;
            int idx = grid.Rows.Add(id, Convert.ToString(p["Name"]), qty.ToString("0.##", Inv), price.ToString("0.##", Inv), "",
                                    Db.D(p["Stock"]).ToString("0.##", Inv), sale ? "" : defPrice.ToString("0.##", Inv));
            ScrollTo(idx);
            Recalc();
        }

        // ---------------- صنف جديد من فاتورة المشتريات ----------------
        bool OfferNewProduct(string barcode, string name)
        {
            string what = barcode != "" ? "الباركود " + barcode : "الصنف \"" + name + "\"";
            if (!UI.Confirm(this, what + " غير موجود في قائمة الأصناف.\nهل تريد إضافته إلى القائمة الآن؟")) return false;
            return CreateNewProduct(barcode, name);
        }

        bool CreateNewProduct(string barcode, string name)
        {
            int newId = NewProductDialog.Ask(this, name, barcode, cmbCur.Text);
            if (newId == 0) return false;
            DataTable t = Db.Query("SELECT * FROM Products WHERE Id=@p0", newId);
            if (t.Rows.Count == 0) return false;
            Status("تمت إضافة الصنف \"" + Convert.ToString(t.Rows[0]["Name"]) + "\" إلى القائمة. اكتب كميته وسعر شرائه.", false);
            AddProduct(t.Rows[0]);
            return true;
        }

        // ---------------- استيراد فاتورة مشتريات من CSV ----------------
        static bool LooksNumeric(string s)
        {
            return s.Trim() != "" && Regex.IsMatch(s.Trim(), @"^[0-9\.,\u0660-\u0669\u066B\u066C]+$");
        }

        void ImportPurchaseCsv()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "CSV|*.csv";
                dlg.Title = "اختر ملف فاتورة المشتريات (الباركود أو الاسم، الكمية، سعر الشراء)";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                PushDraft("استيراد فاتورة");

                var unknown = new List<string[]>();
                int added = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    string[] f = Csv.ParseLine(lines[i]);
                    if (f.Length < 2) continue;
                    if (i == 0 && !LooksNumeric(f[1])) continue;               // سطر العناوين
                    string key = f[0].Trim();
                    double qty = Db.Num(f[1]);
                    double cost = f.Length > 2 && f[2].Trim() != "" ? Db.Num(f[2]) : -1;
                    if (key == "" || qty <= 0) continue;
                    DataTable t = Db.Query("SELECT * FROM Products WHERE Barcode=@p0 OR Name=@p0 LIMIT 1", key);
                    if (t.Rows.Count == 0)   // اسم محفوظ سابقاً لهذا المورد
                        t = Db.Query("SELECT p.* FROM ProductAliases a JOIN Products p ON p.Id=a.ProductId WHERE a.Norm=@p0 LIMIT 1", TextUtil.Norm(key));
                    if (t.Rows.Count > 0) { AddProduct(t.Rows[0], qty, cost, false); added++; }
                    else unknown.Add(new[] { key, qty.ToString(Inv), cost.ToString(Inv) });
                }

                int mapped = 0, created = 0, skipped = 0;
                if (unknown.Count > 0)
                {
                    var index = new NameIndex(Db.Query("SELECT Id, Name FROM Products"));
                    var review = new List<ImportItem>();
                    foreach (var u in unknown)
                    {
                        var it = new ImportItem { Key = u[0], Qty = Db.Num(u[1]), Cost = Db.Num(u[2]) };
                        if (u[2] == "-1") it.Cost = -1;
                        int sid; string sname; double sscore, ssecond;
                        index.Match(u[0], out sid, out sname, out sscore, out ssecond);
                        if (sscore >= 0.25)
                        {
                            it.SugId = sid; it.SugName = sname; it.SugScore = sscore;
                            it.Auto = sscore >= 0.45 || (sscore >= 0.35 && sscore >= ssecond * 1.25);   // يُقترح تلقائياً فقط إن كان واضحاً
                        }
                        review.Add(it);
                    }
                    using (var rf = new ImportReviewForm(review))
                    {
                        if (rf.ShowDialog(this) == DialogResult.OK)
                        {
                            foreach (var d in rf.Decisions)
                            {
                                ImportItem it = review[d.Index];
                                if (d.Action == "map")
                                {
                                    DataTable t = Db.Query("SELECT * FROM Products WHERE Id=@p0", d.ProductId);
                                    if (t.Rows.Count == 0) { skipped++; continue; }
                                    AddProduct(t.Rows[0], it.Qty, it.Cost, false);
                                    mapped++;
                                    if (rf.Remember)
                                        Db.Exec("INSERT OR REPLACE INTO ProductAliases(Alias,Norm,ProductId) VALUES(@p0,@p1,@p2)", it.Key, TextUtil.Norm(it.Key), d.ProductId);
                                }
                                else if (d.Action == "new")
                                {
                                    bool isCode = it.Key.Length >= 6 && LooksNumeric(it.Key);
                                    try
                                    {
                                        Db.Exec("INSERT INTO Products(Name,Barcode,Category,CostPrice,CostCurrency,SellPrice,Stock,MinStock,Margin) VALUES(@p0,@p1,@p2,0,@p3,0,0,0,0.15)",
                                            d.NewName, isCode ? it.Key : null, d.Category, cmbCur.Text);
                                        DataTable t = Db.Query("SELECT * FROM Products WHERE Name=@p0", d.NewName);
                                        if (t.Rows.Count > 0) { AddProduct(t.Rows[0], it.Qty, it.Cost, false); created++; }
                                        if (rf.Remember && !isCode)
                                            Db.Exec("INSERT OR REPLACE INTO ProductAliases(Alias,Norm,ProductId) VALUES(@p0,@p1,@p2)", it.Key, TextUtil.Norm(it.Key), Convert.ToInt32(t.Rows[0]["Id"]));
                                    }
                                    catch { skipped++; }
                                }
                                else skipped++;
                            }
                        }
                        else skipped = unknown.Count;
                    }
                }
                Status("تم استيراد " + added + " صنفاً مطابقاً" + (mapped > 0 ? "، وربط " + mapped + " صنف بأصناف موجودة" : "") +
                       (created > 0 ? "، وأُضيف " + created + " صنف جديد" : "") + (skipped > 0 ? "، وتُخطّي " + skipped : "") + ".", false);
                Recalc();
            }
        }

        // ---------------- الرصيد السابق للزبون ----------------
        void UpdatePrev()
        {
            if (!sale) return;
            string name = cmbParty.Text.Trim();
            double v = 0;
            if (editId > 0 && name == editPartyName) v = editPrevBal;
            else if (name != "")
            {
                object o = Db.Scalar("SELECT Id FROM Customers WHERE Name=@p0", name);
                if (o != null && !(o is DBNull)) v = Db.CustomerBalance(Convert.ToInt32(o));
            }
            cachedPrev = v;
            Recalc();
        }

        // ---------------- تعديل فاتورة محفوظة ----------------
        void LoadForEdit()
        {
            DataTable h = Db.Query(sale
                ? "SELECT s.Id, IFNULL(c.Name,'') AS PName, s.Discount, s.Paid, IFNULL(s.PrevBalance,0) AS PrevBal FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId WHERE s.Id=@p0"
                : "SELECT p.Id, IFNULL(s.Name,'') AS PName, p.Discount, p.Paid, IFNULL(p.Currency,'SYP') AS Cur, IFNULL(p.Rate,1) AS Rate, IFNULL(p.InvoiceNo,'') AS InvNo FROM Purchases p LEFT JOIN Suppliers s ON s.Id=p.SupplierId WHERE p.Id=@p0", editId);
            if (h.Rows.Count == 0) { UI.Warn(this, "الفاتورة غير موجودة."); closeOnShow = true; return; }
            long retCount = Convert.ToInt64(Db.Scalar(sale ? "SELECT COUNT(*) FROM SaleReturns WHERE SaleId=@p0" : "SELECT COUNT(*) FROM PurchaseReturns WHERE PurchaseId=@p0", editId));
            if (retCount > 0)
            {
                UI.Warn(this, "لا يمكن تعديل هذه الفاتورة لأن عليها مرتجعات. ألغِ المرتجع من صفحة المرتجعات أولاً ثم عدّلها.");
                closeOnShow = true;
                return;
            }
            DataRow hr = h.Rows[0];
            Text = (sale ? "تعديل فاتورة بيع رقم " : "تعديل فاتورة مشتريات رقم ") + editId;
            editPartyName = Convert.ToString(hr["PName"]);
            if (sale) editPrevBal = Db.D(hr["PrevBal"]);

            DataTable it = Db.Query(sale
                ? "SELECT si.ProductId, si.Name, si.Qty, si.UnitPrice AS Price, IFNULL(p.Stock,0) AS Stock FROM SaleItems si LEFT JOIN Products p ON p.Id=si.ProductId WHERE si.SaleId=@p0 ORDER BY si.Id"
                : "SELECT pi.ProductId, IFNULL(pr.Name,'(صنف محذوف)') AS Name, pi.Qty, pi.UnitCost AS Price, IFNULL(pr.Stock,0) AS Stock FROM PurchaseItems pi LEFT JOIN Products pr ON pr.Id=pi.ProductId WHERE pi.PurchaseId=@p0 ORDER BY pi.Id", editId);

            busy = true;
            try
            {
                grid.Rows.Clear();
                foreach (DataRow r in it.Rows)
                {
                    double q = Db.D(r["Qty"]), price = Db.D(r["Price"]), stock = Db.D(r["Stock"]);
                    double shownStock = sale ? stock + q : stock;          // المخزون المتاح لو أُلغيت هذه الفاتورة
                    grid.Rows.Add(r["ProductId"], Convert.ToString(r["Name"]), q.ToString("0.##", Inv), price.ToString("0.##", Inv), "",
                                  shownStock.ToString("0.##", Inv), sale ? "" : price.ToString("0.##", Inv));
                }
                if (!sale)
                {
                    cmbCur.SelectedItem = Convert.ToString(hr["Cur"]);
                    txtRate.Text = Db.D(hr["Rate"]).ToString("0.##", Inv);
                    txtInvNo.Text = Convert.ToString(hr["InvNo"]);
                }
                cmbParty.Text = editPartyName;
                double disc = Db.D(hr["Discount"]);
                txtDiscount.Text = disc > 0 ? disc.ToString("0.##", Inv) : "";
                txtPaid.Text = Db.D(hr["Paid"]).ToString("0.##", Inv);
            }
            finally { busy = false; }
            UpdatePrev();
            Recalc();
            Status("وضع التعديل: عدّل الأصناف أو المبالغ ثم احفظ لتحديث الفاتورة رقم " + editId + " (لا تُنشأ فاتورة جديدة).", false);
        }

        void ReopenLast()
        {
            if (lastSavedId <= 0)
            {
                Status("لا توجد فاتورة محفوظة في هذه الشاشة بعد. للفواتير الأقدم: سجل الفواتير ← فتح الفاتورة للتعديل.", true);
                txtBarcode.Focus();
                return;
            }
            UI.ShowPage(new InvoiceForm(sale, lastSavedId));
        }

        // ---------------- التراجع ----------------
        Draft TakeDraft()
        {
            var d = new Draft { Party = cmbParty.Text, Discount = txtDiscount.Text, Paid = txtPaid.Text, InvNo = txtInvNo.Text };
            foreach (DataGridViewRow r in grid.Rows)
                d.Rows.Add(new object[] { r.Cells["Id"].Value, r.Cells["Name"].Value, r.Cells["Qty"].Value, r.Cells["Price"].Value, r.Cells["Total"].Value, r.Cells["Stock"].Value, r.Cells["Prev"].Value });
            return d;
        }

        void RestoreDraft(Draft d)
        {
            busy = true;
            try
            {
                grid.Rows.Clear();
                foreach (var v in d.Rows) grid.Rows.Add(v);
                txtDiscount.Text = d.Discount;
                txtPaid.Text = d.Paid;
                cmbParty.Text = d.Party;
                if (!sale) txtInvNo.Text = d.InvNo;
            }
            finally { busy = false; }
            Recalc();
        }

        void PushDraft(string what)
        {
            if (busy) return;
            Draft d = TakeDraft();
            undo.Push(what, () => RestoreDraft(d));
        }

        void DoUndo()
        {
            grid.EndEdit();
            string next = undo.PeekDesc();
            if (next == null) { Status("لا يوجد ما يمكن التراجع عنه.", true); txtBarcode.Focus(); return; }
            if (next.StartsWith("حفظ الفاتورة") &&
                !UI.Confirm(this, "التراجع سيلغي الفاتورة المحفوظة (" + next + ") ويعيد المخزون كما كان، ثم يعيد أصنافها إلى الشاشة لتعدّلها. متابعة؟"))
            { txtBarcode.Focus(); return; }
            try
            {
                undo.Undo();
                if (next.StartsWith("حفظ الفاتورة")) LoadParties();
                Status("تم التراجع عن: " + next, false);
            }
            catch (Exception ex) { UI.Warn(this, "تعذّر التراجع: " + ex.Message); }
            txtBarcode.Focus();
        }

        void DeleteLine()
        {
            if (grid.CurrentRow == null) { Status("اختر السطر المراد حذفه أولاً.", true); return; }
            PushDraft("حذف سطر");
            grid.Rows.Remove(grid.CurrentRow);
            Recalc();
            txtBarcode.Focus();
        }

        void ScrollTo(int i)
        {
            try { grid.FirstDisplayedScrollingRowIndex = i; } catch { }
        }

        void Status(string msg, bool error)
        {
            lblStatus.ForeColor = error ? Color.Firebrick : Color.DarkGreen;
            lblStatus.Text = msg;
        }

        // ---------------- الحساب ----------------
        void Recalc()
        {
            if (busy) return;
            busy = true;
            try
            {
                double sub = 0;
                foreach (DataGridViewRow r in grid.Rows)
                {
                    double q = Db.Num(Convert.ToString(r.Cells["Qty"].Value));
                    double p = Db.Num(Convert.ToString(r.Cells["Price"].Value));
                    double t = q * p;
                    r.Cells["Total"].Value = t.ToString(sale ? "N0" : "N2");
                    sub += t;
                    if (sale)
                    {
                        double st = Db.Num(Convert.ToString(r.Cells["Stock"].Value));
                        r.DefaultCellStyle.BackColor = q > st ? Color.MistyRose : Color.White;
                    }
                    else
                    {
                        double prevCost = Db.Num(Convert.ToString(r.Cells["Prev"].Value));
                        r.Cells["Price"].Style.BackColor = Math.Abs(p - prevCost) > 1e-9 ? Color.FromArgb(255, 235, 170) : Color.White;
                    }
                }
                double disc = Db.Num(txtDiscount.Text);
                double net = Math.Max(sub - disc, 0);
                double paid = string.IsNullOrWhiteSpace(txtPaid.Text) ? net : Db.Num(txtPaid.Text);
                double rem = Math.Max(net - paid, 0);
                string f = sale ? "N0" : "N2";
                lblSub.Text = sub.ToString(f);
                lblNet.Text = net.ToString(f);
                lblRemain.Text = rem.ToString(f);
                lblRemain.ForeColor = rem > 0 ? Color.Firebrick : Color.DarkGreen;
                if (sale)
                {
                    // ما دُفع زيادة على الفاتورة يُعرض مخصوماً من الدين السابق (يُسأل عنه عند الحفظ)
                    double due = cachedPrev + rem - Math.Max(paid - net, 0);
                    lblPrev.Text = cachedPrev.ToString("N0");
                    lblDue.Text = due.ToString("N0");
                    lblDue.ForeColor = due > 0 ? Color.Firebrick : Color.DarkGreen;
                }
            }
            finally { busy = false; }
        }

        void ClearForm(bool record = true)
        {
            if (record && grid.Rows.Count > 0) PushDraft("مسح الفاتورة");
            grid.Rows.Clear();
            txtDiscount.Clear();
            txtPaid.Clear();
            cmbParty.Text = "";
            if (!sale) txtInvNo.Clear();
            Recalc();
            Status("", false);
            txtBarcode.Focus();
        }

        // ---------------- الحفظ ----------------
        void DoSave(bool print)
        {
            if (!Save()) return;
            int id = lastId;
            if (editId > 0)
            {
                if (print && sale) InvoicePrinter.ShowSale(id);
                UI.Info(this, "تم تعديل " + (sale ? "فاتورة البيع" : "فاتورة المشتريات") + " رقم " + id + ".");
                Close();
                return;
            }
            lastSavedId = id;
            Draft saved = TakeDraft();
            bool wasSale = sale;
            if (print && sale) InvoicePrinter.ShowSale(id);
            ClearForm(false);
            LoadParties();
            long extraPayId = lastExtraPayId;
            undo.Push("حفظ الفاتورة رقم " + id, () =>
            {
                Db.CancelInvoice(wasSale, id);
                if (extraPayId > 0) Db.Exec("DELETE FROM Payments WHERE Id=@p0", extraPayId);
                RestoreDraft(saved);
            });
            string msg = "تم حفظ " + (sale ? "فاتورة البيع" : "فاتورة المشتريات") + " رقم " + id;
            if (sale && lastRemaining > 0) msg += " — وأُضيف المتبقي " + lastRemaining.ToString("N0") + " ل.س إلى ديون الزبون (يظهر في: الديون ← فواتير الدين)";
            if (sale && lastExtra > 0) msg += " — وسُجّل " + lastExtra.ToString("N0") + " ل.س دفعةً من الدين السابق للزبون";
            if (!sale && lastChanged > 0) msg += " — وتحدّث سعر الشراء وسعر البيع لـ " + lastChanged + " صنف في قائمة الأصناف";
            Status(msg, false);
        }

        bool Save()
        {
            var lines = new List<Line>();
            bool overStock = false;
            foreach (DataGridViewRow r in grid.Rows)
            {
                double q = Db.Num(Convert.ToString(r.Cells["Qty"].Value));
                double p = Db.Num(Convert.ToString(r.Cells["Price"].Value));
                if (q <= 0) { UI.Warn(this, "الكمية غير صحيحة للصنف: " + r.Cells["Name"].Value); return false; }
                if (sale && q > Db.Num(Convert.ToString(r.Cells["Stock"].Value))) overStock = true;
                lines.Add(new Line { Id = Convert.ToInt32(r.Cells["Id"].Value), Name = Convert.ToString(r.Cells["Name"].Value), Qty = q, Price = p, Prev = Db.Num(Convert.ToString(r.Cells["Prev"].Value)) });
            }
            if (lines.Count == 0) { UI.Warn(this, "الفاتورة فارغة."); return false; }

            double sub = 0;
            foreach (var l in lines) sub += l.Qty * l.Price;
            double disc = Db.Num(txtDiscount.Text);
            double net = Math.Max(sub - disc, 0);
            double paid = string.IsNullOrWhiteSpace(txtPaid.Text) ? net : Db.Num(txtPaid.Text);
            string party = cmbParty.Text.Trim();
            lastExtra = 0; lastExtraPayId = 0;
            double extra = 0;
            if (paid > net + 1e-9)
            {
                // الزبون دفع أكثر من الفاتورة: إمّا تسديد من دينه السابق، أو باقٍ رُدّ له نقداً
                if (sale && editId <= 0 && party != "" &&
                    UI.Confirm(this, "المدفوع أكبر من صافي الفاتورة بـ " + (paid - net).ToString("N0") + " ل.س.\n" +
                                     "هل يُسجَّل الزائد دفعةً من الدين السابق للزبون \"" + party + "\"؟\n\n" +
                                     "(اختر \"لا\" إن كان الزائد قد رُدّ للزبون كباقٍ)"))
                    extra = paid - net;
                paid = net;
            }
            double rem = Math.Max(net - paid, 0);

            if (rem > 0 && party == "")
            {
                UI.Warn(this, "المبلغ المتبقي (دين) يحتاج اسم " + (sale ? "الزبون" : "المورد") + ".");
                return false;
            }
            string cur = sale ? "SYP" : cmbCur.Text;
            double prate = 1;
            if (!sale && cur == "USD")
            {
                prate = Db.Num(txtRate.Text);
                if (prate <= 0) { UI.Warn(this, "اكتب سعر الصرف."); return false; }
            }
            // الرصيد السابق للزبون قبل هذه الفاتورة (يُحفظ ويُطبع في الفاتورة)
            double prevToStore = 0;
            if (sale && party != "")
            {
                if (editId > 0 && party == editPartyName) prevToStore = editPrevBal;
                else
                {
                    object cidp = Db.Scalar("SELECT Id FROM Customers WHERE Name=@p0", party);
                    if (cidp != null && !(cidp is DBNull)) prevToStore = Db.CustomerBalance(Convert.ToInt32(cidp));
                }
            }
            // تنبيه تجاوز حدّ الدين
            if (sale && rem > 0 && party != "")
            {
                double prevBal = prevToStore;
                double limit = Db.DebtLimit;
                if (prevBal + rem > limit &&
                    !UI.Confirm(this, "⚠ تنبيه: دين الزبون \"" + party + "\" سيصبح " + (prevBal + rem).ToString("N0") + " ل.س وهو فوق الحد (" + limit.ToString("N0") + " ل.س).\nهل تريد المتابعة؟"))
                    return false;
            }
            if (overStock && !UI.Confirm(this, "بعض الكميات أكبر من المخزون المسجّل. هل تريد المتابعة؟")) return false;

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    int partyId = party == "" ? 0 : GetOrCreateParty(c, tx, party);
                    object pid = partyId == 0 ? (object)null : partyId;
                    double rate = Db.Rate;

                    if (sale)
                    {
                        long saleId;
                        var oldCosts = new Dictionary<int, double>();
                        if (editId > 0)
                        {
                            // إرجاع أثر الفاتورة القديمة ثم كتابتها من جديد بنفس الرقم
                            var oldItems = new List<double[]>();
                            using (var cmdOld = Db.Cmd(c, "SELECT ProductId, Qty, UnitCost FROM SaleItems WHERE SaleId=@p0", new object[] { editId }, tx))
                            using (var rdOld = cmdOld.ExecuteReader())
                            {
                                while (rdOld.Read()) oldItems.Add(new[] { Db.D(rdOld[0]), Db.D(rdOld[1]), Db.D(rdOld[2]) });
                            }
                            foreach (var oi in oldItems)
                            {
                                Db.ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)+@p0 WHERE Id=@p1", oi[1], (int)oi[0]);
                                oldCosts[(int)oi[0]] = oi[2];
                            }
                            Db.ExecTx(c, tx, "DELETE FROM SaleItems WHERE SaleId=@p0", editId);
                            Db.ExecTx(c, tx, "UPDATE Sales SET CustomerId=@p0, Total=@p1, Discount=@p2, Net=@p3, Paid=@p4, Remaining=@p5, PrevBalance=@p6 WHERE Id=@p7",
                                pid, sub, disc, net, paid, rem, prevToStore, editId);
                            saleId = editId;
                        }
                        else
                        {
                            Db.ExecTx(c, tx, "INSERT INTO Sales(Date,CustomerId,Total,Discount,Net,Paid,Remaining,PrevBalance) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                                now, pid, sub, disc, net, paid, rem, prevToStore);
                            saleId = Db.LastId(c, tx);
                        }
                        foreach (var l in lines)
                        {
                            double cost = 0;
                            if (!oldCosts.TryGetValue(l.Id, out cost))
                            {
                                cost = 0;
                                using (var cmd = Db.Cmd(c, "SELECT CostPrice,CostCurrency FROM Products WHERE Id=@p0", new object[] { l.Id }, tx))
                                using (var rd = cmd.ExecuteReader())
                                {
                                    if (rd.Read())
                                    {
                                        cost = Db.D(rd[0]);
                                        if (Convert.ToString(rd[1]) == "USD") cost *= rate;
                                    }
                                }
                            }
                            Db.ExecTx(c, tx, "INSERT INTO SaleItems(SaleId,ProductId,Name,Qty,UnitPrice,UnitCost) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",
                                saleId, l.Id, l.Name, l.Qty, l.Price, cost);
                            Db.ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)-@p0 WHERE Id=@p1", l.Qty, l.Id);
                        }
                        if (extra > 0 && partyId != 0)
                        {
                            Db.ExecTx(c, tx, "INSERT INTO Payments(Date,PartyType,PartyId,Amount,Note,Currency,Rate) VALUES(@p0,'C',@p1,@p2,@p3,'SYP',1)",
                                now, partyId, extra, "مع فاتورة البيع رقم " + saleId);
                            lastExtraPayId = Db.LastId(c, tx);
                            lastExtra = extra;
                        }
                        lastId = (int)saleId;
                    }
                    else
                    {
                        long purId;
                        if (editId > 0)
                        {
                            var oldItems = new List<double[]>();
                            using (var cmdOld = Db.Cmd(c, "SELECT ProductId, Qty FROM PurchaseItems WHERE PurchaseId=@p0", new object[] { editId }, tx))
                            using (var rdOld = cmdOld.ExecuteReader())
                            {
                                while (rdOld.Read()) oldItems.Add(new[] { Db.D(rdOld[0]), Db.D(rdOld[1]) });
                            }
                            foreach (var oi in oldItems)
                                Db.ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)-@p0 WHERE Id=@p1", oi[1], (int)oi[0]);
                            Db.ExecTx(c, tx, "DELETE FROM PurchaseItems WHERE PurchaseId=@p0", editId);
                            Db.ExecTx(c, tx, "UPDATE Purchases SET SupplierId=@p0, InvoiceNo=@p1, Currency=@p2, Rate=@p3, Total=@p4, Discount=@p5, Net=@p6, Paid=@p7, Remaining=@p8, NetSYP=@p9, PaidSYP=@p10, RemainingSYP=@p11 WHERE Id=@p12",
                                pid, txtInvNo.Text.Trim(), cur, prate, sub, disc, net, paid, rem, net * prate, paid * prate, rem * prate, editId);
                            purId = editId;
                        }
                        else
                        {
                            Db.ExecTx(c, tx, "INSERT INTO Purchases(Date,SupplierId,InvoiceNo,Currency,Rate,Total,Discount,Net,Paid,Remaining,NetSYP,PaidSYP,RemainingSYP) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12)",
                                now, pid, txtInvNo.Text.Trim(), cur, prate, sub, disc, net, paid, rem, net * prate, paid * prate, rem * prate);
                            purId = Db.LastId(c, tx);
                        }
                        foreach (var l in lines)
                        {
                            Db.ExecTx(c, tx, "INSERT INTO PurchaseItems(PurchaseId,ProductId,Qty,UnitCost) VALUES(@p0,@p1,@p2,@p3)",
                                purId, l.Id, l.Qty, l.Price);
                            Db.ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)+@p0, CostPrice=CASE WHEN @p1>0 THEN @p1 ELSE CostPrice END, CostCurrency=CASE WHEN @p1>0 THEN @p2 ELSE CostCurrency END, SellPrice=CASE WHEN @p1>0 AND IFNULL(Margin,-1)>=0 THEN ROUND(@p1*(1+Margin)*(CASE WHEN @p2='USD' THEN @p4 ELSE 1 END)/5.0)*5 ELSE SellPrice END WHERE Id=@p3",
                                l.Qty, l.Price, cur, l.Id, rate);
                        }
                        lastId = (int)purId;
                    }
                    tx.Commit();
                    lastRemaining = sale ? rem : 0;
                    lastChanged = 0;
                    if (!sale) foreach (var l in lines) if (l.Price > 0 && Math.Abs(l.Price - l.Prev) > 1e-9) lastChanged++;
                    if (sale && partyId != 0) Db.Unfreeze(partyId);     // أي بيع جديد يُعيد الدين إلى الجاري
                    return true;
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    UI.Warn(this, "تعذّر الحفظ: " + ex.Message);
                    return false;
                }
            }
        }

        int GetOrCreateParty(SQLiteConnection c, SQLiteTransaction tx, string name)
        {
            string table = sale ? "Customers" : "Suppliers";
            var o = Db.ScalarTx(c, tx, "SELECT Id FROM " + table + " WHERE Name=@p0", name);
            if (o != null && !(o is DBNull)) return Convert.ToInt32(o);
            Db.ExecTx(c, tx, "INSERT INTO " + table + "(Name) VALUES(@p0)", name);
            return (int)Db.LastId(c, tx);
        }
    }
}
