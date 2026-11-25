using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;

namespace ShopManager
{
    /// <summary>الوصول لقاعدة البيانات (SQLite): ملف shop.db بجانب البرنامج.</summary>
    public static class Db
    {
        public static readonly string DbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shop.db");

        public static SQLiteConnection Open()
        {
            var c = new SQLiteConnection("Data Source=" + DbPath + ";Version=3;");
            c.Open();
            return c;
        }

        public static SQLiteCommand Cmd(SQLiteConnection c, string sql, object[] p, SQLiteTransaction tx = null)
        {
            var cmd = new SQLiteCommand(sql, c, tx);
            for (int i = 0; i < p.Length; i++)
                cmd.Parameters.AddWithValue("@p" + i, p[i] == null ? DBNull.Value : p[i]);
            return cmd;
        }

        public static DataTable Query(string sql, params object[] p)
        {
            using (var c = Open())
            using (var cmd = Cmd(c, sql, p))
            using (var r = cmd.ExecuteReader())
            {
                var t = new DataTable();
                t.Load(r);
                return t;
            }
        }

        public static object Scalar(string sql, params object[] p)
        {
            using (var c = Open())
            using (var cmd = Cmd(c, sql, p))
                return cmd.ExecuteScalar();
        }

        public static int Exec(string sql, params object[] p)
        {
            using (var c = Open())
            using (var cmd = Cmd(c, sql, p))
                return cmd.ExecuteNonQuery();
        }

        public static int ExecTx(SQLiteConnection c, SQLiteTransaction tx, string sql, params object[] p)
        {
            using (var cmd = Cmd(c, sql, p, tx))
                return cmd.ExecuteNonQuery();
        }

        public static object ScalarTx(SQLiteConnection c, SQLiteTransaction tx, string sql, params object[] p)
        {
            using (var cmd = Cmd(c, sql, p, tx))
                return cmd.ExecuteScalar();
        }

        public static long LastId(SQLiteConnection c, SQLiteTransaction tx)
        {
            return Convert.ToInt64(ScalarTx(c, tx, "SELECT last_insert_rowid()"));
        }

        // ---------- تحويل الأرقام ----------
        public static double D(object o)
        {
            if (o == null || o is DBNull) return 0;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
            catch { return Num(Convert.ToString(o)); }
        }

        /// <summary>يقرأ رقماً من نص المستخدم (يقبل الأرقام العربية والفواصل).</summary>
        public static double Num(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            var sb = new StringBuilder();
            foreach (char ch in s.Trim())
            {
                if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
                else if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
                else if (ch == '\u066B') sb.Append('.');
                else if (ch == ',' || ch == '\u066C' || ch == ' ' || ch == '\u00A0') { }
                else sb.Append(ch);
            }
            double v;
            return double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        // ---------- الإعدادات ----------
        public static string Get(string key, string def = "")
        {
            var o = Scalar("SELECT V FROM Settings WHERE K=@p0", key);
            return (o == null || o is DBNull) ? def : Convert.ToString(o);
        }

        public static void Set(string key, string val)
        {
            Exec("INSERT OR REPLACE INTO Settings(K,V) VALUES(@p0,@p1)", key, val);
        }

        /// <summary>سعر صرف الدولار بالليرة.</summary>
        public static double Rate
        {
            get { double r = Num(Get("rate", "151")); return r <= 0 ? 1 : r; }
        }


        // ---------- التسعير التلقائي ----------
        /// <summary>سعر البيع بالليرة = تقريب لأقرب 5 من (التكلفة × (1+نسبة الربح) × سعر الصرف إن كانت بالدولار).</summary>
        public static double AutoPrice(double cost, string cur, double margin, double rate)
        {
            double v = cost * (1 + margin) * (cur == "USD" ? rate : 1);
            return Math.Floor(v / 5 + 0.5) * 5;
        }

        /// <summary>
        /// يعيد تسعير الأصناف المشتراة بالدولار ذات نسبة الربح بعد تغيير سعر الصرف. يعيد عدد الأصناف المحدّثة.
        /// أصناف الليرة لا يؤثر عليها سعر الصرف، فلا تُمسّ (كي لا تُقرَّب أسعارها اليدوية لأقرب 5).
        /// </summary>
        public static int RecalcPrices(double rate)
        {
            return Exec("UPDATE Products SET SellPrice = ROUND(CostPrice*(1+Margin)*@p0/5.0)*5 WHERE CostCurrency='USD' AND IFNULL(Margin,-1)>=0 AND IFNULL(CostPrice,0)>0", rate);
        }

        // ---------- لقطات الجداول للتراجع ----------
        public static DataTable Snapshot(string table)
        {
            return Query("SELECT * FROM " + table);
        }

        public static void InsertRow(SQLiteConnection c, SQLiteTransaction tx, string table, DataRow r)
        {
            var names = new System.Collections.Generic.List<string>();
            var ps = new System.Collections.Generic.List<string>();
            var vals = new System.Collections.Generic.List<object>();
            for (int i = 0; i < r.Table.Columns.Count; i++)
            {
                names.Add(r.Table.Columns[i].ColumnName);
                ps.Add("@p" + i);
                vals.Add(r[i] is DBNull ? null : r[i]);
            }
            ExecTx(c, tx, "INSERT INTO " + table + "(" + string.Join(",", names) + ") VALUES(" + string.Join(",", ps) + ")", vals.ToArray());
        }

        public static void Restore(string table, DataTable snap)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                ExecTx(c, tx, "DELETE FROM " + table);
                foreach (DataRow r in snap.Rows) InsertRow(c, tx, table, r);
                tx.Commit();
            }
        }

        /// <summary>
        /// تراجع دقيق: لقطة قبل الإجراء وأخرى بعده، والتراجع يعكس الفرق بينهما فقط
        /// (الصفوف والأعمدة التي غيّرها هذا الإجراء)، فلا يمسّ ما حدث بعده من بيع أو إضافة في نوافذ أخرى.
        /// الاستخدام: var ch = Db.TableChange.Begin("Products"); ...الإجراء...; undo.Push("...", ch.End());
        /// </summary>
        public sealed class TableChange
        {
            readonly string table;
            readonly DataTable before;
            TableChange(string table) { this.table = table; before = Snapshot(table); }

            public static TableChange Begin(string table) { return new TableChange(table); }

            /// <summary>يلتقط الحالة بعد الإجراء ويعيد عملية التراجع.</summary>
            public Action End()
            {
                DataTable after = Snapshot(table);
                string t = table; DataTable b = before;
                return () => Revert(t, b, after);
            }
        }

        static string KeyOf(DataTable t) { return t.Columns.Contains("Id") ? "Id" : "K"; }

        static Dictionary<string, DataRow> Index(DataTable t, string key)
        {
            var d = new Dictionary<string, DataRow>();
            foreach (DataRow r in t.Rows) d[Convert.ToString(r[key], CultureInfo.InvariantCulture)] = r;
            return d;
        }

        /// <summary>يعكس الفرق بين لقطتين: يحذف ما أُضيف، ويعيد ما حُذف، ويرجع قيم الأعمدة التي تغيّرت فقط.</summary>
        public static void Revert(string table, DataTable before, DataTable after)
        {
            string key = KeyOf(before);
            var b = Index(before, key);
            var a = Index(after, key);
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    foreach (var kv in a)
                        if (!b.ContainsKey(kv.Key))
                            ExecTx(c, tx, "DELETE FROM " + table + " WHERE " + key + "=@p0", kv.Value[key]);
                    foreach (var kv in b)
                    {
                        DataRow br = kv.Value;
                        DataRow ar;
                        if (!a.TryGetValue(kv.Key, out ar))
                        {
                            // أُعيد إدراجه فقط إن لم يُنشأ صف بنفس المفتاح لاحقاً
                            if (ScalarTx(c, tx, "SELECT 1 FROM " + table + " WHERE " + key + "=@p0", br[key]) == null)
                                InsertRow(c, tx, table, br);
                            continue;
                        }
                        foreach (DataColumn col in before.Columns)
                        {
                            if (col.ColumnName == key) continue;
                            if (Equals(br[col.ColumnName], ar[col.ColumnName])) continue;
                            ExecTx(c, tx, "UPDATE " + table + " SET " + col.ColumnName + "=@p0 WHERE " + key + "=@p1",
                                br[col.ColumnName] is DBNull ? null : br[col.ColumnName], br[key]);
                        }
                    }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
        }

        // ---------- إلغاء فاتورة وإعادتها ----------
        public static void CancelInvoice(bool sale, int id)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    long returns = Convert.ToInt64(ScalarTx(c, tx, sale ? "SELECT COUNT(*) FROM SaleReturns WHERE SaleId=@p0" : "SELECT COUNT(*) FROM PurchaseReturns WHERE PurchaseId=@p0", id));
                    if (returns > 0)
                        throw new InvalidOperationException("على هذه الفاتورة مرتجعات. ألغِ المرتجع من صفحة المرتجعات أولاً ثم ألغِ الفاتورة.");
                    var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, double>>();
                    string sel = sale ? "SELECT ProductId, Qty FROM SaleItems WHERE SaleId=@p0" : "SELECT ProductId, Qty FROM PurchaseItems WHERE PurchaseId=@p0";
                    using (var cmd = Cmd(c, sel, new object[] { id }, tx))
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read()) items.Add(new System.Collections.Generic.KeyValuePair<int, double>(Convert.ToInt32(rd[0]), D(rd[1])));
                    }
                    foreach (var it in items)
                        ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)" + (sale ? "+" : "-") + "@p0 WHERE Id=@p1", it.Value, it.Key);
                    if (sale) { ExecTx(c, tx, "DELETE FROM SaleItems WHERE SaleId=@p0", id); ExecTx(c, tx, "DELETE FROM Sales WHERE Id=@p0", id); }
                    else { ExecTx(c, tx, "DELETE FROM PurchaseItems WHERE PurchaseId=@p0", id); ExecTx(c, tx, "DELETE FROM Purchases WHERE Id=@p0", id); }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
        }

        public static void RestoreInvoice(bool sale, DataTable hdr, DataTable items)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    foreach (DataRow r in hdr.Rows) InsertRow(c, tx, sale ? "Sales" : "Purchases", r);
                    foreach (DataRow r in items.Rows)
                    {
                        InsertRow(c, tx, sale ? "SaleItems" : "PurchaseItems", r);
                        double q = D(r["Qty"]);
                        ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)" + (sale ? "-" : "+") + "@p0 WHERE Id=@p1", q, Convert.ToInt32(r["ProductId"]));
                    }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
        }


        // ---------- ديون الزبائن: الحد والتجميد ----------
        /// <summary>حدّ الدين (يوضع بجانبه إشارة تحذير). الافتراضي 200,000.</summary>
        public static double DebtLimit
        {
            get { double v = Num(Get("debt_limit", "200000")); return v <= 0 ? 200000 : v; }
        }

        /// <summary>عدد الأيام بلا حركة قبل تجميد الدين. الافتراضي 20.</summary>
        public static int FreezeDays
        {
            get { int v = (int)Num(Get("freeze_days", "20")); return v <= 0 ? 20 : v; }
        }

        public static double CustomerBalance(int customerId)
        {
            return D(Scalar("SELECT " + Sql.CustBal("@p0"), customerId));
        }

        public static void Unfreeze(int customerId)
        {
            Exec("UPDATE Customers SET Frozen=0, FrozenDate=NULL WHERE Id=@p0 AND IFNULL(Frozen,0)=1", customerId);
        }

        /// <summary>يجمّد ديون الزبائن التي مضى عليها أكثر من N يوم بلا حركة، ويعيدها (للتنبيه).</summary>
        public static DataTable FreezeStaleDebts()
        {
            DataTable t = Query(Sql.StaleDebts(), "-" + FreezeDays + " day");
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (DataRow r in t.Rows)
                Exec("UPDATE Customers SET Frozen=1, FrozenDate=@p1 WHERE Id=@p0", Convert.ToInt32(r["Id"]), today);
            return t;
        }

        // ---------- المرتجعات ----------
        public static void CancelReturn(bool sale, long id)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, double>>();
                    string sel = sale ? "SELECT ProductId, Qty FROM SaleReturnItems WHERE ReturnId=@p0" : "SELECT ProductId, Qty FROM PurchaseReturnItems WHERE ReturnId=@p0";
                    using (var cmd = Cmd(c, sel, new object[] { id }, tx))
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read()) items.Add(new System.Collections.Generic.KeyValuePair<int, double>(Convert.ToInt32(rd[0]), D(rd[1])));
                    }
                    foreach (var it in items)
                        ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)" + (sale ? "-" : "+") + "@p0 WHERE Id=@p1", it.Value, it.Key);
                    if (sale) { ExecTx(c, tx, "DELETE FROM SaleReturnItems WHERE ReturnId=@p0", id); ExecTx(c, tx, "DELETE FROM SaleReturns WHERE Id=@p0", id); }
                    else { ExecTx(c, tx, "DELETE FROM PurchaseReturnItems WHERE ReturnId=@p0", id); ExecTx(c, tx, "DELETE FROM PurchaseReturns WHERE Id=@p0", id); }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
        }

        public static void RestoreReturn(bool sale, DataTable hdr, DataTable items)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    foreach (DataRow r in hdr.Rows) InsertRow(c, tx, sale ? "SaleReturns" : "PurchaseReturns", r);
                    foreach (DataRow r in items.Rows)
                    {
                        InsertRow(c, tx, sale ? "SaleReturnItems" : "PurchaseReturnItems", r);
                        ExecTx(c, tx, "UPDATE Products SET Stock=IFNULL(Stock,0)" + (sale ? "+" : "-") + "@p0 WHERE Id=@p1", D(r["Qty"]), Convert.ToInt32(r["ProductId"]));
                    }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
        }

        // ---------- إنشاء الجداول ----------
        public static void Init()
        {
            string[] ddl =
            {
                "CREATE TABLE IF NOT EXISTS Settings(K TEXT PRIMARY KEY, V TEXT)",
                "CREATE TABLE IF NOT EXISTS Products(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Barcode TEXT, Category TEXT, CostPrice REAL DEFAULT 0, CostCurrency TEXT DEFAULT 'SYP', SellPrice REAL DEFAULT 0, Stock REAL DEFAULT 0, MinStock REAL DEFAULT 0, Margin REAL DEFAULT -1)",
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_prod_barcode ON Products(Barcode) WHERE Barcode IS NOT NULL AND Barcode<>''",
                "CREATE TABLE IF NOT EXISTS Customers(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Phone TEXT, Frozen INTEGER DEFAULT 0, FrozenDate TEXT, OpeningBalance REAL DEFAULT 0, OpeningDate TEXT)",
                "CREATE TABLE IF NOT EXISTS Suppliers(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Phone TEXT)",
                "CREATE TABLE IF NOT EXISTS Sales(Id INTEGER PRIMARY KEY AUTOINCREMENT, Date TEXT, CustomerId INTEGER, Total REAL, Discount REAL, Net REAL, Paid REAL, Remaining REAL, PrevBalance REAL DEFAULT 0)",
                "CREATE TABLE IF NOT EXISTS SaleItems(Id INTEGER PRIMARY KEY AUTOINCREMENT, SaleId INTEGER, ProductId INTEGER, Name TEXT, Qty REAL, UnitPrice REAL, UnitCost REAL)",
                "CREATE TABLE IF NOT EXISTS Purchases(Id INTEGER PRIMARY KEY AUTOINCREMENT, Date TEXT, SupplierId INTEGER, InvoiceNo TEXT, Currency TEXT, Rate REAL, Total REAL, Discount REAL, Net REAL, Paid REAL, Remaining REAL, NetSYP REAL, PaidSYP REAL, RemainingSYP REAL)",
                "CREATE TABLE IF NOT EXISTS PurchaseItems(Id INTEGER PRIMARY KEY AUTOINCREMENT, PurchaseId INTEGER, ProductId INTEGER, Qty REAL, UnitCost REAL)",
                "CREATE TABLE IF NOT EXISTS Expenses(Id INTEGER PRIMARY KEY AUTOINCREMENT, Date TEXT, Description TEXT, Category TEXT, Amount REAL DEFAULT 0)",
                "CREATE TABLE IF NOT EXISTS ProductAliases(Id INTEGER PRIMARY KEY AUTOINCREMENT, Alias TEXT NOT NULL, Norm TEXT NOT NULL UNIQUE, ProductId INTEGER)",
                "CREATE TABLE IF NOT EXISTS SaleReturns(Id INTEGER PRIMARY KEY AUTOINCREMENT, Date TEXT, SaleId INTEGER, CustomerId INTEGER, Total REAL, DebtReduction REAL, RefundCash REAL)",
                "CREATE TABLE IF NOT EXISTS SaleReturnItems(Id INTEGER PRIMARY KEY AUTOINCREMENT, ReturnId INTEGER, ProductId INTEGER, Name TEXT, Qty REAL, UnitPrice REAL, UnitCost REAL)",
                "CREATE TABLE IF NOT EXISTS PurchaseReturns(Id INTEGER PRIMARY KEY AUTOINCREMENT, Date TEXT, PurchaseId INTEGER, SupplierId INTEGER, Currency TEXT, Rate REAL, Total REAL, DebtReduction REAL, RefundCash REAL)",
                "CREATE TABLE IF NOT EXISTS PurchaseReturnItems(Id INTEGER PRIMARY KEY AUTOINCREMENT, ReturnId INTEGER, ProductId INTEGER, Qty REAL, UnitCost REAL)",
                "CREATE TABLE IF NOT EXISTS Payments(Id INTEGER PRIMARY KEY AUTOINCREMENT, Date TEXT, PartyType TEXT, PartyId INTEGER, Amount REAL, Note TEXT, Currency TEXT DEFAULT 'SYP', Rate REAL DEFAULT 1)",
                "CREATE TABLE IF NOT EXISTS Shifts(Id INTEGER PRIMARY KEY AUTOINCREMENT, Machine TEXT, StartAt TEXT, EndAt TEXT, Invoices INTEGER, NetSales REAL, CashExpected REAL, CashCounted REAL)",
                "CREATE TABLE IF NOT EXISTS Users(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, PinHash TEXT, PinSalt TEXT, Perms TEXT DEFAULT '', Active INTEGER DEFAULT 1)",
                // فهارس لتسريع حساب الأرصدة والفواتير مع كبر البيانات
                "CREATE INDEX IF NOT EXISTS ix_sales_customer ON Sales(CustomerId)",
                "CREATE INDEX IF NOT EXISTS ix_sales_date ON Sales(Date)",
                "CREATE INDEX IF NOT EXISTS ix_saleitems_sale ON SaleItems(SaleId)",
                "CREATE INDEX IF NOT EXISTS ix_purchases_supplier ON Purchases(SupplierId)",
                "CREATE INDEX IF NOT EXISTS ix_purchaseitems_purchase ON PurchaseItems(PurchaseId)",
                "CREATE INDEX IF NOT EXISTS ix_payments_party ON Payments(PartyType, PartyId)",
                "CREATE INDEX IF NOT EXISTS ix_salereturns_sale ON SaleReturns(SaleId)",
                "CREATE INDEX IF NOT EXISTS ix_salereturns_customer ON SaleReturns(CustomerId)",
                "CREATE INDEX IF NOT EXISTS ix_salereturnitems_ret ON SaleReturnItems(ReturnId)",
                "CREATE INDEX IF NOT EXISTS ix_purchasereturns_purchase ON PurchaseReturns(PurchaseId)",
                "CREATE INDEX IF NOT EXISTS ix_purchasereturns_supplier ON PurchaseReturns(SupplierId)",
                "CREATE INDEX IF NOT EXISTS ix_purchasereturnitems_ret ON PurchaseReturnItems(ReturnId)"
            };
            foreach (var s in ddl) Exec(s);

            // ترحيل: قاعدة بيانات قديمة بلا عمودي العملة وسعر الصرف في الدفعات
            bool hasCur = false, hasRate = false;
            foreach (DataRow r in Query("PRAGMA table_info(Payments)").Rows)
            {
                string n = Convert.ToString(r["name"]);
                if (n == "Currency") hasCur = true;
                if (n == "Rate") hasRate = true;
            }
            bool hasMargin = false;
            foreach (DataRow r in Query("PRAGMA table_info(Products)").Rows)
                if (Convert.ToString(r["name"]) == "Margin") hasMargin = true;
            bool hasFrozen = false;
            foreach (DataRow r in Query("PRAGMA table_info(Customers)").Rows)
                if (Convert.ToString(r["name"]) == "Frozen") hasFrozen = true;
            if (!hasFrozen)
            {
                Exec("ALTER TABLE Customers ADD COLUMN Frozen INTEGER DEFAULT 0");
                Exec("ALTER TABLE Customers ADD COLUMN FrozenDate TEXT");
            }
            bool hasOpening = false;
            foreach (DataRow r in Query("PRAGMA table_info(Customers)").Rows)
                if (Convert.ToString(r["name"]) == "OpeningBalance") hasOpening = true;
            if (!hasOpening)
            {
                Exec("ALTER TABLE Customers ADD COLUMN OpeningBalance REAL DEFAULT 0");
                Exec("ALTER TABLE Customers ADD COLUMN OpeningDate TEXT");
            }
            bool hasPrev = false;
            foreach (DataRow r in Query("PRAGMA table_info(Sales)").Rows)
                if (Convert.ToString(r["name"]) == "PrevBalance") hasPrev = true;
            if (!hasPrev) Exec("ALTER TABLE Sales ADD COLUMN PrevBalance REAL DEFAULT 0");
            if (!hasMargin) Exec("ALTER TABLE Products ADD COLUMN Margin REAL DEFAULT -1");
            if (!hasCur) Exec("ALTER TABLE Payments ADD COLUMN Currency TEXT DEFAULT 'SYP'");
            if (!hasRate) Exec("ALTER TABLE Payments ADD COLUMN Rate REAL DEFAULT 1");

            // من قام بالعملية: عمود UserId يُملأ تلقائياً بالمستخدم الحالي (Settings.session_user) عند الإضافة.
            // الشرط "NEW.UserId IS NULL" يحفظ المستخدم الأصلي عند إعادة إدراج صف بالتراجع.
            foreach (var t in new[] { "Sales", "SaleReturns", "Payments", "Purchases", "PurchaseReturns", "Expenses", "Shifts" })
            {
                bool hasUser = false;
                foreach (DataRow r in Query("PRAGMA table_info(" + t + ")").Rows)
                    if (Convert.ToString(r["name"]) == "UserId") hasUser = true;
                if (!hasUser) Exec("ALTER TABLE " + t + " ADD COLUMN UserId INTEGER");
                Exec("CREATE TRIGGER IF NOT EXISTS tr_" + t + "_user AFTER INSERT ON " + t + " WHEN NEW.UserId IS NULL BEGIN " +
                     "UPDATE " + t + " SET UserId=(SELECT CAST(V AS INTEGER) FROM Settings WHERE K='session_user') WHERE Id=NEW.Id; END");
            }

            Exec("INSERT OR IGNORE INTO Settings(K,V) VALUES('shop_name','اسم المحل')");
            Exec("INSERT OR IGNORE INTO Settings(K,V) VALUES('shop_phone','')");
            Exec("INSERT OR IGNORE INTO Settings(K,V) VALUES('rate','151')");
            Exec("INSERT OR IGNORE INTO Settings(K,V) VALUES('opening_cash','0')");
            Exec("INSERT OR IGNORE INTO Settings(K,V) VALUES('opening_capital','0')");
            Exec("INSERT OR IGNORE INTO Settings(K,V) VALUES('debt_limit','200000')");
            Exec("INSERT OR IGNORE INTO Settings(K,V) VALUES('freeze_days','20')");
        }
    }
}
