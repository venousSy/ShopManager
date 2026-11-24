using System;
using System.Collections.Generic;
using System.Data;

namespace ShopManager
{
    /// <summary>
    /// بحث ذكي عن الأصناف يعمل محلياً بلا إنترنت:
    ///  1) توحيد الإملاء (أ/إ/آ=ا، ة=ه، ى=ي، بلا تشكيل ولا "ال") عبر TextUtil.Norm.
    ///  2) الكلمات بأي ترتيب، وتكفي بداية الكلمة ("بند" تجد "بندورة").
    ///  3) ترتيب حسب القرب: الكلمة النادرة أثقل من الشائعة، فتظهر الأصناف المشابهة تحت الأقرب.
    ///  4) تحمّل خطأ إملائي: حرف في الكلمات من 4 أحرف، وحرفان من 7 أحرف.
    /// التطابق التقريبي لا يُضاف للفاتورة تلقائياً؛ يُعرض في قائمة ليختار المستخدم.
    /// </summary>
    public class SmartSearch
    {
        public class Hit
        {
            public int Id;
            public string Name;
            public double Score;   // 0..1، الأعلى أقرب
            public bool Exact;     // كل كلمات البحث موجودة (كاملة أو بدايتها) بلا أخطاء إملائية
        }

        readonly List<int> ids = new List<int>();
        readonly List<string> names = new List<string>();
        readonly List<string> norms = new List<string>();
        readonly List<string> barcodes = new List<string>();
        readonly List<List<string>> toks = new List<List<string>>();
        readonly Dictionary<string, int> df = new Dictionary<string, int>();
        readonly int n;

        /// <summary>الجدول يحتاج عمودي Id و Name، والعمود Barcode اختياري.</summary>
        public SmartSearch(DataTable products)
        {
            bool hasBarcode = products.Columns.Contains("Barcode");
            foreach (DataRow r in products.Rows)
            {
                if (r.RowState == DataRowState.Deleted || r["Id"] is DBNull) continue;
                string name = Convert.ToString(r["Name"]);
                string norm = TextUtil.Norm(name);
                var ts = TextUtil.TokenList(norm);
                ids.Add(Convert.ToInt32(r["Id"]));
                names.Add(name);
                norms.Add(norm);
                barcodes.Add(hasBarcode ? Convert.ToString(r["Barcode"]).Trim() : "");
                toks.Add(ts);
                foreach (string t in ts) { int d; df.TryGetValue(t, out d); df[t] = d + 1; }
            }
            n = ids.Count;
        }

        public static SmartSearch FromDb()
        {
            return new SmartSearch(Db.Query("SELECT Id, Name, Barcode FROM Products"));
        }

        double Idf(string t)
        {
            int d; df.TryGetValue(t, out d);
            return Math.Log(1.0 + n / (1.0 + d));
        }

        /// <summary>عدد الأخطاء المسموح بها حسب طول الكلمة (الكلمات القصيرة لا تُسامَح كي لا تكثر النتائج الخاطئة).</summary>
        static int AllowedTypos(int len) { return len >= 7 ? 2 : len >= 4 ? 1 : 0; }

        /// <summary>مدى تطابق كلمة البحث q مع كلمة الصنف p: 1 كاملة، 0.9 بدايتها، 0.75 جزء منها، 0.7/0.5 بخطأ إملائي، 0 لا تطابق.</summary>
        static double TokenMatch(string q, string p, out bool clean)
        {
            clean = true;
            if (q == p) return 1;
            if (q.Length >= 2 && p.StartsWith(q, StringComparison.Ordinal)) return 0.9;
            if (q.Length >= 3 && p.Contains(q)) return 0.75;
            clean = false;
            int allowed = AllowedTypos(Math.Min(q.Length, p.Length));
            // نادراً ما يخطئ الناس في الحرف الأول؛ اشتراطه يمنع تطابقات بعيدة مثل "نيدو" ← "ديدوس"
            if (allowed == 0 || q[0] != p[0]) return 0;
            // مقارنة مع بداية كلمة الصنف أيضاً (المستخدم ما زال يكتب): "بنضو" ← "بندورة"
            int d = Math.Min(Distance(q, p, allowed), q.Length < p.Length ? Distance(q, p.Substring(0, q.Length), allowed) : int.MaxValue);
            if (d == 1) return 0.7;
            if (d == 2 && allowed >= 2) return 0.5;
            return 0;
        }

        /// <summary>مسافة التحرير (Damerau-Levenshtein المبسطة) مع توقف مبكر إذا تجاوزت الحد.</summary>
        static int Distance(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max) return max + 1;
            var prev2 = new int[b.Length + 1];
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1); // تبديل حرفين متجاورين
                    cur[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > max) return max + 1;
                var tmp = prev2; prev2 = prev; prev = cur; cur = tmp;
            }
            return prev[b.Length];
        }

        static bool SameWords(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (string w in a) if (!b.Contains(w)) return false;
            return true;
        }

        /// <summary>تطابق واضح يكفي لإضافة الصنف مباشرة: الاسم نفسه، أو تطابق واضح وحيد. وإلا null.</summary>
        public static Hit Clear(List<Hit> hits)
        {
            var same = hits.FindAll(h => h.Score >= 1.0);
            if (same.Count == 1) return same[0];
            if (same.Count > 1) return null;
            var exact = hits.FindAll(h => h.Exact);
            return exact.Count == 1 ? exact[0] : null;
        }

        /// <summary>يعيد الأصناف مرتبة من الأقرب، بحد أدنى للقرب minScore.</summary>
        public List<Hit> Search(string query, int max = 200, double minScore = 0.3)
        {
            var hits = new List<Hit>();
            string raw = (query ?? "").Trim();
            if (raw == "") return hits;
            string qn = TextUtil.Norm(raw);
            var qt = TextUtil.TokenList(qn);

            for (int i = 0; i < n; i++)
            {
                // الباركود: تطابق كامل أو جزء منه (6 أرقام فأكثر) يُعدّ تطابقاً واضحاً
                if (barcodes[i] != "" && (barcodes[i] == raw || (raw.Length >= 6 && barcodes[i].Contains(raw))))
                { hits.Add(new Hit { Id = ids[i], Name = names[i], Score = 1.01, Exact = true }); continue; }
                if (qt.Count == 0) continue;
                // نفس الاسم تماماً (بعد توحيد الإملاء وبأي ترتيب للكلمات): "بيضه" = "البيضة"
                if (norms[i] == qn || SameWords(qt, toks[i])) { hits.Add(new Hit { Id = ids[i], Name = names[i], Score = 1.0, Exact = true }); continue; }

                double wSum = 0, got = 0; bool allClean = true; int matched = 0;
                foreach (string q in qt)
                {
                    double best = 0, bestW = 0; bool bestClean = false;
                    foreach (string p in toks[i])
                    {
                        bool clean;
                        double m = TokenMatch(q, p, out clean);
                        if (m > best) { best = m; bestW = Idf(p); bestClean = clean; }
                    }
                    // وزن كلمة البحث: ندرة الكلمة المطابِقة، أو أعلى ندرة إن لم تطابق شيئاً (فكلمة لا وجود لها تخفض القرب)
                    double w = best > 0 ? bestW : Idf(q);
                    wSum += w;
                    got += w * best;
                    if (best > 0) matched++;
                    if (best < 0.9 || !bestClean) allClean = false;
                }
                if (wSum <= 0 || matched == 0) continue;
                double score = got / wSum;
                // الاسم الأقصر الأقرب لكلمات البحث يتقدّم قليلاً على الاسم الطويل
                score *= 0.85 + 0.15 * Math.Min(1.0, (double)matched / Math.Max(1, toks[i].Count));
                if (score < minScore) continue;
                hits.Add(new Hit { Id = ids[i], Name = names[i], Score = score, Exact = allClean });
            }
            hits.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Name, b.Name));
            if (hits.Count > max) hits.RemoveRange(max, hits.Count - max);
            return hits;
        }
    }
}
