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
        class Total { public string Label, Value; public bool Bold, Big; }   // Big = مربع أسود بارز في الإيصال (الصافي)

        class Model
        {
            public string Title;
            public string Number = "", Date = "", Party = "", Cashier = "";   // رأس الإيصال الحراري
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
            BuildSale(saleId, m => { if (ReceiptMode) PrintReceipt(m, false); else Print(m); });
        }

        static void BuildSale(int saleId, Action<Model> output)
        {
            DataTable h = Db.Query(
                "SELECT s.Id, s.Date, s.Total, s.Discount, s.Net, s.Paid, s.Remaining, IFNULL(s.PrevBalance,0) AS PrevBal, IFNULL(s.CustomerId,0) AS CId, IFNULL(c.Name,'') AS CName, IFNULL(u.Name,'') AS UName " +
                "FROM Sales s LEFT JOIN Customers c ON c.Id=s.CustomerId LEFT JOIN Users u ON u.Id=s.UserId WHERE s.Id=@p0", saleId);
            if (h.Rows.Count == 0) return;
            DataRow head = h.Rows[0];
            DataTable items = Db.Query("SELECT Name, Qty, UnitPrice FROM SaleItems WHERE SaleId=@p0 ORDER BY Id", saleId);

            var m = new Model { Title = "فاتورة مبيع", PriceHeader = "السعر" };
            string cust = Convert.ToString(head["CName"]);
            m.Number = Convert.ToString(head["Id"]);
            m.Date = DateText(head["Date"]);
            m.Party = cust == "" ? "نقدي" : cust;
            m.Cashier = Convert.ToString(head["UName"]);
            m.RightLines.Add("رقم الفاتورة: " + head["Id"]);
            m.RightLines.Add("الزبون: " + m.Party);
            if (m.Cashier != "") m.RightLines.Add("البائع: " + m.Cashier);
            m.LeftLines.Add("التاريخ: " + m.Date);
            foreach (DataRow r in items.Rows)
            {
                double q = Db.D(r["Qty"]), p = Db.D(r["UnitPrice"]);
                m.Rows.Add(new[] { Convert.ToString(r["Name"]), q.ToString("0.##"), p.ToString("N0"), (q * p).ToString("N0") });
            }
            m.Totals.Add(new Total { Label = "المجموع", Value = Db.D(head["Total"]).ToString("N0") });
            if (Db.D(head["Discount"]) > 0) m.Totals.Add(new Total { Label = "الخصم", Value = Db.D(head["Discount"]).ToString("N0") });
            m.Totals.Add(new Total { Label = "الصافي", Value = Db.D(head["Net"]).ToString("N0") + " ل.س", Bold = true, Big = true });
            m.Totals.Add(new Total { Label = "المدفوع", Value = Db.D(head["Paid"]).ToString("N0") });
            if (Db.D(head["Remaining"]) > 0) m.Totals.Add(new Total { Label = "المتبقي (دين)", Value = Db.D(head["Remaining"]).ToString("N0"), Bold = true });
            if (Convert.ToInt32(head["CId"]) > 0 && Math.Abs(Db.D(head["PrevBal"])) > 0.0001)
            {
                // الدفعة الزائدة المسجّلة مع هذه الفاتورة (تسديد من الدين السابق) تُخصم من المستحق
                double extra = Db.D(Db.Scalar("SELECT IFNULL(SUM(Amount),0) FROM Payments WHERE PartyType='C' AND PartyId=@p0 AND Note=@p1",
                                              Convert.ToInt32(head["CId"]), "مع فاتورة البيع رقم " + head["Id"]));
                m.Totals.Add(new Total { Label = "الرصيد السابق", Value = Db.D(head["PrevBal"]).ToString("N0") });
                if (extra > 0) m.Totals.Add(new Total { Label = "دفعة من الدين السابق", Value = extra.ToString("N0") });
                m.Totals.Add(new Total { Label = "إجمالي المستحق", Value = (Db.D(head["PrevBal"]) + Db.D(head["Remaining"]) - extra).ToString("N0") + " ل.س", Bold = true });
            }
            output(m);
        }

        // ---------------- إيصال حراري 80mm ----------------
        public const string ModeA4 = "a4", ModeReceipt = "receipt80";

        /// <summary>فواتير البيع تُطبع إيصالاً حرارياً 80mm مباشرة (بلا معاينة) إذا اختير ذلك من الإعدادات.</summary>
        public static bool ReceiptMode { get { return Db.Get("print_mode", ModeA4) == ModeReceipt; } }

        /// <summary>معاينة إيصال لآخر فاتورة بيع (أو إيصال تجريبي) لضبط الطابعة من الإعدادات.</summary>
        public static void PreviewReceipt()
        {
            object last = Db.Scalar("SELECT MAX(Id) FROM Sales");
            Model m;
            if (last != null && !(last is DBNull)) m = SaleModel(Convert.ToInt32(last));
            else
            {
                m = new Model { Title = "فاتورة مبيع", Number = "0", Party = "نقدي", Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
                m.RightLines.Add("رقم الفاتورة: 0");
                m.RightLines.Add("الزبون: نقدي");
                m.LeftLines.Add("التاريخ: " + m.Date);
                m.Rows.Add(new[] { "صنف تجريبي باسم طويل نوعاً ما", "2", "1,500", "3,000" });
                m.Rows.Add(new[] { "صنف ثانٍ", "1", "750", "750" });
                m.Totals.Add(new Total { Label = "المجموع", Value = "3,750" });
                m.Totals.Add(new Total { Label = "الصافي", Value = "3,750 ل.س", Bold = true, Big = true });
                m.Totals.Add(new Total { Label = "المدفوع", Value = "3,750" });
            }
            PrintReceipt(m, true);
        }

        static Model SaleModel(int saleId)
        {
            Model captured = null;
            BuildSale(saleId, mm => captured = mm);
            return captured;
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

        // الوحدات هنا 1/100 إنش (وحدة الطباعة الافتراضية). ورق 80mm = 315، والمنطقة القابلة للطباعة عادةً 72mm ≈ 283.
        const int PaperWidth80 = 315;
        const float ReceiptMaxWidth = 279;

        static void PrintReceipt(Model m, bool preview)
        {
            string shop = Db.Get("shop_name");
            string phone = Db.Get("shop_phone");

            // نقيس طول الإيصال أولاً لنطلب ورقاً بطوله بالضبط (لا صفحة A4 فارغة على لفة الورق)
            float height;
            using (var bmp = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(bmp))
            {
                mg.PageUnit = GraphicsUnit.Document;   // 1/300 إنش
                mg.PageScale = 3f;                     // ← فتصبح الوحدة 1/100 إنش مثل الطابعة
                height = DrawReceipt(mg, m, 2, ReceiptMaxWidth, shop, phone, false);
            }

            var doc = new PrintDocument { DocumentName = m.Title };
            string printer = Db.Get("receipt_printer", "");
            if (printer != "")
            {
                doc.PrinterSettings.PrinterName = printer;
                if (!doc.PrinterSettings.IsValid)
                {
                    UI.Warn(null, "طابعة الإيصالات المحددة في الإعدادات غير موجودة: \"" + printer + "\".\nستُستخدم الطابعة الافتراضية.");
                    doc.PrinterSettings = new PrinterSettings();
                }
            }
            doc.DefaultPageSettings.PaperSize = new PaperSize("Receipt 80mm", PaperWidth80, (int)Math.Ceiling(height) + 40);
            doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            doc.PrintPage += (a, e) =>
            {
                float avail = e.PageSettings.PrintableArea.Width;
                float W = Math.Min(avail > 50 ? avail : ReceiptMaxWidth, ReceiptMaxWidth) - 4;
                DrawReceipt(e.Graphics, m, 2, W, shop, phone, true);
                e.HasMorePages = false;
            };

            if (preview)
            {
                using (var dlg = new PrintPreviewDialog { Document = doc, Width = 520, Height = 800 })
                    dlg.ShowDialog();
                return;
            }
            try
            {
                doc.PrintController = new StandardPrintController();   // بلا نافذة "جارٍ الطباعة"
                doc.Print();
            }
            catch (Exception ex)
            {
                UI.Warn(null, "تعذّرت طباعة الإيصال: " + ex.Message + "\nتأكد أن الطابعة موصولة ومشغّلة، أو غيّرها من الإعدادات.");
            }
        }

        /// <summary>يرسم الإيصال (أو يقيسه فقط إن كان draw=false) ويعيد الارتفاع الكلي.</summary>
        /// <summary>
        /// إيصال 80mm بترتيب إيصالات برامج المحاسبة: اسم المحل، شريط "فاتورة مبيع"، رقم وتاريخ وزبون وبائع،
        /// جدول بأعمدة (الصنف | السعر | العدد | المجموع)، عدد الأصناف، الإجماليات مع الصافي في مربع بارز، ثم الشكر.
        /// الوحدة 1/100 إنش. النص بلا تنعيم (أبيض/أسود صافٍ) لأن الطابعة الحرارية تطبع التنعيم الرمادي نقاطاً باهتة.
        /// </summary>
        static float DrawReceipt(Graphics g, Model m, float L, float W, string shop, string phone, bool draw)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            const string F = "Tahoma";   // أوضح خط عربي على دقة الطابعات الحرارية المنخفضة
            using (var fShop = new Font(F, 15, FontStyle.Bold))
            using (var fBanner = new Font(F, 11, FontStyle.Bold))
            using (var fInfo = new Font(F, 9))
            using (var fInfoB = new Font(F, 9, FontStyle.Bold))
            using (var fHead = new Font(F, 9, FontStyle.Bold))
            using (var fItem = new Font(F, 9, FontStyle.Bold))
            using (var fSmall = new Font(F, 8))
            using (var fTotal = new Font(F, 10, FontStyle.Bold))
            using (var fNet = new Font(F, 13, FontStyle.Bold))
            using (var ctr = new StringFormat { Alignment = StringAlignment.Center })
            using (var right = new StringFormat { FormatFlags = StringFormatFlags.DirectionRightToLeft, Alignment = StringAlignment.Near })
            using (var num = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            using (var numLeft = new StringFormat { Alignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap })
            using (var solid = new Pen(Color.Black, 1.5f))
            using (var thin = new Pen(Color.Black, 0.6f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
            {
                float y = 4;
                // يرسم نصاً في مستطيل عرضه w عند السطر الحالي ويعيد ارتفاعه (يلتفّ إن طال)
                Func<string, Font, StringFormat, float, float, Brush, float> textAt = (s, f, sf, x, w, br) =>
                {
                    int wi = (int)Math.Max(1, w);   // نفس العرض للقياس والرسم كي يلتفّ النص بالطريقة نفسها
                    SizeF sz = g.MeasureString(s, f, wi, sf);
                    if (draw) g.DrawString(s, f, br, new RectangleF(x + (w - wi), y, wi, sz.Height + 1), sf);
                    return sz.Height;
                };
                Func<string, Font, StringFormat, float, float, float> text = (s, f, sf, x, w) => textAt(s, f, sf, x, w, Brushes.Black);
                Action<Pen, float> rule = (p, gap) => { y += gap; if (draw) g.DrawLine(p, L, y, L + W, y); y += gap; };
                // سطر بعمودين: العنوان يميناً والقيمة يساراً
                Action<string, string, Font, Font> pair = (label, value, fl, fv) =>
                {
                    float h1 = text(label, fl, right, L + W * 0.42f, W * 0.58f);
                    float h2 = text(value, fv, numLeft, L, W * 0.42f);
                    y += Math.Max(h1, h2);
                };
                // شريط أسود بنص أبيض في المنتصف (عنوان الفاتورة)
                Action<string, Font> banner = (s, f) =>
                {
                    float h = g.MeasureString(s, f, (int)W, ctr).Height + 6;
                    if (draw) { g.FillRectangle(Brushes.Black, L, y, W, h); }
                    y += 3; textAt(s, f, ctr, L, W, Brushes.White); y += h - 3;
                };

                // ---- الرأس ----
                if (!string.IsNullOrWhiteSpace(shop)) y += text(shop, fShop, ctr, L, W);
                if (!string.IsNullOrWhiteSpace(phone)) y += text(phone, fInfo, ctr, L, W);
                y += 4;
                banner(m.Title, fBanner);
                y += 4;
                // رقم الفاتورة يميناً والتاريخ يساراً على سطر واحد
                float hn = text("رقم: " + m.Number, fInfoB, right, L + W * 0.45f, W * 0.55f);
                float hd = text(m.Date, fInfo, numLeft, L, W * 0.45f);
                y += Math.Max(hn, hd);
                y += text("الزبون: " + m.Party, fInfoB, right, L, W);
                if (m.Cashier != "") y += text("البائع: " + m.Cashier, fInfo, right, L, W);

                // ---- جدول الأصناف: الصنف | السعر | العدد | المجموع (من اليمين) ----
                float cName = W * 0.43f, cPrice = W * 0.20f, cQty = W * 0.13f, cTot = W * 0.24f;
                float xName = L + W - cName, xPrice = xName - cPrice, xQty = xPrice - cQty, xTot = L;
                rule(solid, 3);
                float hh = text("الصنف", fHead, right, xName, cName);
                text("السعر", fHead, ctr, xPrice, cPrice);
                text("العدد", fHead, ctr, xQty, cQty);
                text("المجموع", fHead, ctr, xTot, cTot);
                y += hh;
                rule(solid, 2);
                double qtySum = 0;
                for (int i = 0; i < m.Rows.Count; i++)
                {
                    string[] r = m.Rows[i];               // الصنف، الكمية، السعر، المجموع
                    double q; if (double.TryParse(r[1], out q)) qtySum += q;
                    float h1 = text(r[0], fItem, right, xName, cName - 2);
                    float h2 = text(r[2], fItem, num, xPrice, cPrice);
                    text(r[1], fItem, num, xQty, cQty);
                    text(r[3], fItem, num, xTot, cTot);
                    y += Math.Max(h1, h2) + 1;
                    if (i < m.Rows.Count - 1) rule(thin, 1.5f);
                }
                rule(solid, 3);
                y += text("عدد الأصناف: " + m.Rows.Count + "     مجموع الكميات: " + qtySum.ToString("0.##"), fSmall, ctr, L, W);
                y += 3;

                // ---- الإجماليات ----
                foreach (var t in m.Totals)
                {
                    if (t.Big)
                    {
                        y += 3;
                        float h = g.MeasureString(t.Value, fNet).Height + 6;
                        if (draw) g.FillRectangle(Brushes.Black, L, y, W, h);
                        y += 3;
                        textAt(t.Label, fNet, right, L + W * 0.45f, W * 0.55f - 4, Brushes.White);
                        textAt(t.Value, fNet, numLeft, L + 4, W * 0.55f, Brushes.White);
                        y += h - 3 + 3;
                    }
                    else pair(t.Label, t.Value, t.Bold ? fTotal : fInfo, t.Bold ? fTotal : fInfoB);
                }

                // ---- التذييل ----
                rule(solid, 4);
                y += text("شكراً لتعاملكم معنا", fInfoB, ctr, L, W);
                return y + 30;   // فراغ قبل القصّ
            }
        }

        /// <summary>للفحص: يرسم إيصال فاتورة بيع (أو الإيصال التجريبي إن كان saleId=0) في صورة PNG بدقة طابعة 203 نقطة/إنش.</summary>
        public static void SaveReceiptPng(int saleId, string file)
        {
            Model m = null;
            if (saleId > 0) m = SaleModel(saleId);
            if (m == null)
            {
                m = new Model { Title = "فاتورة مبيع", Number = "0", Party = "نقدي", Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
                m.Rows.Add(new[] { "صنف تجريبي باسم طويل نوعاً ما", "2", "1,500", "3,000" });
                m.Rows.Add(new[] { "صنف ثانٍ", "1", "750", "750" });
                m.Totals.Add(new Total { Label = "الصافي", Value = "3,750 ل.س", Bold = true, Big = true });
            }
            string shop = Db.Get("shop_name"), phone = Db.Get("shop_phone");
            const float dpi = 203f;
            float h;
            using (var b0 = new Bitmap(1, 1))
            using (var g0 = Graphics.FromImage(b0)) { g0.PageUnit = GraphicsUnit.Document; g0.PageScale = 3f; h = DrawReceipt(g0, m, 2, ReceiptMaxWidth, shop, phone, false); }
            using (var bmp = new Bitmap((int)(PaperWidth80 * dpi / 100), (int)((h + 10) * dpi / 100)))
            {
                bmp.SetResolution(dpi, dpi);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.PageUnit = GraphicsUnit.Document; g.PageScale = 3f;   // 1/100 إنش مثل الطابعة
                    DrawReceipt(g, m, 2, ReceiptMaxWidth, shop, phone, true);
                }
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
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
