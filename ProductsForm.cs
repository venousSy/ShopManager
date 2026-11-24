using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>الأصناف والمخزون: تعديل مباشر في الجدول، تسعير تلقائي حسب سعر الصرف، استيراد CSV، تراجع.</summary>
    public class ProductsForm : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly SQLiteConnection conn;
        readonly SQLiteDataAdapter ad;
        readonly SQLiteCommandBuilder cb;
        readonly DataTable tbl = new DataTable();
        readonly DataGridView grid = new DataGridView();
        readonly TextBox txtSearch = new TextBox();
        readonly Label lblInfo = new Label();
        readonly UndoStack undo = new UndoStack();
        bool suspend;

        public ProductsForm()
        {
            UI.Rtl(this);
            Text = "الأصناف والمخزون";
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            UI.SetupGrid(grid, false);
            grid.AllowUserToDeleteRows = false;

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            txtSearch.Width = 240;
            txtSearch.Margin = new Padding(4, 10, 4, 0);
            txtSearch.PlaceholderText = "بحث بالاسم أو الباركود";
            txtSearch.TextChanged += (a, b) => ApplyFilter();
            bar.Controls.Add(UI.Lbl("بحث:"));
            bar.Controls.Add(txtSearch);
            bar.Controls.Add(MakeBtn("حفظ التعديلات", (a, b) => SaveChanges(false)));
            bar.Controls.Add(MakeBtn("حذف الصنف الحالي", (a, b) => DeleteCurrent()));
            bar.Controls.Add(MakeBtn("استيراد من CSV", (a, b) => ImportCsv()));
            bar.Controls.Add(MakeBtn("اعتماد المخزون كمخزون افتتاحي", (a, b) => SetOpeningStock()));
            bar.Controls.Add(MakeBtn("أسماء المورد المحفوظة", (a, b) => { new AliasesForm().Show(); }));
            bar.Controls.Add(UndoStack.MakeButton(DoUndo));

            lblInfo.Dock = DockStyle.Bottom;
            lblInfo.Height = 64;
            lblInfo.Padding = new Padding(8, 8, 8, 0);
            lblInfo.Text = "اكتب في الجدول مباشرة ثم اضغط (حفظ التعديلات). عملة الشراء: USD أو SYP. " +
                           "نسبة الربح: 0.15 تعني 15% فيُحسب سعر البيع تلقائياً ويتغيّر مع سعر الصرف للأصناف المشتراة بالدولار. " +
                           "اتركها -1 لسعر بيع ثابت. وتعديلك يدوياً لسعر البيع يجعله ثابتاً. القسم مطلوب لكل صنف جديد.";

            Controls.Add(grid);
            Controls.Add(lblInfo);
            Controls.Add(bar);

            conn = Db.Open();
            ad = new SQLiteDataAdapter("SELECT Id,Name,Barcode,Category,CostPrice,CostCurrency,Margin,SellPrice,Stock,MinStock FROM Products ORDER BY Category, Name", conn);
            // التحديث بالمعرّف فقط (لا مقارنة بكل القيم القديمة) حتى لا يفشل الحفظ لأن البيع غيّر المخزون؛
            // المخزون نفسه يُحدَّث من قاعدة البيانات قبل الحفظ في RefreshUntouchedStock.
            cb = new SQLiteCommandBuilder(ad) { ConflictOption = ConflictOption.OverwriteChanges };

            grid.DefaultValuesNeeded += (a, b) =>
            {
                b.Row.Cells["CostCurrency"].Value = "SYP";
                b.Row.Cells["CostPrice"].Value = 0;
                b.Row.Cells["Margin"].Value = -1;
                b.Row.Cells["SellPrice"].Value = 0;
                b.Row.Cells["Stock"].Value = 0;
                b.Row.Cells["MinStock"].Value = 0;
            };

            // تسجيل التعديلات غير المحفوظة للتراجع
            tbl.ColumnChanging += (s2, e2) =>
            {
                if (suspend) return;
                DataRow row = e2.Row;
                DataColumn col = e2.Column;
                if (row.RowState == DataRowState.Detached) return;
                object old = row[col];
                if (Equals(old, e2.ProposedValue)) return;
                undo.Push("تعديل (" + col.ColumnName + ")", () => { suspend = true; try { row[col] = old; } finally { suspend = false; } });
            };
            tbl.RowChanged += (s2, e2) =>
            {
                if (suspend || e2.Action != DataRowAction.Add) return;
                DataRow row = e2.Row;
                undo.Push("إضافة صنف", () => { suspend = true; try { row.Delete(); } finally { suspend = false; } });
            };
            tbl.RowDeleting += (s2, e2) =>
            {
                if (suspend) return;
                DataRow row = e2.Row;
                undo.Push("حذف صنف", () => { suspend = true; try { row.RejectChanges(); } finally { suspend = false; } });
            };

            Reload();
            KeyDown += (a, b) =>
            {
                if (b.Control && b.KeyCode == Keys.Z && !grid.IsCurrentCellInEditMode && !(ActiveControl is TextBox))
                { b.Handled = true; DoUndo(); }
            };
            FormClosed += (a, b) => { conn.Dispose(); };
        }

        static Button MakeBtn(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            b.Click += h;
            return b;
        }

        void DoUndo()
        {
            grid.EndEdit();
            string what = undo.Undo();
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            grid.Refresh();
            ApplyFilter();
        }

        void Reload()
        {
            suspend = true;
            try
            {
                tbl.Clear();
                ad.Fill(tbl);
            }
            finally { suspend = false; }
            grid.DataSource = tbl;
            var heads = new Dictionary<string, string>
            {
                { "Id", "رقم" }, { "Name", "الصنف" }, { "Barcode", "الباركود" }, { "Category", "القسم" },
                { "CostPrice", "سعر الشراء" }, { "CostCurrency", "عملة الشراء" }, { "Margin", "نسبة الربح (-1 ثابت)" },
                { "SellPrice", "سعر البيع (ل.س)" }, { "Stock", "المخزون" }, { "MinStock", "الحد الأدنى" }
            };
            foreach (var kv in heads) if (grid.Columns[kv.Key] != null) grid.Columns[kv.Key].HeaderText = kv.Value;
            grid.Columns["Id"].ReadOnly = true;
            grid.Columns["Id"].FillWeight = 30;
            grid.Columns["Name"].FillWeight = 170;
            grid.Columns["Category"].FillWeight = 80;
            grid.Columns["Margin"].FillWeight = 75;
            ApplyFilter();
        }

        void ApplyFilter()
        {
            string t = txtSearch.Text.Trim().Replace("'", "''").Replace("[", "").Replace("]", "").Replace("%", "").Replace("*", "");
            tbl.DefaultView.RowFilter = t == "" ? "" : "Name LIKE '%" + t + "%' OR Barcode LIKE '%" + t + "%'";
        }

        static string FixCurrency(object v)
        {
            string s = Convert.ToString(v).Trim().ToUpperInvariant();
            return (s.Contains("USD") || s.Contains("$") || s.Contains("دولار")) ? "USD" : "SYP";
        }

        static bool Blank(object v)
        {
            return v == DBNull.Value || v == null || string.IsNullOrWhiteSpace(Convert.ToString(v));
        }

        void Normalize()
        {
            double rate = Db.Rate;
            foreach (DataRow r in tbl.Rows)
            {
                if (r.RowState == DataRowState.Deleted || r.RowState == DataRowState.Unchanged || r.RowState == DataRowState.Detached) continue;
                r["Name"] = Convert.ToString(r["Name"]).Trim();
                if (Blank(r["Barcode"])) r["Barcode"] = DBNull.Value; else r["Barcode"] = Convert.ToString(r["Barcode"]).Trim();
                r["CostCurrency"] = FixCurrency(r["CostCurrency"]);
                foreach (var col in new[] { "CostPrice", "SellPrice", "Stock", "MinStock" })
                    if (Blank(r[col])) r[col] = 0;
                if (Blank(r["Margin"])) r["Margin"] = -1;

                double cost = Db.D(r["CostPrice"]);
                string cur = Convert.ToString(r["CostCurrency"]);
                double mg = Db.D(r["Margin"]);

                if (r.RowState == DataRowState.Modified)
                {
                    double oc = Db.D(r["CostPrice", DataRowVersion.Original]);
                    string ocur = Convert.ToString(r["CostCurrency", DataRowVersion.Original]);
                    double om = Blank(r["Margin", DataRowVersion.Original]) ? -1 : Db.D(r["Margin", DataRowVersion.Original]);
                    double os = Db.D(r["SellPrice", DataRowVersion.Original]);
                    bool sellChanged = Math.Abs(Db.D(r["SellPrice"]) - os) > 0.0001;
                    bool inputsChanged = Math.Abs(cost - oc) > 1e-9 || cur != ocur || Math.Abs(mg - om) > 1e-9;
                    if (sellChanged && !inputsChanged) { r["Margin"] = -1; }          // تعديل يدوي ← سعر ثابت
                    else if (mg >= 0 && cost > 0) r["SellPrice"] = Db.AutoPrice(cost, cur, mg, rate);
                }
                else if (mg >= 0 && cost > 0)
                {
                    r["SellPrice"] = Db.AutoPrice(cost, cur, mg, rate);
                }
            }
        }

        bool SaveChanges(bool silent)
        {
            try
            {
                grid.EndEdit();
                BindingContext[tbl].EndCurrentEdit();
                var noCat = new List<string>();
                foreach (DataRow r in tbl.Rows)
                {
                    if (r.RowState != DataRowState.Added && r.RowState != DataRowState.Modified) continue;
                    if (Blank(r["Category"])) noCat.Add(Convert.ToString(r["Name"]));
                }
                if (noCat.Count > 0)
                {
                    UI.Warn(this, "حدّد القسم لهذه الأصناف قبل الحفظ (ليوضع كل صنف مع أصنافه):\n" + string.Join("\n", noCat.GetRange(0, Math.Min(noCat.Count, 10))));
                    return false;
                }
                var ch = Db.TableChange.Begin("Products");
                suspend = true;
                try { Normalize(); RefreshUntouchedStock(); ad.Update(tbl); }
                finally { suspend = false; }
                Action undoSave = ch.End();
                undo.Clear();
                undo.Push("حفظ تعديلات الأصناف", () => { undoSave(); Reload(); });
                Reload();
                if (!silent) UI.Info(this, "تم حفظ التعديلات.");
                return true;
            }
            catch (Exception ex)
            {
                UI.Warn(this, "تعذّر الحفظ (قد يكون اسم أو باركود مكرراً): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// الجدول قد يكون مفتوحاً منذ مدة وبيعت أصناف من نافذة الفاتورة. للصفوف المعدّلة التي لم يغيّر
        /// المستخدم مخزونها، نقرأ المخزون الحالي من قاعدة البيانات كي لا يُكتب الرقم القديم فوق البيع.
        /// </summary>
        void RefreshUntouchedStock()
        {
            foreach (DataRow r in tbl.Rows)
            {
                if (r.RowState != DataRowState.Modified) continue;
                if (!Equals(r["Stock", DataRowVersion.Original], r["Stock", DataRowVersion.Current])) continue;
                object cur = Db.Scalar("SELECT Stock FROM Products WHERE Id=@p0", r["Id"]);
                if (cur != null && !(cur is DBNull)) r["Stock"] = cur;
            }
        }

        void DeleteCurrent()
        {
            if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) return;
            if (!UI.Confirm(this, "حذف الصنف المحدد؟ (لا يؤثر على الفواتير القديمة. ويمكنك التراجع بعده.)")) return;
            grid.Rows.Remove(grid.CurrentRow);
            SaveChanges(true);
        }

        /// <summary>يسجّل قيمة المخزون الحالي كمخزون افتتاحي (يظهر في التقارير).</summary>
        void SetOpeningStock()
        {
            if (tbl.GetChanges() != null)
            {
                if (!UI.Confirm(this, "توجد تعديلات غير محفوظة وسيتم حفظها أولاً. متابعة؟")) return;
                if (!SaveChanges(true)) return;
            }
            double rate = Db.Rate;
            double value = Db.D(Db.Scalar("SELECT IFNULL(SUM(Stock*CostPrice*CASE WHEN CostCurrency='USD' THEN @p0 ELSE 1 END),0) FROM Products WHERE Stock>0", rate));
            long items = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM Products WHERE Stock>0"));
            if (items == 0) { UI.Warn(this, "لا يوجد مخزون مسجّل. اكتب كميات الجرد في عمود (المخزون) أولاً."); return; }
            if (!UI.Confirm(this, "اعتماد المخزون الحالي كمخزون افتتاحي؟\n\nعدد الأصناف: " + items + "\nالقيمة بسعر الشراء: " + value.ToString("N0") +
                                  " ل.س (الدولار بسعر الصرف " + rate.ToString("0.##", Inv) + ")\n\nتظهر هذه القيمة في التقارير ضمن رأس المال المبدئي.")) return;
            var ch = Db.TableChange.Begin("Settings");
            Db.Set("opening_stock_value", value.ToString(Inv));
            Db.Set("opening_stock_items", items.ToString(Inv));
            Db.Set("opening_stock_date", DateTime.Today.ToString("yyyy-MM-dd", Inv));
            undo.Push("اعتماد المخزون الافتتاحي", ch.End());
            UI.Info(this, "تم اعتماد المخزون الافتتاحي: " + value.ToString("N0") + " ل.س. يمكنك رؤيته في صفحة التقارير.");
        }

        void ImportCsv()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "CSV|*.csv";
                dlg.Title = "اختر ملف الأصناف";
                string guess = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "products_import.csv");
                if (File.Exists(guess)) { dlg.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory; dlg.FileName = "products_import.csv"; }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var ch = Db.TableChange.Begin("Products");
                int added = 0, updated = 0, failed = 0;
                string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
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
                            string barcode = f.Length > 1 ? f[1].Trim() : "";
                            double cost = f.Length > 2 ? Db.Num(f[2]) : 0;
                            string cur = f.Length > 3 ? FixCurrency(f[3]) : "SYP";
                            double sell = f.Length > 4 ? Db.Num(f[4]) : 0;
                            string cat = f.Length > 5 ? f[5].Trim() : "";
                            bool hasStock = f.Length > 6 && f[6].Trim() != "";
                            double stock = hasStock ? Db.Num(f[6]) : 0;
                            double min = f.Length > 7 ? Db.Num(f[7]) : 0;
                            double margin = (f.Length > 8 && f[8].Trim() != "") ? Db.Num(f[8]) : -1;

                            var id = Db.ScalarTx(c, tx, "SELECT Id FROM Products WHERE Name=@p0", name);
                            if (id != null && !(id is DBNull))
                            {
                                Db.ExecTx(c, tx, "UPDATE Products SET Barcode=CASE WHEN @p1<>'' THEN @p1 ELSE Barcode END, CostPrice=@p2, CostCurrency=@p3, SellPrice=@p4, Category=@p5, Stock=CASE WHEN @p6=1 THEN @p7 ELSE Stock END, MinStock=CASE WHEN @p8>0 THEN @p8 ELSE MinStock END, Margin=@p9 WHERE Id=@p0",
                                    Convert.ToInt32(id), barcode, cost, cur, sell, cat, hasStock ? 1 : 0, stock, min, margin);
                                updated++;
                            }
                            else
                            {
                                Db.ExecTx(c, tx, "INSERT INTO Products(Name,Barcode,Category,CostPrice,CostCurrency,SellPrice,Stock,MinStock,Margin) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                    name, barcode == "" ? null : barcode, cat, cost, cur, sell, stock, min, margin);
                                added++;
                            }
                        }
                        catch { failed++; }
                    }
                    tx.Commit();
                }
                undo.Clear();
                Action undoImport = ch.End();
                undo.Push("استيراد الأصناف", () => { undoImport(); Reload(); });
                Reload();
                UI.Info(this, "تم الاستيراد (مرتّب حسب الأقسام).\nأصناف جديدة: " + added + "\nأصناف محدّثة: " + updated + (failed > 0 ? "\nسطور فشلت (باركود مكرر غالباً): " + failed : ""));
            }
        }
    }
}
