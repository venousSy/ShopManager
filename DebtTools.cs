using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>الفواتير التي بقي منها مبلغ على الزبون (الدين) مع رابط لفتح كل فاتورة وطباعتها.
    /// الدفعات والمرتجعات تُوزَّع على أقدم فاتورة أولاً.</summary>
    public class DebtInvoicesForm : Form
    {
        readonly DataGridView grid = new DataGridView();
        readonly int custId;
        readonly string custName;

        public DebtInvoicesForm(int customerId, string name)
        {
            custId = customerId;
            custName = name;
            UI.Rtl(this);
            Text = "فواتير الدين: " + name;
            Width = 1000; Height = 640;
            StartPosition = FormStartPosition.CenterParent;

            UI.SetupGrid(grid, true);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.CellDoubleClick += (a, b) => { if (b.RowIndex >= 0) OpenDetails(); };

            DataTable inv = Db.Query(
                "SELECT Id, substr(Date,1,16) AS d, substr(Date,1,10) AS day, Net, Paid, Remaining FROM Sales " +
                "WHERE CustomerId=@p0 AND Remaining>0 ORDER BY Date, Id", customerId);
            double pool = Db.D(Db.Scalar("SELECT IFNULL(SUM(Amount),0) FROM Payments WHERE PartyType='C' AND PartyId=@p0", customerId))
                        + Db.D(Db.Scalar("SELECT IFNULL(SUM(DebtReduction),0) FROM SaleReturns WHERE CustomerId=@p0", customerId));

            double opening = Db.D(Db.Scalar("SELECT IFNULL(OpeningBalance,0) FROM Customers WHERE Id=@p0", customerId));
            string openDate = Convert.ToString(Db.Scalar("SELECT IFNULL(OpeningDate,'') FROM Customers WHERE Id=@p0", customerId));

            var t = new DataTable();
            t.Columns.Add("رقم الفاتورة", typeof(long));
            t.Columns.Add("التاريخ", typeof(string));
            t.Columns.Add("صافي الفاتورة", typeof(double));
            t.Columns.Add("المدفوع عند البيع", typeof(double));
            t.Columns.Add("المتبقي الأصلي", typeof(double));
            t.Columns.Add("المتبقي الآن", typeof(double));
            t.Columns.Add("عمر الدين (يوم)", typeof(int));
            double total = 0;
            if (opening > 0)
            {
                double take0 = Math.Min(pool, opening);
                pool -= take0;
                double now0 = opening - take0;
                if (now0 > 0.0001)
                {
                    DateTime d0;
                    int days0 = DateTime.TryParse(openDate, out d0) ? (int)(DateTime.Today - d0).TotalDays : 0;
                    t.Rows.Add(0L, "رصيد سابق مستورد " + openDate, opening, 0.0, opening, now0, days0);
                    total += now0;
                }
            }
            foreach (DataRow r in inv.Rows)
            {
                double rem = Db.D(r["Remaining"]);
                double take = Math.Min(pool, rem);
                pool -= take;
                double now = rem - take;
                if (now <= 0.0001) continue;
                DateTime dt;
                int days = DateTime.TryParse(Convert.ToString(r["day"]), out dt) ? (int)(DateTime.Today - dt).TotalDays : 0;
                t.Rows.Add(Convert.ToInt64(r["Id"]), Convert.ToString(r["d"]), Db.D(r["Net"]), Db.D(r["Paid"]), rem, now, days);
                total += now;
            }
            grid.DataSource = t;
            foreach (DataGridViewColumn c in grid.Columns)
                if (c.ValueType == typeof(double)) { c.DefaultCellStyle.Format = "N0"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; }

            var head = new Label
            {
                Dock = DockStyle.Top, Height = 54, Font = new Font("Segoe UI", 13, FontStyle.Bold), Padding = new Padding(10, 12, 0, 0),
                Text = name + "   |   إجمالي المتبقي: " + total.ToString("N0") + " ل.س   |   نقرتان على الفاتورة لفتح تفاصيلها"
            };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            var b1 = new Button { Text = "فتح تفاصيل الفاتورة", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            b1.Click += (a, b) => OpenDetails();
            var b2 = new Button { Text = "طباعة الفاتورة", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            b2.Click += (a, b) => { int id; if (CurrentId(out id) && id > 0) InvoicePrinter.ShowSale(id); };
            bar.Controls.Add(b1);
            bar.Controls.Add(b2);

            Controls.Add(grid);
            Controls.Add(bar);
            Controls.Add(head);
        }

        bool CurrentId(out int id)
        {
            id = 0;
            if (grid.CurrentRow == null) { UI.Warn(this, "اختر فاتورة أولاً."); return false; }
            id = Convert.ToInt32(grid.CurrentRow.Cells[0].Value);
            return true;
        }

        void OpenDetails()
        {
            int id;
            if (!CurrentId(out id)) return;
            if (id == 0) { UI.Info(this, "هذا رصيد سابق مستورد من دفترك القديم ولا توجد له فاتورة."); return; }
            string head = "فاتورة بيع رقم " + id + "   |   المتبقي الآن: " + Db.D(grid.CurrentRow.Cells["المتبقي الآن"].Value).ToString("N0") + " ل.س";
            new InvoiceDetailsForm(true, id, head).Show();
        }
    }
}
