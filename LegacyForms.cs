using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>تفاصيل مرتجع مبيعات أو مشتريات: رأس المرتجع وأصنافه.</summary>
    public class ReturnDetailsForm : Form
    {
        public ReturnDetailsForm(bool sale, long id)
        {
            UI.Rtl(this);
            Text = sale ? "تفاصيل مرتجع مبيعات" : "تفاصيل مرتجع مشتريات";
            Width = 960; Height = 600;

            DataTable h = Db.Query(sale
                ? "SELECT r.Id, r.Date, r.SaleId AS InvId, IFNULL(c.Name,'نقدي') AS PName, 'SYP' AS Cur, r.Total, r.DebtReduction, r.RefundCash FROM SaleReturns r LEFT JOIN Customers c ON c.Id=r.CustomerId WHERE r.Id=@p0"
                : "SELECT r.Id, r.Date, r.PurchaseId AS InvId, IFNULL(s.Name,'-') AS PName, IFNULL(r.Currency,'SYP') AS Cur, r.Total, r.DebtReduction, r.RefundCash FROM PurchaseReturns r LEFT JOIN Suppliers s ON s.Id=r.SupplierId WHERE r.Id=@p0", id);
            DataTable items = Db.Query(sale
                ? "SELECT Name AS \"الصنف\", Qty AS \"الكمية\", UnitPrice AS \"السعر\", Qty*UnitPrice AS \"القيمة\" FROM SaleReturnItems WHERE ReturnId=@p0 ORDER BY Id"
                : "SELECT IFNULL(pr.Name,'(صنف محذوف)') AS \"الصنف\", ri.Qty AS \"الكمية\", ri.UnitCost AS \"سعر الشراء\", ri.Qty*ri.UnitCost AS \"القيمة\" FROM PurchaseReturnItems ri LEFT JOIN Products pr ON pr.Id=ri.ProductId WHERE ri.ReturnId=@p0 ORDER BY ri.Id", id);

            string head = "لا توجد بيانات لهذا المرتجع.";
            int invId = 0;
            string fmt = "N0";
            if (h.Rows.Count > 0)
            {
                DataRow r = h.Rows[0];
                bool usd = Convert.ToString(r["Cur"]) == "USD";
                fmt = (sale || !usd) ? "N0" : "N2";
                string sym = (!sale && usd) ? " $" : " ل.س";
                invId = Convert.ToInt32(r["InvId"]);
                string d = Convert.ToString(r["Date"]);
                head = (sale ? "مرتجع مبيعات رقم " : "مرتجع مشتريات رقم ") + id + "   |   " + (d.Length > 16 ? d.Substring(0, 16) : d) +
                       "   |   من " + (sale ? "فاتورة بيع رقم " : "فاتورة مشتريات رقم ") + invId + "   |   " + (sale ? "الزبون: " : "المورد: ") + r["PName"] +
                       "\nالقيمة: " + Db.D(r["Total"]).ToString(fmt) + sym + "   |   خُصم من الدين: " + Db.D(r["DebtReduction"]).ToString(fmt) +
                       "   |   " + (sale ? "رُدّ نقداً: " : "استُرد نقداً: ") + Db.D(r["RefundCash"]).ToString(fmt);
            }

            var grid = new DataGridView();
            UI.SetupGrid(grid, true);
            grid.DataSource = items;
            UI.Col(grid, 0).FillWeight = 160;
            for (int i = 1; i < grid.Columns.Count; i++)
            {
                UI.Col(grid, i).DefaultCellStyle.Format = i == 1 ? "0.##" : (sale ? "N0" : "N2");
                UI.Col(grid, i).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            var lbl = new Label { Dock = DockStyle.Top, Height = 84, Padding = new Padding(10, 10, 10, 0), Font = new Font("Segoe UI", 11, FontStyle.Bold), Text = head };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(8), BackColor = Color.FromArgb(235, 242, 250) };
            var btn = new Button { Text = "فتح الفاتورة الأصلية", AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
            btn.Click += (a, b) =>
            {
                if (invId <= 0) return;
                new InvoiceDetailsForm(sale, invId, (sale ? "فاتورة بيع رقم " : "فاتورة مشتريات رقم ") + invId).Show();
            };
            bar.Controls.Add(btn);

            Controls.Add(grid);
            Controls.Add(bar);
            Controls.Add(lbl);
        }
    }
}
