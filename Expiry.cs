using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>تواريخ الإنتاج والانتهاء: قراءة ما يكتبه البائع بأي صيغة شائعة، وحالة الصنف (منتهٍ / قريب / سليم).</summary>
    public static class ExpiryUtil
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public enum State { None, Ok, Soon, Expired }

        /// <summary>
        /// يقبل: 2027-03-15، 15/3/2027، 15-3-27، 3/2027 (شهر/سنة فقط)، 2027-03 ، وبالأرقام العربية أيضاً.
        /// الشهر وحده: لتاريخ الانتهاء آخر يوم في الشهر، ولتاريخ الإنتاج أول يوم.
        /// النص الفارغ صحيح ويعني "بلا تاريخ" (iso = null).
        /// </summary>
        public static bool TryParse(string text, bool endOfMonth, out string iso)
        {
            iso = null;
            string s = (text ?? "").Trim();
            if (s == "") return true;
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
                else if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
                else sb.Append(ch);
            }
            string[] p = Regex.Split(sb.ToString(), @"[^0-9]+");
            p = Array.FindAll(p, x => x != "");
            if (Array.Exists(p, x => x.Length > 4)) return false;
            int y, m, d;
            if (p.Length == 3)
            {
                if (p[0].Length == 4) { y = int.Parse(p[0]); m = int.Parse(p[1]); d = int.Parse(p[2]); }   // سنة-شهر-يوم
                else { d = int.Parse(p[0]); m = int.Parse(p[1]); y = int.Parse(p[2]); }                   // يوم/شهر/سنة
            }
            else if (p.Length == 2)
            {
                if (p[0].Length == 4) { y = int.Parse(p[0]); m = int.Parse(p[1]); }
                else { m = int.Parse(p[0]); y = int.Parse(p[1]); }
                if (y < 100) y += 2000;
                if (m < 1 || m > 12) return false;
                d = endOfMonth ? DateTime.DaysInMonth(y, m) : 1;
            }
            else return false;
            if (y < 100) y += 2000;
            if (y < 2000 || y > 2100 || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return false;
            iso = new DateTime(y, m, d).ToString("yyyy-MM-dd", Inv);
            return true;
        }

        public static State Of(object expiry)
        {
            // يقبل أيضاً ما كُتب للتو بالجدول ولم يُحفظ بعد (مثل 3/2027)
            if (!TryParse(Convert.ToString(expiry), true, out string iso) || iso == null) return State.None;
            DateTime dt = DateTime.ParseExact(iso, "yyyy-MM-dd", Inv);
            if (dt < DateTime.Today) return State.Expired;
            if (dt <= DateTime.Today.AddDays(WarnDays())) return State.Soon;
            return State.Ok;
        }

        // يُستدعى Of لكل خلية عند الرسم، فلا نقرأ الإعداد من القاعدة كل مرة
        static int cachedDays;
        static DateTime cachedAt;
        static int WarnDays()
        {
            if ((DateTime.Now - cachedAt).TotalSeconds > 60) { cachedDays = Db.ExpiryWarnDays; cachedAt = DateTime.Now; }
            return cachedDays;
        }

        public static readonly Color ExpiredBack = Color.FromArgb(255, 205, 205);
        public static readonly Color SoonBack = Color.FromArgb(255, 232, 180);

        /// <summary>عدد الأصناف الموجودة في المخزون المنتهية أو القريبة من الانتهاء (لشريط الشاشة الرئيسية).</summary>
        public static long CountAlerts()
        {
            return Convert.ToInt64(Db.Scalar(
                "SELECT COUNT(*) FROM Products WHERE IFNULL(ExpiryDate,'')<>'' AND Stock>0 AND ExpiryDate<=date('now','localtime',@p0)",
                "+" + Db.ExpiryWarnDays + " day"));
        }
    }

    /// <summary>شاشة الصلاحية: الأصناف ذات تاريخ انتهاء مرتبة من الأقرب، المنتهية بالأحمر والقريبة بالبرتقالي.</summary>
    public class ExpiryForm : Form
    {
        readonly DataGridView grid = new DataGridView();
        readonly CheckBox chkAlerts = new CheckBox { Text = "المنتهية والقريبة من الانتهاء فقط", Checked = true, AutoSize = true, Margin = new Padding(12, 12, 4, 0) };
        readonly CheckBox chkStock = new CheckBox { Text = "الموجودة في المخزون فقط", Checked = true, AutoSize = true, Margin = new Padding(12, 12, 4, 0) };

        public ExpiryForm()
        {
            UI.Rtl(this);
            Text = "الصلاحية";
            WindowState = FormWindowState.Maximized;
            UI.SetupGrid(grid, true);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            chkAlerts.CheckedChanged += (a, b) => Reload();
            chkStock.CheckedChanged += (a, b) => Reload();
            bar.Controls.Add(chkAlerts);
            bar.Controls.Add(chkStock);
            var note = new Label
            {
                Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 10, 8, 0),
                Text = "بالأحمر: منتهي الصلاحية. بالبرتقالي: ينتهي خلال " + Db.ExpiryWarnDays + " يوماً. التواريخ تُكتب لكل صنف من شاشة الأصناف والمخزون (اختيارية)."
            };
            grid.CellFormatting += (a, e) =>
            {
                if (e.RowIndex < 0 || grid.Columns["تاريخ الانتهاء"] == null) return;
                var st = ExpiryUtil.Of(grid.Rows[e.RowIndex].Cells["تاريخ الانتهاء"].Value);
                if (st == ExpiryUtil.State.Expired) e.CellStyle.BackColor = ExpiryUtil.ExpiredBack;
                else if (st == ExpiryUtil.State.Soon) e.CellStyle.BackColor = ExpiryUtil.SoonBack;
            };
            Controls.Add(grid);
            Controls.Add(note);
            Controls.Add(bar);
            Reload();
        }

        void Reload()
        {
            string where = "IFNULL(ExpiryDate,'')<>''";
            if (chkAlerts.Checked) where += " AND ExpiryDate<=date('now','localtime',@p0)";
            if (chkStock.Checked) where += " AND Stock>0";
            grid.DataSource = Db.Query(
                "SELECT Name AS \"الصنف\", IFNULL(Category,'') AS \"القسم\", Stock AS \"المخزون\", IFNULL(ProdDate,'') AS \"تاريخ الإنتاج\", " +
                "ExpiryDate AS \"تاريخ الانتهاء\", CAST(julianday(ExpiryDate)-julianday(date('now','localtime')) AS INTEGER) AS \"الأيام المتبقية\" " +
                "FROM Products WHERE " + where + " ORDER BY ExpiryDate, Name", "+" + Db.ExpiryWarnDays + " day");
            UI.Col(grid, 0).FillWeight = 160;
            if (grid.Columns["المخزون"] != null) grid.Columns["المخزون"].DefaultCellStyle.Format = "0.##";
        }
    }
}
