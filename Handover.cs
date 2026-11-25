using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShopManager
{
    /// <summary>
    /// استلام وتسليم ملف البيانات بين جهازين لا يعملان في الوقت نفسه، عبر مجلد مشترك تزامنه خدمة سحابية
    /// (Google Drive / OneDrive). الملف عند جهاز واحد فقط في كل لحظة:
    ///   - الاستلام: ينزّل أحدث نسخة من المجلد ويسجّل "الجهاز X استلم".
    ///   - العمل: على نسخة محلية، فلا يتوقف البيع إن انقطع الإنترنت.
    ///   - التسليم: يرفع النسخة ويحرّر الاستلام.
    /// أي حالة شاذة (الملف لم يكتمل تزامنه، أو استلام بالقوة من جهاز آخر) لا تكتب فوق بيانات أحد بصمت:
    /// تُحفظ النسخة المتعارضة في مجلد conflicts ليُراجعها صاحب المحل.
    /// هذا الصنف لا يعرض أي نافذة، ليمكن اختباره.
    /// </summary>
    public class Handover
    {
        public class Ticket
        {
            public string Holder { get; set; }        // اسم الجهاز المستلم، أو null إذا سُلّم
            public string HolderSince { get; set; }
            public int Version { get; set; }          // يزيد عند كل تسليم
            public string Hash { get; set; }          // بصمة shop.db المرفوع، للتأكد أن تزامنه اكتمل
            public string LastBy { get; set; }
            public string LastAt { get; set; }
        }

        public class LocalState
        {
            public int Version { get; set; }          // نسخة المجلد التي بُنيت عليها النسخة المحلية
            public bool Holding { get; set; }
            public string ShiftStart { get; set; }
            public Dictionary<string, long> Marks { get; set; } = new Dictionary<string, long>();
        }

        public enum Status { Ok, NotInitialized, HeldByOther, SyncIncomplete, FolderMissing, Conflict, Failed }

        public class Result
        {
            public Status Status;
            public string Message = "";
            public Ticket Ticket;
            public string ConflictFile;
            public bool Downloaded;
        }

        public readonly string LocalDb, StateFile, Folder, Machine;
        string CloudDb { get { return Path.Combine(Folder, "shop.db"); } }
        string TicketFile { get { return Path.Combine(Folder, "handover.json"); } }
        string ConflictsDir { get { return Path.Combine(Folder, "conflicts"); } }
        string BackupsDir { get { return Path.Combine(Path.GetDirectoryName(LocalDb), "backups"); } }

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        public Handover(string localDb, string stateFile, string folder, string machine)
        {
            LocalDb = localDb; StateFile = stateFile; Folder = folder; Machine = machine;
        }

        static string Now() { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
        static string Stamp() { return DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture); }

        // ---------------- الملفات ----------------
        public LocalState LoadState()
        {
            try { if (File.Exists(StateFile)) return JsonSerializer.Deserialize<LocalState>(File.ReadAllText(StateFile)) ?? new LocalState(); }
            catch { }
            return new LocalState();
        }

        public void SaveState(LocalState s) { WriteAtomic(StateFile, JsonSerializer.Serialize(s, Json)); }

        public Ticket ReadTicket()
        {
            if (!File.Exists(TicketFile)) return null;
            return JsonSerializer.Deserialize<Ticket>(File.ReadAllText(TicketFile));
        }

        void WriteTicket(Ticket t) { WriteAtomic(TicketFile, JsonSerializer.Serialize(t, Json)); }

        /// <summary>كتابة في ملف مؤقت ثم استبدال، فلا يرى أحد ملفاً نصف مكتوب.</summary>
        static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, true);
        }

        static void CopyAtomic(string from, string to)
        {
            string tmp = to + ".copying";
            File.Copy(from, tmp, true);
            File.Move(tmp, to, true);
        }

        public static string HashOf(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return Convert.ToHexString(sha.ComputeHash(f));
        }

        /// <summary>اتصالات SQLite المفتوحة قد تمنع نسخ الملف أو تجعله غير مكتمل؛ نغلقها قبل أي نسخ.</summary>
        static void ReleaseDb() { SQLiteConnection.ClearAllPools(); GC.Collect(); GC.WaitForPendingFinalizers(); }

        string SaveConflictCopy(string reason)
        {
            Directory.CreateDirectory(ConflictsDir);
            string f = Path.Combine(ConflictsDir, "shop_" + Safe(Machine) + "_" + Stamp() + "_" + reason + ".db");
            File.Copy(LocalDb, f, true);
            return f;
        }

        void BackupLocal()
        {
            if (!File.Exists(LocalDb)) return;
            Directory.CreateDirectory(BackupsDir);
            File.Copy(LocalDb, Path.Combine(BackupsDir, "shop_" + Stamp() + ".db"), true);
            var old = new List<string>(Directory.GetFiles(BackupsDir, "shop_*.db"));
            old.Sort(StringComparer.Ordinal);
            for (int i = 0; i < old.Count - 20; i++) { try { File.Delete(old[i]); } catch { } }   // آخر 20 نسخة تكفي
        }

        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // ---------------- الاستلام ----------------
        /// <summary>يستلم ملف البيانات لهذا الجهاز. force: استلام رغم أن جهازاً آخر لم يسلّم.</summary>
        public Result CheckOut(bool force)
        {
            try
            {
                if (!Directory.Exists(Folder)) return new Result { Status = Status.FolderMissing, Message = Folder };
                var state = LoadState();
                Ticket t = ReadTicket();
                if (t == null)
                    return new Result { Status = File.Exists(CloudDb) ? Status.SyncIncomplete : Status.NotInitialized };

                if (t.Holder != null && t.Holder != Machine && !force)
                    return new Result { Status = Status.HeldByOther, Ticket = t };

                // هذا الجهاز ما زال مستلماً (أُغلق البرنامج دون تسليم): نتابع على نسخته المحلية
                if (t.Holder == Machine && state.Holding && state.Version == t.Version && File.Exists(LocalDb))
                    return new Result { Status = Status.Ok, Ticket = t };

                var res = new Result { Status = Status.Ok };
                if (t.Version != state.Version || !File.Exists(LocalDb))
                {
                    if (!File.Exists(CloudDb) || HashOf(CloudDb) != t.Hash)
                        return new Result { Status = Status.SyncIncomplete, Ticket = t };
                    ReleaseDb();
                    // كان هذا الجهاز مستلماً وأخذه جهاز آخر بالقوة: لا نرمي عمله، نحفظه نسخة تعارض
                    if (state.Holding && File.Exists(LocalDb)) res.ConflictFile = SaveConflictCopy("not_handed_over");
                    BackupLocal();
                    CopyAtomic(CloudDb, LocalDb);
                    if (HashOf(LocalDb) != t.Hash) return new Result { Status = Status.Failed, Message = "النسخة المنزّلة لا تطابق المرفوعة." };
                    res.Downloaded = true;
                }
                t.Holder = Machine;
                t.HolderSince = Now();
                WriteTicket(t);
                state.Version = t.Version;
                state.Holding = true;
                SaveState(state);
                res.Ticket = t;
                return res;
            }
            catch (Exception ex) { return new Result { Status = Status.Failed, Message = ex.Message }; }
        }

        /// <summary>أول مرة: يرفع ملف هذا الجهاز ليصبح النسخة المشتركة، ويبقى مستلماً.</summary>
        public Result Initialize()
        {
            try
            {
                if (!Directory.Exists(Folder)) return new Result { Status = Status.FolderMissing, Message = Folder };
                if (File.Exists(TicketFile) || File.Exists(CloudDb)) return new Result { Status = Status.Failed, Message = "المجلد المشترك فيه بيانات أصلاً." };
                ReleaseDb();
                CopyAtomic(LocalDb, CloudDb);
                var t = new Ticket { Holder = Machine, HolderSince = Now(), Version = 1, Hash = HashOf(CloudDb), LastBy = Machine, LastAt = Now() };
                WriteTicket(t);
                var s = LoadState(); s.Version = 1; s.Holding = true; SaveState(s);
                return new Result { Status = Status.Ok, Ticket = t };
            }
            catch (Exception ex) { return new Result { Status = Status.Failed, Message = ex.Message }; }
        }

        // ---------------- التسليم ----------------
        public Result HandOver()
        {
            try
            {
                if (!Directory.Exists(Folder)) return new Result { Status = Status.FolderMissing, Message = Folder };
                var state = LoadState();
                Ticket t = ReadTicket();
                ReleaseDb();
                // استلمه جهاز آخر بالقوة، أو تغيّرت النسخة في المجلد: لا نكتب فوقها
                if (t == null || t.Holder != Machine || t.Version != state.Version)
                {
                    string cf = SaveConflictCopy("conflict");
                    state.Holding = false;
                    SaveState(state);
                    return new Result { Status = Status.Conflict, Ticket = t, ConflictFile = cf };
                }
                CopyAtomic(LocalDb, CloudDb);
                t.Hash = HashOf(CloudDb);
                if (HashOf(LocalDb) != t.Hash) return new Result { Status = Status.Failed, Message = "النسخة المرفوعة لا تطابق المحلية." };
                t.Version++;
                t.Holder = null;
                t.HolderSince = null;
                t.LastBy = Machine;
                t.LastAt = Now();
                WriteTicket(t);
                state.Version = t.Version;
                state.Holding = false;
                SaveState(state);
                return new Result { Status = Status.Ok, Ticket = t };
            }
            catch (Exception ex) { return new Result { Status = Status.Failed, Message = ex.Message }; }
        }
    }
}
