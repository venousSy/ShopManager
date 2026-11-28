using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ShopManager
{
    /// <summary>
    /// عدة فواتير بيع مفتوحة معاً، كل زبون في تبويب: يُركن زبون ويُخدم آخر ثم يُعاد للأول.
    /// كل تبويب يحمل InvoiceForm كاملة مضمّنة (TopLevel=false) فلا تتكرر منطقها هنا.
    /// </summary>
    public class SalesTabsForm : Form
    {
        readonly TabControl tabs = new TabControl();
        int counter;

        public SalesTabsForm()
        {
            UI.Rtl(this);
            Text = "فواتير البيع";
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            var strip = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(6, 6, 6, 0), BackColor = Color.FromArgb(31, 78, 120) };
            var btnAdd = new Button
            {
                Text = "+ فاتورة لزبون جديد  (Ctrl+T)", AutoSize = true, Height = 40, Margin = new Padding(4, 0, 4, 0),
                Font = new Font("Segoe UI", 12, FontStyle.Bold), BackColor = Color.FromArgb(214, 238, 214), FlatStyle = FlatStyle.Flat
            };
            btnAdd.Click += (a, b) => AddInvoice();
            var btnClose = new Button
            {
                Text = "إغلاق هذه الفاتورة  (Ctrl+W)", AutoSize = true, Height = 40, Margin = new Padding(4, 0, 4, 0),
                Font = new Font("Segoe UI", 12), BackColor = Color.FromArgb(250, 225, 225), FlatStyle = FlatStyle.Flat
            };
            btnClose.Click += (a, b) => CloseCurrent();
            var hint = new Label
            {
                Text = "التنقل بين الفواتير: انقر التبويب أو Ctrl+Tab", AutoSize = true, ForeColor = Color.White,
                Margin = new Padding(20, 10, 4, 0), Font = new Font("Segoe UI", 11)
            };
            strip.Controls.Add(btnAdd);
            strip.Controls.Add(btnClose);
            strip.Controls.Add(hint);

            tabs.Dock = DockStyle.Fill;
            tabs.RightToLeftLayout = true;
            tabs.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            tabs.Padding = new Point(18, 6);
            tabs.SelectedIndexChanged += (a, b) => { tabs.Invalidate(); Current()?.FocusBarcode(); };
            // التبويب المعروض بلون واضح حتى يعرف البائع أي زبون أمامه
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(300, 40);
            tabs.DrawItem += (a, e) =>
            {
                bool sel = e.Index == tabs.SelectedIndex;
                var back = sel ? Color.FromArgb(31, 78, 120) : Color.FromArgb(225, 232, 240);
                // مع RightToLeftLayout يُعكس رسم GDI+ أفقياً ولا يُعكس النص، فنقلب مستطيل الخلفية وحده
                var fill = new Rectangle(tabs.Width - e.Bounds.Right, e.Bounds.Top, e.Bounds.Width, e.Bounds.Height);
                using (var br = new SolidBrush(back)) e.Graphics.FillRectangle(br, fill);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
                    sel ? Color.White : Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis);
            };

            Controls.Add(tabs);
            Controls.Add(strip);

            KeyDown += (a, b) =>
            {
                if (b.Control && b.KeyCode == Keys.T) { b.Handled = b.SuppressKeyPress = true; AddInvoice(); }
                else if (b.Control && b.KeyCode == Keys.W) { b.Handled = b.SuppressKeyPress = true; CloseCurrent(); }
            };
            // إغلاق النافذة كلها: كل فاتورة تسأل عن أصنافها، وأي رفض يُبقي النافذة مفتوحة
            FormClosing += (a, b) =>
            {
                if (b.CloseReason == CloseReason.WindowsShutDown) return;
                foreach (var inv in Invoices().ToList())
                {
                    tabs.SelectedTab = (TabPage)inv.Parent;
                    inv.Close();
                    if (!inv.IsDisposed) { b.Cancel = true; return; }
                }
            };

            AddInvoice();
        }

        /// <summary>تبويب جديد بفاتورة بيع فارغة، ويصبح هو المعروض.</summary>
        public void AddInvoice()
        {
            // فاتورة فارغة موجودة (مثلاً بعد حفظ فاتورة زبون) تُستعمل بدل تكديس تبويبات فارغة
            var empty = Invoices().FirstOrDefault(i => !i.HasLines);
            if (empty != null)
            {
                tabs.SelectedTab = (TabPage)empty.Parent;
                empty.FocusBarcode();
                return;
            }
            counter++;
            int number = counter;
            var inv = new InvoiceForm(true)
            {
                WindowState = FormWindowState.Normal,
                TopLevel = false,
                FormBorderStyle = FormBorderStyle.None,
                Dock = DockStyle.Fill
            };
            var page = new TabPage(inv.TabCaption(number));
            page.Controls.Add(inv);
            inv.Changed += () => { if (!page.IsDisposed) page.Text = inv.TabCaption(number); };
            // الفاتورة أُغلقت (بعد التأكيد): يُزال تبويبها، وآخر تبويب يغلق النافذة
            inv.FormClosed += (a, b) =>
            {
                tabs.TabPages.Remove(page);
                page.Dispose();
                if (tabs.TabPages.Count == 0 && !Disposing && !IsDisposed) BeginInvoke(new Action(Close));
            };
            tabs.TabPages.Add(page);
            inv.Show();
            tabs.SelectedTab = page;
            inv.FocusBarcode();
        }

        /// <summary>يُظهر النافذة (حتى لو كانت مصغّرة) ويفتح فيها فاتورة جديدة.</summary>
        public void BringUpWithNewInvoice()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Maximized;
            Activate();
            AddInvoice();
        }

        void CloseCurrent()
        {
            var inv = Current();
            if (inv != null) inv.Close();
        }

        InvoiceForm Current()
        {
            return tabs.SelectedTab?.Controls.OfType<InvoiceForm>().FirstOrDefault();
        }

        System.Collections.Generic.IEnumerable<InvoiceForm> Invoices()
        {
            foreach (TabPage p in tabs.TabPages)
                foreach (var inv in p.Controls.OfType<InvoiceForm>()) yield return inv;
        }
    }
}
