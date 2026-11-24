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
        readonly UndoStack undo = new UndoStack();

        public SettingsForm()
        {
            UI.Rtl(this);
            Text = "الإعدادات";
            Width = 620; Height = 680;
            KeyPreview = true;
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z && !(ActiveControl is TextBox)) { b.Handled = true; DoUndo(); } };
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = true;
            StartPosition = FormStartPosition.CenterParent;

            var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(20) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            Row(tl, "اسم المحل (يظهر في الفاتورة)", txtName);
            Row(tl, "الهاتف / العنوان", txtPhone);
            Row(tl, "سعر صرف الدولار (ل.س)", txtRate);
            Row(tl, "النقد الافتتاحي في الصندوق (ل.س)", txtCash);
            Row(tl, "رأس المال المبدئي (اختياري، للمقارنة)", txtCapital);
            Row(tl, "حدّ الدين الذي توضع بجانبه إشارة (ل.س)", txtLimit);
            Row(tl, "تجميد الدين بعد كم يوماً بلا حركة", txtFreeze);

            LoadValues();

            var btnSave = new Button { Text = "حفظ الإعدادات", Dock = DockStyle.Top, Height = 44 };
            btnSave.Click += (a, b) => Save();
            var btnUndo = UndoStack.MakeButton(DoUndo);
            btnUndo.Dock = DockStyle.Top; btnUndo.AutoSize = false; btnUndo.Height = 44; btnUndo.Margin = new Padding(0);
            var btnBackup = new Button { Text = "نسخة احتياطية من البيانات", Dock = DockStyle.Top, Height = 44 };
            btnBackup.Click += (a, b) => Backup();
            var info = new Label { Dock = DockStyle.Top, Height = 60, Padding = new Padding(20, 10, 20, 0), Text = "ملف البيانات: " + Db.DbPath };

            Controls.Add(info);
            Controls.Add(btnBackup);
            Controls.Add(btnUndo);
            Controls.Add(btnSave);
            Controls.Add(tl);
        }

        static void Row(TableLayoutPanel tl, string label, TextBox tb)
        {
            tl.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 10) });
            tb.Dock = DockStyle.Fill;
            tb.Margin = new Padding(3, 6, 3, 6);
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

            string msg = "تم الحفظ.";
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
