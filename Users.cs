using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>الصلاحيات: مفتاح ثابت يُحفظ في قاعدة البيانات + نص عربي يظهر بجانب مربع الاختيار.</summary>
    public static class Perm
    {
        public const string Admin = "admin", Sell = "sell", Discount = "discount", EditInvoices = "edit_invoices",
            SaleReturns = "sale_returns", Payments = "payments", Purchases = "purchases", PurchaseReturns = "purchase_returns",
            Expenses = "expenses", Products = "products", Parties = "parties", Profits = "profits";

        public static readonly KeyValuePair<string, string>[] All =
        {
            new KeyValuePair<string, string>(Admin, "مدير (كل الصلاحيات + المستخدمون والإعدادات)"),
            new KeyValuePair<string, string>(Sell, "فواتير البيع والطباعة"),
            new KeyValuePair<string, string>(Discount, "إعطاء خصم وتغيير سعر البيع بالفاتورة"),
            new KeyValuePair<string, string>(EditInvoices, "تعديل وإلغاء الفواتير المحفوظة والتراجع عنها"),
            new KeyValuePair<string, string>(SaleReturns, "مرتجعات المبيعات"),
            new KeyValuePair<string, string>(Payments, "تسجيل دفعات الزبائن والموردين"),
            new KeyValuePair<string, string>(Purchases, "فواتير المشتريات"),
            new KeyValuePair<string, string>(PurchaseReturns, "مرتجعات المشتريات"),
            new KeyValuePair<string, string>(Expenses, "المصاريف"),
            new KeyValuePair<string, string>(Products, "الأصناف والأسعار والمخزون"),
            new KeyValuePair<string, string>(Parties, "إدارة الزبائن والموردين"),
            new KeyValuePair<string, string>(Profits, "رؤية الأرباح وأسعار التكلفة والتقارير"),
        };

        /// <summary>تأتي مؤشَّرة مسبقاً عند إنشاء مستخدم جديد.</summary>
        public static readonly string[] Defaults = { Sell, SaleReturns, Payments };

        public static string Label(string key)
        {
            foreach (var p in All) if (p.Key == key) return p.Value;
            return key;
        }
    }

    public sealed class AppUser
    {
        public int Id;
        public string Name;
        public bool Active;
        public HashSet<string> Perms = new HashSet<string>();
        public bool IsAdmin { get { return Perms.Contains(Perm.Admin); } }
        public override string ToString() { return Name; }   // يظهر في قائمة الدخول
    }

    /// <summary>المستخدم الذي سجّل الدخول الآن.</summary>
    public static class Session
    {
        public static AppUser User { get; private set; }

        /// <summary>المدير يملك كل الصلاحيات دائماً.</summary>
        public static bool Can(string perm)
        {
            return User != null && (User.IsAdmin || User.Perms.Contains(perm));
        }

        public static void SignIn(AppUser u)
        {
            User = u;
            // يقرؤه trigger قاعدة البيانات ليسجّل من قام بكل بيع/دفعة/مرتجع/مصروف
            Db.Set("session_user", u.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>بعد تعديل المدير لحسابه: يعيد تحميل اسمه وصلاحياته.</summary>
        public static void Refresh()
        {
            if (User == null) return;
            var u = UserStore.Load(User.Id);
            if (u != null) User = u;
        }
    }

    public static class UserStore
    {
        const int Iterations = 100000;

        static string Hash(string pin, byte[] salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(pin, salt, Iterations, HashAlgorithmName.SHA256))
                return Convert.ToBase64String(kdf.GetBytes(32));
        }

        public static bool ValidPin(string pin)
        {
            return pin != null && pin.Length == 4 && pin.All(ch => ch >= '0' && ch <= '9');
        }

        static HashSet<string> Parse(string s)
        {
            return new HashSet<string>((s ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
        }

        static AppUser FromRow(DataRow r)
        {
            return new AppUser
            {
                Id = Convert.ToInt32(r["Id"]),
                Name = Convert.ToString(r["Name"]),
                Active = Convert.ToInt32(r["Active"]) == 1,
                Perms = Parse(Convert.ToString(r["Perms"]))
            };
        }

        public static int Count() { return Convert.ToInt32(Db.Scalar("SELECT COUNT(*) FROM Users")); }

        public static List<AppUser> All(bool activeOnly)
        {
            var list = new List<AppUser>();
            foreach (DataRow r in Db.Query("SELECT Id, Name, Perms, Active FROM Users" + (activeOnly ? " WHERE Active=1" : "") + " ORDER BY Name").Rows)
                list.Add(FromRow(r));
            return list;
        }

        public static AppUser Load(int id)
        {
            var t = Db.Query("SELECT Id, Name, Perms, Active FROM Users WHERE Id=@p0", id);
            return t.Rows.Count == 0 ? null : FromRow(t.Rows[0]);
        }

        public static bool Verify(int id, string pin)
        {
            var t = Db.Query("SELECT PinHash, PinSalt FROM Users WHERE Id=@p0 AND Active=1", id);
            if (t.Rows.Count == 0 || !ValidPin(pin)) return false;
            byte[] salt = Convert.FromBase64String(Convert.ToString(t.Rows[0]["PinSalt"]));
            byte[] want = Convert.FromBase64String(Convert.ToString(t.Rows[0]["PinHash"]));
            byte[] got = Convert.FromBase64String(Hash(pin, salt));
            return CryptographicOperations.FixedTimeEquals(want, got);
        }

        /// <summary>إضافة (id=0) أو تعديل. pin فارغ عند التعديل = يبقى الرمز القديم.</summary>
        public static int Save(int id, string name, string pin, IEnumerable<string> perms)
        {
            string p = string.Join(",", perms.Distinct());
            if (id == 0)
            {
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                Db.Exec("INSERT INTO Users(Name, PinHash, PinSalt, Perms, Active) VALUES(@p0,@p1,@p2,@p3,1)",
                        name, Hash(pin, salt), Convert.ToBase64String(salt), p);
                return Convert.ToInt32(Db.Scalar("SELECT Id FROM Users WHERE Name=@p0", name));
            }
            Db.Exec("UPDATE Users SET Name=@p0, Perms=@p1 WHERE Id=@p2", name, p, id);
            if (!string.IsNullOrEmpty(pin))
            {
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                Db.Exec("UPDATE Users SET PinHash=@p0, PinSalt=@p1 WHERE Id=@p2", Hash(pin, salt), Convert.ToBase64String(salt), id);
            }
            return id;
        }

        /// <summary>عدد المدراء الفعّالين غير المستخدم المستثنى.</summary>
        public static int OtherActiveAdmins(int exceptId)
        {
            return All(true).Count(u => u.IsAdmin && u.Id != exceptId);
        }
    }

    /// <summary>شاشة الدخول: اختيار الاسم + رمز من 4 أرقام. أول مرة (لا مستخدمين) تنشئ حساب المدير.</summary>
    public class LoginForm : Form
    {
        static int failures;
        static DateTime lockedUntil = DateTime.MinValue;

        readonly ListBox lstUsers = new ListBox();
        readonly TextBox txtPin = new TextBox();
        readonly Label lblMsg = new Label();

        /// <summary>يعرض الدخول ويعيد true إذا سجّل مستخدم دخوله.</summary>
        public static bool Run(IWin32Window owner = null)
        {
            if (UserStore.Count() == 0) return CreateFirstAdmin(owner);   // المدير الجديد يدخل مباشرة
            using (var f = new LoginForm())
                return (owner == null ? f.ShowDialog() : f.ShowDialog(owner)) == DialogResult.OK;
        }

        static bool CreateFirstAdmin(IWin32Window owner)
        {
            UI.Info(owner, "أهلاً بك. صار في البرنامج نظام مستخدمين.\nأنشئ الآن حساب المدير (أنت): اسم ورمز دخول من 4 أرقام.\nبعدها تضيف العمال من: الإعدادات ← المستخدمون.");
            using (var d = new UserEditDialog(null, true))
                return d.ShowDialog(owner) == DialogResult.OK;
        }

        LoginForm()
        {
            UI.Rtl(this);
            Text = "تسجيل الدخول - " + Db.Get("shop_name");
            Width = 460; Height = 560;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            var lblU = new Label { Text = "اختر اسمك:", Dock = DockStyle.Top, Height = 34, Padding = new Padding(16, 8, 16, 0) };
            lstUsers.Dock = DockStyle.Fill;
            lstUsers.Font = new Font("Segoe UI", 15);
            lstUsers.IntegralHeight = false;
            foreach (var u in UserStore.All(true)) lstUsers.Items.Add(u);
            if (lstUsers.Items.Count > 0) lstUsers.SelectedIndex = 0;
            lstUsers.DoubleClick += (a, b) => txtPin.Focus();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(16, 6, 16, 10) };
            var lblP = new Label { Text = "رمز الدخول (4 أرقام):", Dock = DockStyle.Top, Height = 32 };
            txtPin.Dock = DockStyle.Top;
            txtPin.Font = new Font("Segoe UI", 20);
            txtPin.MaxLength = 4;
            txtPin.UseSystemPasswordChar = true;
            txtPin.TextAlign = HorizontalAlignment.Center;
            txtPin.KeyPress += (a, b) => { if (!char.IsControl(b.KeyChar) && !char.IsDigit(b.KeyChar)) b.Handled = true; };
            txtPin.KeyDown += (a, b) => { if (b.KeyCode == Keys.Enter) { b.SuppressKeyPress = true; TryLogin(); } };
            lblMsg.Dock = DockStyle.Top; lblMsg.Height = 34; lblMsg.ForeColor = Color.Firebrick; lblMsg.TextAlign = ContentAlignment.MiddleCenter;
            var btn = new Button { Text = "دخول", Dock = DockStyle.Top, Height = 48, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
            btn.Click += (a, b) => TryLogin();
            bottom.Controls.Add(btn);
            bottom.Controls.Add(lblMsg);
            bottom.Controls.Add(txtPin);
            bottom.Controls.Add(lblP);

            var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 0) };
            mid.Controls.Add(lstUsers);
            Controls.Add(mid);
            Controls.Add(bottom);
            Controls.Add(lblU);
            AcceptButton = btn;
            Shown += (a, b) => { Activate(); txtPin.Focus(); };
        }

        void TryLogin()
        {
            if (DateTime.Now < lockedUntil)
            {
                lblMsg.Text = "محاولات خاطئة كثيرة. انتظر " + Math.Ceiling((lockedUntil - DateTime.Now).TotalSeconds) + " ثانية.";
                txtPin.Clear();
                return;
            }
            var u = lstUsers.SelectedItem as AppUser;
            if (u == null) { lblMsg.Text = "اختر اسمك من القائمة."; return; }
            if (!UserStore.Verify(u.Id, txtPin.Text))
            {
                txtPin.Clear(); txtPin.Focus();
                if (++failures >= 5)
                {
                    failures = 0;
                    lockedUntil = DateTime.Now.AddSeconds(30);
                    lblMsg.Text = "5 محاولات خاطئة: الدخول مقفل 30 ثانية.";
                }
                else lblMsg.Text = "الرمز غير صحيح.";
                return;
            }
            failures = 0;
            Session.SignIn(UserStore.Load(u.Id));
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>نافذة إضافة/تعديل مستخدم: الاسم، الرمز، ومربعات اختيار الصلاحيات.</summary>
    public class UserEditDialog : Form
    {
        readonly AppUser user;            // null = مستخدم جديد
        readonly bool firstAdmin;
        readonly TextBox txtName = new TextBox();
        readonly TextBox txtPin = new TextBox();
        readonly TextBox txtPin2 = new TextBox();
        readonly CheckedListBox chk = new CheckedListBox();
        bool syncing;

        public int SavedId { get; private set; }

        public UserEditDialog(AppUser u, bool isFirstAdmin = false)
        {
            user = u; firstAdmin = isFirstAdmin;
            UI.Rtl(this);
            Text = firstAdmin ? "إنشاء حساب المدير" : (u == null ? "إضافة مستخدم" : "تعديل المستخدم: " + u.Name);
            Width = 560; Height = firstAdmin ? 360 : 760;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            if (firstAdmin) { StartPosition = FormStartPosition.CenterScreen; TopMost = true; }

            var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            foreach (var tb in new[] { txtPin, txtPin2 })
            {
                tb.MaxLength = 4; tb.UseSystemPasswordChar = true;
                tb.KeyPress += (a, b) => { if (!char.IsControl(b.KeyChar) && !char.IsDigit(b.KeyChar)) b.Handled = true; };
            }
            if (u != null) { txtName.Text = u.Name; txtPin.PlaceholderText = "(اتركه فارغاً لعدم التغيير)"; }
            AddRow(tl, "اسم المستخدم", txtName);
            AddRow(tl, "رمز الدخول (4 أرقام)", txtPin);
            AddRow(tl, "تأكيد الرمز", txtPin2);

            var ok = new Button { Text = firstAdmin ? "إنشاء الحساب" : "حفظ", Dock = DockStyle.Bottom, Height = 46, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            ok.Click += (a, b) => DoSave();
            Controls.Add(ok);

            if (!firstAdmin)
            {
                var lbl = new Label { Text = "الصلاحيات (أشّر على المسموح لهذا المستخدم):", Dock = DockStyle.Top, Height = 34, Padding = new Padding(16, 6, 16, 0), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
                chk.Dock = DockStyle.Fill;
                chk.CheckOnClick = true;
                chk.IntegralHeight = false;
                chk.Font = new Font("Segoe UI", 12);
                foreach (var p in Perm.All)
                {
                    bool on = u == null ? Perm.Defaults.Contains(p.Key) : u.Perms.Contains(p.Key);
                    chk.Items.Add(p.Value, on);
                }
                chk.ItemCheck += OnItemCheck;
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 8) };
                pad.Controls.Add(chk);
                Controls.Add(pad);
                Controls.Add(lbl);
            }
            Controls.Add(tl);
            AcceptButton = ok;
        }

        static void AddRow(TableLayoutPanel tl, string label, Control c)
        {
            tl.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 10) });
            c.Dock = DockStyle.Fill; c.Margin = new Padding(3, 6, 3, 6);
            tl.Controls.Add(c);
        }

        /// <summary>تأشير "مدير" يؤشّر الكل؛ إلغاء أي صلاحية يلغي "مدير".</summary>
        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (syncing) return;
            syncing = true;
            try
            {
                if (e.Index == 0 && e.NewValue == CheckState.Checked)
                    for (int i = 1; i < chk.Items.Count; i++) chk.SetItemChecked(i, true);
                else if (e.Index > 0 && e.NewValue == CheckState.Unchecked && chk.GetItemChecked(0))
                    chk.SetItemChecked(0, false);
            }
            finally { syncing = false; }
        }

        List<string> SelectedPerms()
        {
            var list = new List<string>();
            for (int i = 0; i < chk.Items.Count; i++)
                if (chk.GetItemChecked(i)) list.Add(Perm.All[i].Key);
            return list;
        }

        void DoSave()
        {
            string name = txtName.Text.Trim(), pin = txtPin.Text, pin2 = txtPin2.Text;
            if (name == "") { UI.Warn(this, "اكتب اسم المستخدم."); return; }
            object other = Db.Scalar("SELECT Id FROM Users WHERE Name=@p0 AND Id<>@p1", name, user == null ? 0 : user.Id);
            if (other != null && !(other is DBNull)) { UI.Warn(this, "يوجد مستخدم آخر بهذا الاسم."); return; }
            bool pinRequired = user == null;
            if (pinRequired || pin != "" || pin2 != "")
            {
                if (!UserStore.ValidPin(pin)) { UI.Warn(this, "رمز الدخول يجب أن يكون 4 أرقام بالضبط."); return; }
                if (pin != pin2) { UI.Warn(this, "الرمزان غير متطابقين."); return; }
            }
            var perms = firstAdmin ? new List<string> { Perm.Admin } : SelectedPerms();
            if (perms.Contains(Perm.Admin)) perms = new List<string> { Perm.Admin };   // المدير يملك الكل ضمناً
            if (user != null && user.IsAdmin && !perms.Contains(Perm.Admin) && UserStore.OtherActiveAdmins(user.Id) == 0)
            {
                UI.Warn(this, "هذا هو المدير الوحيد. لا يمكن إزالة صلاحية المدير منه قبل إضافة مدير آخر.");
                return;
            }
            if (!firstAdmin && perms.Count == 0) { UI.Warn(this, "أشّر على صلاحية واحدة على الأقل."); return; }
            SavedId = UserStore.Save(user == null ? 0 : user.Id, name, pin, perms);
            if (firstAdmin) Session.SignIn(UserStore.Load(SavedId));
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>شاشة المستخدمين (للمدير فقط): إضافة، تعديل الصلاحيات والرمز، تعطيل/تفعيل.</summary>
    public class UsersForm : Form
    {
        readonly DataGridView grid = new DataGridView();
        readonly UndoStack undo = new UndoStack();

        public UsersForm()
        {
            UI.Rtl(this);
            Text = "المستخدمون والصلاحيات";
            Width = 1000; Height = 620;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            KeyDown += (a, b) => { if (b.Control && b.KeyCode == Keys.Z) { b.Handled = true; DoUndo(); } };

            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.CellDoubleClick += (a, b) => { if (b.RowIndex >= 0) EditCurrent(); };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            bar.Controls.Add(Btn("إضافة مستخدم", (a, b) => Add()));
            bar.Controls.Add(Btn("تعديل الصلاحيات / الرمز", (a, b) => EditCurrent()));
            bar.Controls.Add(Btn("تعطيل / تفعيل", (a, b) => ToggleActive()));
            bar.Controls.Add(UndoStack.MakeButton(DoUndo));

            Controls.Add(grid);
            Controls.Add(bar);
            Reload();
        }

        static Button Btn(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(6, 4, 4, 0) };
            b.Click += h;
            return b;
        }

        void Reload()
        {
            var t = new DataTable();
            t.Columns.Add("Id", typeof(int));
            t.Columns.Add("الاسم", typeof(string));
            t.Columns.Add("الحالة", typeof(string));
            t.Columns.Add("الصلاحيات", typeof(string));
            foreach (var u in UserStore.All(false))
                t.Rows.Add(u.Id, u.Name, u.Active ? "فعّال" : "معطّل",
                           u.IsAdmin ? "مدير (كل الصلاحيات)" : string.Join("، ", Perm.All.Where(p => u.Perms.Contains(p.Key)).Select(p => p.Value)));
            grid.DataSource = t;
            grid.Columns["Id"].Visible = false;
            grid.Columns["الاسم"].FillWeight = 20;
            grid.Columns["الحالة"].FillWeight = 10;
            grid.Columns["الصلاحيات"].FillWeight = 70;
        }

        AppUser Current()
        {
            if (grid.CurrentRow == null) return null;
            return UserStore.Load(Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value));
        }

        void Add()
        {
            var ch = Db.TableChange.Begin("Users");
            using (var d = new UserEditDialog(null))
                if (d.ShowDialog(this) != DialogResult.OK) return;
            undo.Push("إضافة مستخدم", ch.End());
            Reload();
        }

        void EditCurrent()
        {
            var u = Current();
            if (u == null) return;
            var ch = Db.TableChange.Begin("Users");
            using (var d = new UserEditDialog(u))
                if (d.ShowDialog(this) != DialogResult.OK) return;
            undo.Push("تعديل " + u.Name, ch.End());
            Session.Refresh();
            Reload();
        }

        void ToggleActive()
        {
            var u = Current();
            if (u == null) return;
            if (u.Active && u.IsAdmin && UserStore.OtherActiveAdmins(u.Id) == 0) { UI.Warn(this, "لا يمكن تعطيل المدير الوحيد."); return; }
            if (u.Active && Session.User != null && u.Id == Session.User.Id) { UI.Warn(this, "لا يمكنك تعطيل حسابك وأنت داخل به."); return; }
            if (!UI.Confirm(this, (u.Active ? "تعطيل" : "تفعيل") + " المستخدم " + u.Name + "؟")) return;
            var ch = Db.TableChange.Begin("Users");
            Db.Exec("UPDATE Users SET Active=@p0 WHERE Id=@p1", u.Active ? 0 : 1, u.Id);
            undo.Push((u.Active ? "تعطيل " : "تفعيل ") + u.Name, ch.End());
            Reload();
        }

        void DoUndo()
        {
            string what = undo.Undo();
            if (what == null) { UI.Info(this, "لا يوجد ما يمكن التراجع عنه."); return; }
            // التراجع لا يجوز أن يترك البرنامج بلا مدير فعّال
            if (UserStore.OtherActiveAdmins(0) == 0)
                UI.Warn(this, "تنبيه: لا يوجد الآن مدير فعّال. أضف مديراً أو فعّل واحداً قبل الإغلاق.");
            Session.Refresh();
            Reload();
        }
    }
}
