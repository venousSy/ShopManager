using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>طباعة فاتورة بيع أو مشتريات (معاينة ثم طباعة) على ورق A4.</summary>
    public static class InvoicePrinter
    {
        class Total { public string Label, Value; public bool Bold; }

        class Model
        {
            public string Title;
            public List<string> RightLines = new List<string>();   // يمين: رقم الفاتورة والجهة
            public List<string> LeftLines = new List<string>();    // يسار: التاريخ والعملة
            public string PriceHeader = "السعر";
            public List<string[]> Rows = new List<string[]>();     // الصنف، الكمية، السعر، المجموع
            public List<Total> Totals = new List<Total>();
        }

        static string DateText(object v)
        {
            DateTime dt;
            return DateTime.TryParse(Convert.ToString(v), out dt) ? dt.ToString("yyyy-MM-dd HH:mm") : Convert.ToString(v);
        }

        public static void ShowSale(int saleId)
        {
            DataTable h = Db.Query(
                "SELECT s.Id, s.Date, s.Total, s.Discount, s.Net, s.Paid, s.Remaining, IFNULL(s.PrevBalance,0) AS PrevBal, IFNULL(s.CustomerId,0) AS CId, IFNULL(c.Name,'') AS CName " +
                "FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId WHERE s.Id=@p0", saleId);
            if (h.Rows.Count == 0) return;
            DataRow head = h.Rows[0];
            DataTable items = Db.Query("SELECT Name, Qty, UnitPrice FROM SaleItems WHERE SaleId=@p0 ORDER BY Id", saleId);

            var m = new Model { Title = "فاتورة مبيعات", PriceHeader = "السعر" };
            m.RightLines.Add("رقم الفاتورة: " + head["Id"]);
            string cust = Convert.ToString(head["CName"]);
            m.RightLines.Add("الزبون: " + (cust == "" ? "نقدي" : cust));
            m.LeftLines.Add("التاريخ: " + DateText(head["Date"]));
            foreach (DataRow r in items.Rows)
            {
                double q = Db.D(r["Qty"]), p = Db.D(r["UnitPrice"]);
                m.Rows.Add(new[] { Convert.ToString(r["Name"]), q.ToString("0.##"), p.ToString("N0"), (q * p).ToString("N0") });
            }
            m.Totals.Add(new Total { Label = "المجموع", Value = Db.D(head["Total"]).ToString("N0") });
            if (Db.D(head["Discount"]) > 0) m.Totals.Add(new Total { Label = "الخصم", Value = Db.D(head["Discount"]).ToString("N0") });
            m.Totals.Add(new Total { Label = "الصافي", Value = Db.D(head["Net"]).ToString("N0") + " ل.س", Bold = true });
            m.Totals.Add(new Total { Label = "المدفوع", Value = Db.D(head["Paid"]).ToString("N0") });
            if (Db.D(head["Remaining"]) > 0) m.Totals.Add(new Total { Label = "المتبقي (دين)", Value = Db.D(head["Remaining"]).ToString("N0"), Bold = true });
            if (Convert.ToInt32(head["CId"]) > 0 && Math.Abs(Db.D(head["PrevBal"])) > 0.0001)
            {
                m.Totals.Add(new Total { Label = "الرصيد السابق", Value = Db.D(head["PrevBal"]).ToString("N0") });
                m.Totals.Add(new Total { Label = "إجمالي المستحق", Value = (Db.D(head["PrevBal"]) + Db.D(head["Remaining"])).ToString("N0") + " ل.س", Bold = true });
            }
            Print(m);
        }

        public static void ShowPurchase(int purId)
        {
            DataTable h = Db.Query(
                "SELECT p.Id, p.Date, p.InvoiceNo, IFNULL(p.Currency,'SYP') AS Cur, p.Total, p.Discount, p.Net, p.Paid, p.Remaining, IFNULL(s.Name,'') AS SName " +
                "FROM Purchases p LEFT JOIN Suppliers s ON s.Id=p.SupplierId WHERE p.Id=@p0", purId);
            if (h.Rows.Count == 0) return;
            DataRow head = h.Rows[0];
            DataTable items = Db.Query(
                "SELECT IFNULL(pr.Name,'(صنف محذوف)') AS Name, pi.Qty, pi.UnitCost FROM PurchaseItems pi " +
                "LEFT JOIN Products pr ON pr.Id=pi.ProductId WHERE pi.PurchaseId=@p0 ORDER BY pi.Id", purId);

            bool usd = Convert.ToString(head["Cur"]) == "USD";
            string sym = usd ? " $" : " ل.س";
            string fmt = "N2";
            var m = new Model { Title = "فاتورة مشتريات", PriceHeader = "سعر الشراء" };
            m.RightLines.Add("رقم الفاتورة: " + head["Id"]);
            string sup = Convert.ToString(head["SName"]);
            m.RightLines.Add("المورد: " + (sup == "" ? "-" : sup));
            string theirNo = Convert.ToString(head["InvoiceNo"]);
            if (theirNo != "") m.RightLines.Add("فاتورة المورد رقم: " + theirNo);
            m.LeftLines.Add("التاريخ: " + DateText(head["Date"]));
            m.LeftLines.Add("العملة: " + (usd ? "دولار" : "ليرة"));
            foreach (DataRow r in items.Rows)
            {
                double q = Db.D(r["Qty"]), p = Db.D(r["UnitCost"]);
                m.Rows.Add(new[] { Convert.ToString(r["Name"]), q.ToString("0.##"), p.ToString(fmt), (q * p).ToString(fmt) });
            }
            m.Totals.Add(new Total { Label = "المجموع", Value = Db.D(head["Total"]).ToString(fmt) });
            if (Db.D(head["Discount"]) > 0) m.Totals.Add(new Total { Label = "الخصم", Value = Db.D(head["Discount"]).ToString(fmt) });
            m.Totals.Add(new Total { Label = "الصافي", Value = Db.D(head["Net"]).ToString(fmt) + sym, Bold = true });
            m.Totals.Add(new Total { Label = "المدفوع", Value = Db.D(head["Paid"]).ToString(fmt) });
            if (Db.D(head["Remaining"]) > 0) m.Totals.Add(new Total { Label = "المتبقي للمورد", Value = Db.D(head["Remaining"]).ToString(fmt), Bold = true });
            Print(m);
        }

        static void Print(Model m)
        {
            string shop = Db.Get("shop_name");
            string phone = Db.Get("shop_phone");
            int idx = 0;

            var doc = new PrintDocument();
            doc.BeginPrint += (a, b) => { idx = 0; };
            doc.PrintPage += (a, e) =>
            {
                Graphics g = e.Graphics;
                using (var fTitle = new Font("Arial", 22, FontStyle.Bold))
                using (var fBold = new Font("Arial", 11, FontStyle.Bold))
                using (var fNorm = new Font("Arial", 11))
                using (var fSmall = new Font("Arial", 10))
                using (var rtl = new StringFormat { FormatFlags = StringFormatFlags.DirectionRightToLeft, Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                using (var ctr = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var pen = new Pen(Color.FromArgb(120, 120, 120)))
                {
                    float L = e.MarginBounds.Left, W = e.MarginBounds.Width, y = e.MarginBounds.Top, rh = 28;

                    g.DrawString(shop, fTitle, Brushes.Black, new RectangleF(L, y, W, 44), ctr); y += 44;
                    if (phone != "") { g.DrawString(phone, fSmall, Brushes.Black, new RectangleF(L, y, W, 22), ctr); y += 22; }
                    g.DrawString(m.Title, fBold, Brushes.Black, new RectangleF(L, y, W, 28), ctr); y += 30;
                    g.DrawLine(pen, L, y, L + W, y); y += 6;

                    int lines = Math.Max(m.RightLines.Count, m.LeftLines.Count);
                    for (int i = 0; i < lines; i++)
                    {
                        if (i < m.RightLines.Count)
                            g.DrawString(m.RightLines[i], i == 0 ? fBold : fNorm, Brushes.Black, new RectangleF(L + W / 2, y, W / 2, rh), rtl);
                        if (i < m.LeftLines.Count)
                            g.DrawString(m.LeftLines[i], fNorm, Brushes.Black, new RectangleF(L, y, W / 2, rh), ctr);
                        y += rh;
                    }
                    y += 6;

                    float[] fr = { 0.07f, 0.43f, 0.12f, 0.19f, 0.19f };
                    DrawRow(g, pen, L, W, y, rh, fr, new[] { "م", "الصنف", "الكمية", m.PriceHeader, "المجموع" }, fBold, ctr, rtl, true);
                    y += rh;

                    double bottomLimit = e.MarginBounds.Bottom - 40 - rh * (m.Totals.Count + 1);
                    while (idx < m.Rows.Count && y < bottomLimit)
                    {
                        string[] row = m.Rows[idx];
                        DrawRow(g, pen, L, W, y, rh, fr, new[] { (idx + 1).ToString(), row[0], row[1], row[2], row[3] }, fNorm, ctr, rtl, false);
                        y += rh;
                        idx++;
                    }
                    if (idx < m.Rows.Count) { e.HasMorePages = true; return; }
                    e.HasMorePages = false;

                    y += 10;
                    float lw = W * 0.30f, vw = W * 0.22f, xr = L + W;
                    foreach (var t in m.Totals)
                    {
                        TotalLine(g, t.Label, t.Value, t.Bold ? fBold : fNorm, rtl, ctr, xr, y, lw, vw, rh);
                        y += rh;
                    }
                    y += 20;
                    g.DrawString("شكراً لتعاملكم معنا", fBold, Brushes.Black, new RectangleF(L, y, W, 28), ctr);
                }
            };

            using (var dlg = new PrintPreviewDialog())
            {
                dlg.Document = doc;
                dlg.WindowState = FormWindowState.Maximized;
                dlg.ShowDialog();
            }
        }

        static void DrawRow(Graphics g, Pen pen, float L, float W, float y, float rh, float[] fr, string[] cells, Font f, StringFormat ctr, StringFormat rtl, bool header)
        {
            float xr = L + W;
            for (int i = 0; i < fr.Length; i++)
            {
                float cw = W * fr[i];
                var rect = new RectangleF(xr - cw, y, cw, rh);
                if (header) g.FillRectangle(Brushes.Gainsboro, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                var inner = new RectangleF(rect.X + 3, rect.Y, rect.Width - 6, rect.Height);
                g.DrawString(cells[i], f, Brushes.Black, inner, i == 1 && !header ? rtl : ctr);
                xr -= cw;
            }
        }

        static void TotalLine(Graphics g, string label, string value, Font f, StringFormat rtl, StringFormat ctr, float xr, float y, float lw, float vw, float rh)
        {
            var rl = new RectangleF(xr - lw, y, lw, rh);
            var rv = new RectangleF(xr - lw - vw, y, vw, rh);
            g.FillRectangle(Brushes.Gainsboro, rl);
            g.DrawRectangle(Pens.Gray, rl.X, rl.Y, rl.Width, rl.Height);
            g.DrawRectangle(Pens.Gray, rv.X, rv.Y, rv.Width, rv.Height);
            g.DrawString(label, f, Brushes.Black, new RectangleF(rl.X + 3, rl.Y, rl.Width - 6, rl.Height), rtl);
            g.DrawString(value, f, Brushes.Black, rv, ctr);
        }
    }
}
