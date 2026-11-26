using System;
using System.Data;
using System.IO;
using System.Windows.Forms;

namespace ShopManager
{
    public class SettingsForm : Form
    {
        readonly TextBox txtName = new TextBox();
        readonly TextBox txtPhone = new TextBox();
        readonly TextBox txtRate = new TextBox();
        readonly TextBox txtCash = new TextBox();
        readonly TextBox txtCapital = new TextBox();
        readonly TextBox txtLimit = new TextBox();
        readonly TextBox txtFreeze = new TextBox();
        readonly ComboBox cmbPrintMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox cmbPrinter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox chkAutoPrint = new CheckBox { Text = "اطبع إيصال 80mm فوراً عند حفظ أي فاتورة بيع", AutoSize = true };
        const string DefaultPrinterLabel = "(الطابعة الافتراضية في ويندوز)";
        readonly TextBox txtSyncFolder = new TextBox();
        readonly TextBox txtMachine = new TextBox();
        readonly UndoStack undo = new UndoStack();

        public SettingsForm()
        {
            UI.Rtl(this);
            Text = "الإعدادات";
            Width = 760; Height = 800;   // الارتفاع النهائي يُحسب من المحتوى عند الفتح (انظر FitToScreen)
            KeyPreview = true;
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z && !(ActiveControl is TextBox)) { b.Handled = true; DoUndo(); } };
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = true;
            StartPosition = FormStartPosition.CenterParent;

            var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 8, 16, 8) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            Row(tl, "اسم المحل (يظهر في الفاتورة)", txtName);
            Row(tl, "الهاتف / العنوان", txtPhone);
            Row(tl, "سعر صرف الدولار (ل.س)", txtRate);
            Row(tl, "النقد الافتتاحي في الصندوق (ل.س)", txtCash);
            Row(tl, "رأس المال المبدئي (اختياري، للمقارنة)", txtCapital);
            Row(tl, "حدّ الدين الذي توضع بجانبه إشارة (ل.س)", txtLimit);
            Row(tl, "تجميد الدين بعد كم يوماً بلا حركة", txtFreeze);

            cmbPrintMode.Items.AddRange(new object[] { "ورق A4 (مع معاينة)", "إيصال حراري 80mm (طباعة مباشرة)" });
            cmbPrinter.Items.Add(DefaultPrinterLabel);
            try { foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) cmbPrinter.Items.Add(p); } catch { }
            Row(tl, "طباعة فاتورة البيع", cmbPrintMode);
            Row(tl, "طابعة الإيصالات", cmbPrinter);
            Row(tl, "طباعة فورية", chkAutoPrint);

            // المزامنة: إعدادات هذا الجهاز فقط (ملف sync_config.json بجانب البرنامج)
            var syncRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            txtSyncFolder.Width = 230;
            var btnBrowse = new Button { Text = "اختيار...", AutoSize = true };
            btnBrowse.Click += (a, b) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "مجلد داخل Google Drive أو OneDrive يظهر على الجهازين", UseDescriptionForTitle = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtSyncFolder.Text = dlg.SelectedPath;
            };
            syncRow.Controls.Add(txtSyncFolder);
            syncRow.Controls.Add(btnBrowse);
            Row(tl, "مجلد المزامنة بين الجهازين (فارغ = بلا مزامنة)", syncRow);
            Row(tl, "اسم هذا الجهاز/الوردية", txtMachine);

            LoadValues();

            var btnSave = new Button { Text = "حفظ الإعدادات", Dock = DockStyle.Top, Height = 46, Font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold) };
            btnSave.Click += (a, b) => Save();
            var btnUndo = UndoStack.MakeButton(DoUndo);
            var btnBackup = new Button { Text = "نسخة احتياطية من البيانات" };
            btnBackup.Click += (a, b) => Backup();
            var btnTestReceipt = new Button { Text = "معاينة إيصال تجريبي (80mm)" };
            btnTestReceipt.Click += (a, b) => InvoicePrinter.PreviewReceipt();
            var btnUsers = new Button { Text = "المستخدمون والصلاحيات" };
            btnUsers.Click += (a, b) => { using (var f = new UsersForm()) f.ShowDialog(this); };

            // الأزرار الأربعة في عمودين بدل أربعة أسطر متتالية (توفير ارتفاع)
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 92, ColumnCount = 2, RowCount = 2, Padding = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            foreach (var b in new[] { btnUsers, btnBackup, btnTestReceipt, btnUndo })
            {
                b.AutoSize = false; b.Dock = DockStyle.Fill; b.Margin = new Padding(2);
                grid.Controls.Add(b);
            }
            var info = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(16, 8, 16, 0), Text = "ملف البيانات: " + Db.DbPath, AutoEllipsis = true };

            Controls.Add(info);
            Controls.Add(grid);
            Controls.Add(btnSave);
            Controls.Add(tl);
            Load += (a, b) => FitToScreen(tl.Height + btnSave.Height + grid.Height + info.Height + 8);
        }

        /// <summary>ارتفاع النافذة = محتواها، وبحد أقصى مساحة الشاشة (فوق شريط المهام) مع شريط تمرير إن لزم.</summary>
        void FitToScreen(int contentHeight)
        {
            var wa = Screen.FromControl(this).WorkingArea;
            int frame = Height - ClientSize.Height;
            int h = Math.Min(contentHeight + frame, wa.Height);
            if (contentHeight + frame > wa.Height) AutoScroll = true;
            Height = h;
            Top = Math.Max(wa.Top, wa.Top + (wa.Height - h) / 2);
        }

        static void Row(TableLayoutPanel tl, string label, Control tb)
        {
            tl.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 7, 3, 7) });
            tb.Dock = DockStyle.Fill;
            tb.Margin = new Padding(3, 4, 3, 4);
            tl.Controls.Add(tb);
        }

        void LoadValues()
        {
            txtName.Text = Db.Get("shop_name");
            txtPhone.Text = Db.Get("shop_phone");
            txtRate.Text = Db.Get("rate", "151");
            txtCash.Text = Db.Get("opening_cash", "0");
            txtCapital.Text = Db.Get("opening_capital", "0");
            txtLimit.Text = Db.Get("debt_limit", "200000");
            txtFreeze.Text = Db.Get("freeze_days", "20");
            cmbPrintMode.SelectedIndex = Db.Get("print_mode", InvoicePrinter.ModeA4) == InvoicePrinter.ModeReceipt ? 1 : 0;
            string pr = Db.Get("receipt_printer", "");
            int pi = pr == "" ? 0 : cmbPrinter.Items.IndexOf(pr);
            if (pi < 0) { cmbPrinter.Items.Add(pr); pi = cmbPrinter.Items.Count - 1; }   // طابعة محفوظة غير موصولة الآن
            cmbPrinter.SelectedIndex = pi;
            chkAutoPrint.Checked = InvoicePrinter.AutoPrint;
            var cfg = SyncConfig.Load();
            txtSyncFolder.Text = cfg.Folder;
            txtMachine.Text = cfg.Machine;
        }

        void DoUndo()
        {
            string what = undo.Undo();
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            LoadValues();
        }

        void Save()
        {
            double newRate = Db.Num(txtRate.Text);
            if (newRate <= 0) { UI.Warn(this, "سعر الصرف غير صحيح."); return; }
            double oldRate = Db.Rate;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var chSettings = Db.TableChange.Begin("Settings");
            var chProducts = Db.TableChange.Begin("Products");

            Db.Set("shop_name", txtName.Text.Trim());
            Db.Set("shop_phone", txtPhone.Text.Trim());
            Db.Set("rate", newRate.ToString(inv));
            Db.Set("opening_cash", Db.Num(txtCash.Text).ToString(inv));
            Db.Set("opening_capital", Db.Num(txtCapital.Text).ToString(inv));
            Db.Set("debt_limit", (Db.Num(txtLimit.Text) > 0 ? Db.Num(txtLimit.Text) : 200000).ToString(inv));
            Db.Set("freeze_days", ((int)Db.Num(txtFreeze.Text) > 0 ? (int)Db.Num(txtFreeze.Text) : 20).ToString(inv));
            Db.Set("print_mode", cmbPrintMode.SelectedIndex == 1 ? InvoicePrinter.ModeReceipt : InvoicePrinter.ModeA4);
            string printer = Convert.ToString(cmbPrinter.SelectedItem);
            Db.Set("receipt_printer", printer == DefaultPrinterLabel ? "" : printer);
            Db.Set("auto_print", chkAutoPrint.Checked ? "1" : "0");

            string msg = "تم الحفظ.";
            var cfg = SyncConfig.Load();
            string newFolder = txtSyncFolder.Text.Trim(), newMachine = txtMachine.Text.Trim();
            if (newMachine == "") newMachine = Environment.MachineName;
            if (newFolder != cfg.Folder || newMachine != cfg.Machine)
            {
                if (cfg.Enabled && cfg.Handover().LoadState().Holding && newFolder != cfg.Folder)
                    UI.Warn(this, "هذا الجهاز مستلم للبيانات الآن. سلّم الوردية أولاً (إغلاق الوردية وتسليم) قبل تغيير مجلد المزامنة. لم يُغيَّر المجلد.");
                else
                {
                    if (newFolder != "" && !Directory.Exists(newFolder)) { UI.Warn(this, "مجلد المزامنة غير موجود."); return; }
                    cfg.Folder = newFolder; cfg.Machine = newMachine; cfg.Save();
                    msg += "\nإعدادات المزامنة تُطبَّق عند إعادة تشغيل البرنامج.";
                }
            }
            if (Math.Abs(newRate - oldRate) > 1e-9)
            {
                int n = Db.RecalcPrices(newRate);
                msg += "\nتم تحديث أسعار البيع حسب سعر الصرف الجديد (" + newRate.ToString("0.##", inv) + ") لـ " + n + " صنف. الأصناف ذات السعر الثابت لم تتغير.";
            }
            // التراجع يعيد الإعدادات وأسعار البيع التي غيّرها هذا الحفظ فقط، ولا يمسّ المخزون أو ما تغيّر بعده
            Action undoSettings = chSettings.End(), undoProducts = chProducts.End();
            undo.Push("حفظ الإعدادات", () => { undoSettings(); undoProducts(); });
            UI.Info(this, msg);
        }

        void Backup()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.FileName = "shop_backup_" + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".db";
                dlg.Filter = "Database|*.db";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.Copy(Db.DbPath, dlg.FileName, true);
                UI.Info(this, "تم حفظ النسخة الاحتياطية.");
            }
        }
    }
}
