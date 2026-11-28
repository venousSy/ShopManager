using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using ClosedXML.Excel;

namespace ShopManager
{
    /// <summary>ورقة واحدة في ملف الإكسل: جدول البيانات وما يُخفى منه وما يُجمع وما يُلوَّن.</summary>
    public class ExcelSheet
    {
        public string Name;
        public DataTable Table;
        public string[] Hide = new string[0];                // أعمدة لا تُصدَّر (مثل Id)
        public string[] Totals = new string[0];              // أعمدة يُضاف لها سطر مجموع (الموجب فقط، مثل شاشة الديون)
        public Func<DataRow, bool> Highlight;                 // سطر يُلوَّن بالأحمر (فوق الحد، تحت الحد الأدنى...)
        public string Title;                                  // سطر عنوان فوق الجدول (اختياري)
    }

    /// <summary>تصدير جداول إلى ملف Excel حقيقي (.xlsx) باتجاه عربي وتنسيق للأرقام.</summary>
    public static class ExcelExport
    {
        public static void Save(IWin32Window owner, string baseName, params ExcelSheet[] sheets)
        {
            string path;
            using (var dlg = new SaveFileDialog())
            {
                dlg.FileName = baseName + "_" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".xlsx";
                dlg.Filter = "Excel|*.xlsx";
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            try
            {
                using (var wb = new XLWorkbook())
                {
                    foreach (var s in sheets) WriteSheet(wb, s);
                    wb.SaveAs(path);
                }
            }
            catch (IOException)
            {
                UI.Warn(owner, "تعذّر حفظ الملف. إن كان مفتوحاً في إكسل أغلقه ثم أعد المحاولة.");
                return;
            }
            if (UI.Confirm(owner, "تم حفظ الملف:\n" + path + "\n\nفتحه الآن؟"))
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { UI.Warn(owner, "تعذّر فتح الملف: " + ex.Message); }
            }
        }

        static void WriteSheet(XLWorkbook wb, ExcelSheet s)
        {
            var ws = wb.Worksheets.Add(SafeSheetName(s.Name));
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Segoe UI";
            ws.Style.Font.FontSize = 11;

            var cols = new List<DataColumn>();
            foreach (DataColumn c in s.Table.Columns)
                if (Array.IndexOf(s.Hide, c.ColumnName) < 0) cols.Add(c);
            if (cols.Count == 0) return;

            int row = 1;
            if (!string.IsNullOrEmpty(s.Title))
            {
                var t = ws.Cell(row, 1);
                t.Value = s.Title;
                t.Style.Font.Bold = true;
                t.Style.Font.FontSize = 14;
                ws.Range(row, 1, row, cols.Count).Merge();
                row += 2;
            }

            int headerRow = row;
            for (int i = 0; i < cols.Count; i++) ws.Cell(headerRow, i + 1).Value = cols[i].ColumnName;
            var header = ws.Range(headerRow, 1, headerRow, cols.Count);
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = XLColor.FromArgb(31, 78, 120);
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int r = headerRow;
            foreach (DataRow dr in s.Table.Rows)
            {
                r++;
                for (int i = 0; i < cols.Count; i++) SetValue(ws.Cell(r, i + 1), dr[cols[i]]);
                if (s.Highlight != null && s.Highlight(dr))
                {
                    var line = ws.Range(r, 1, r, cols.Count);
                    line.Style.Font.FontColor = XLColor.DarkRed;
                    line.Style.Font.Bold = true;
                    line.Style.Fill.BackgroundColor = XLColor.FromArgb(253, 233, 233);
                }
            }
            int lastData = r;

            // تنسيق الأرقام حسب العملة في اسم العمود
            for (int i = 0; i < cols.Count; i++)
            {
                string n = cols[i].ColumnName;
                string fmt = n.Contains("$") ? "#,##0.00" : (n.Contains("ل.س") ? "#,##0" : null);
                if (fmt != null && lastData > headerRow) ws.Range(headerRow + 1, i + 1, lastData + 1, i + 1).Style.NumberFormat.Format = fmt;
            }

            // سطر المجموع: صيغة حيّة (SUMIF للموجب فقط) فيتحدّث إن عدّل أحدٌ الأرقام في إكسل
            if (s.Totals.Length > 0 && lastData > headerRow)
            {
                int tr = lastData + 1;
                bool any = false;
                for (int i = 0; i < cols.Count; i++)
                {
                    if (Array.IndexOf(s.Totals, cols[i].ColumnName) < 0) continue;
                    string addr = ws.Range(headerRow + 1, i + 1, lastData, i + 1).RangeAddress.ToStringRelative();
                    ws.Cell(tr, i + 1).FormulaA1 = "SUMIF(" + addr + ",\">0\")";
                    any = true;
                }
                if (any)
                {
                    ws.Cell(tr, 1).Value = "المجموع";
                    var tot = ws.Range(tr, 1, tr, cols.Count);
                    tot.Style.Font.Bold = true;
                    tot.Style.Fill.BackgroundColor = XLColor.FromArgb(221, 235, 247);
                    tot.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                }
            }

            ws.SheetView.FreezeRows(headerRow);
            if (lastData > headerRow) ws.Range(headerRow, 1, lastData, cols.Count).SetAutoFilter();
            ws.Columns(1, cols.Count).AdjustToContents();
            foreach (var c in ws.Columns(1, cols.Count)) if (c.Width < 10) c.Width = 10; else if (c.Width > 60) c.Width = 60;
        }

        static void SetValue(IXLCell cell, object v)
        {
            if (v == null || v == DBNull.Value) return;
            switch (v)
            {
                case double d: cell.Value = d; break;
                case float f: cell.Value = f; break;
                case decimal m: cell.Value = m; break;
                case long l: cell.Value = l; break;
                case int i: cell.Value = i; break;
                case bool b: cell.Value = b ? "نعم" : "لا"; break;
                default: cell.Value = Convert.ToString(v, CultureInfo.InvariantCulture); break;
            }
        }

        /// <summary>أسماء أوراق إكسل: 31 حرفاً كحد أقصى ودون الرموز \ / ? * [ ] :</summary>
        static string SafeSheetName(string n)
        {
            foreach (char ch in new[] { '\\', '/', '?', '*', '[', ']', ':' }) n = n.Replace(ch, '-');
            return n.Length > 31 ? n.Substring(0, 31) : n;
        }
    }
}
