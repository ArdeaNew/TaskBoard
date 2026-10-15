using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using TaskBoard.Core;

namespace TaskBoard.Persistence
{
    public sealed class BoardStore
    {
        private readonly string directory;
        private readonly string primaryPath;
        private readonly string backupPath;
        private bool protectPrimary;
        private bool protectBackup;

        public BoardStore(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("A data directory is required.", "dataDirectory");

            directory = Path.GetFullPath(dataDirectory);
            primaryPath = Path.Combine(directory, "board.json");
            backupPath = Path.Combine(directory, "board.backup.json");
            LastWarning = string.Empty;
        }

        public string LastWarning { get; private set; }

        public BoardState Load()
        {
            LastWarning = string.Empty;
            protectPrimary = false;
            protectBackup = false;

            BoardState state;
            if (TryLoad(primaryPath, out state, out protectPrimary))
                return state;

            if (TryLoad(backupPath, out state, out protectBackup))
            {
                AddWarning("已从本地备份恢复任务。");
                return state;
            }

            return new BoardState();
        }

        public void Save(BoardState state)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            if (protectPrimary || protectBackup)
                throw new IOException("原任务文件无法安全保留，请检查数据目录的访问权限。");

            BoardState snapshot = Normalize(state);
            Directory.CreateDirectory(directory);
            // Files can also be changed or damaged outside this running process.
            // Preserve any invalid file before replacement could overwrite it.
            BoardState existing;
            bool blocked;
            TryLoad(primaryPath, out existing, out blocked);
            protectPrimary = blocked;
            TryLoad(backupPath, out existing, out blocked);
            protectBackup = blocked;
            if (protectPrimary || protectBackup)
                throw new IOException("原任务文件无法安全保留，请检查数据目录的访问权限。");

            string temporaryPath = Path.Combine(directory, ".board-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    CompatibilityData.Write(stream, snapshot, CreateSerializer());
                    stream.Flush(true);
                }

                if (File.Exists(primaryPath))
                {
                    // Replace is atomic on the same volume and keeps the previous state.
                    File.Replace(temporaryPath, primaryPath, backupPath, true);
                }
                else
                {
                    File.Move(temporaryPath, primaryPath);
                    if (!File.Exists(backupPath))
                        CreateInitialBackup();
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private bool TryLoad(string path, out BoardState state, out bool protectFile)
        {
            state = null;
            protectFile = false;
            if (!File.Exists(path))
                return false;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ValidateDocumentBoundary(stream);
                    stream.Position = 0;
                    state = CompatibilityData.Read(stream, CreateSerializer());
                }

                if (state == null || state.Tasks == null)
                    throw new SerializationException("The task list is missing.");
                state = Normalize(state);
                return true;
            }
            catch (Exception error)
            {
                if (!IsReadFailure(error))
                    throw;

                string preservedPath;
                if (TryPreserve(path, out preservedPath))
                    AddWarning("无法读取任务文件，原文件已保留为 " + Path.GetFileName(preservedPath) + "。");
                else
                {
                    protectFile = true;
                    AddWarning("无法读取任务文件，原文件仍保留在 " + path + "。");
                }
                state = null;
                return false;
            }
        }

        private bool TryPreserve(string path, out string preservedPath)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
            preservedPath = Path.Combine(directory,
                Path.GetFileNameWithoutExtension(path) + ".corrupt-" + timestamp + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            try
            {
                File.Move(path, preservedPath);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private void CreateInitialBackup()
        {
            string temporaryBackup = Path.Combine(directory, ".backup-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(primaryPath, temporaryBackup);
                File.Move(temporaryBackup, backupPath);
            }
            finally
            {
                if (File.Exists(temporaryBackup))
                    File.Delete(temporaryBackup);
            }
        }

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(typeof(BoardState));
        }

        private static void ValidateDocumentBoundary(Stream stream)
        {
            // The framework JSON reader silently ignores content after its root.
            // Check only the boundary here; the serializer validates JSON syntax.
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 1024, true))
            {
                int depth = 0;
                bool started = false;
                bool inString = false;
                bool escaped = false;
                int next;
                while ((next = reader.Read()) != -1)
                {
                    char c = (char)next;
                    if (!started || depth == 0)
                    {
                        if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                            continue;
                        if (started || c != '{')
                            throw new SerializationException("Unexpected JSON document content.");
                        started = true;
                        depth = 1;
                    }
                    else if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (c == '\\') escaped = true;
                        else if (c == '"') inString = false;
                    }
                    else if (c == '"') inString = true;
                    else if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') depth--;
                }
                if (!started || depth != 0 || inString)
                    throw new SerializationException("Incomplete JSON document.");
            }
        }

        private static BoardState Normalize(BoardState source)
        {
            var result = new BoardState
            {
                ExtensionData = source.ExtensionData,
                Tasks = NormalizeTasks(source.Tasks),
                Weeks = new List<WeekPlan>(),
                ActiveWeekStart = string.IsNullOrWhiteSpace(source.ActiveWeekStart) ? null :
                    WeeklyPlanner.Key(WeeklyPlanner.ParseWeekStart(source.ActiveWeekStart))
            };
            var weeks = new Dictionary<string, WeekPlan>(StringComparer.Ordinal);
            var duplicateExtras = new Dictionary<string, List<TaskItem>>(StringComparer.Ordinal);
            if (source.Weeks != null)
                foreach (WeekPlan plan in source.Weeks)
                {
                    if (plan == null || plan.Tasks == null)
                        throw new SerializationException("The weekly archive contains an invalid plan.");
                    string key = WeeklyPlanner.Key(WeeklyPlanner.ParseWeekStart(plan.WeekStart));
                    List<TaskItem> tasks = NormalizeTasks(plan.Tasks);
                    WeekPlan existing;
                    if (weeks.TryGetValue(key, out existing))
                    {
                        List<TaskItem> extras;
                        if (!duplicateExtras.TryGetValue(key, out extras))
                            duplicateExtras.Add(key, extras = new List<TaskItem>());
                        extras.AddRange(tasks);
                        existing.Tasks.AddRange(tasks);
                        existing.Tasks = NormalizeTasks(existing.Tasks);
                    }
                    else
                    {
                        existing = new WeekPlan { WeekStart = key, Tasks = tasks };
                        weeks.Add(key, existing);
                        result.Weeks.Add(existing);
                    }
                }
            if (result.ActiveWeekStart != null)
            {
                // Tasks is authoritative for the active week, including an undo
                // that replaced its list. Additional duplicate plans are retained.
                List<TaskItem> extras;
                if (duplicateExtras.TryGetValue(result.ActiveWeekStart, out extras))
                {
                    result.Tasks.AddRange(extras);
                    result.Tasks = NormalizeTasks(result.Tasks);
                }
                WeekPlan active;
                if (!weeks.TryGetValue(result.ActiveWeekStart, out active))
                {
                    active = new WeekPlan { WeekStart = result.ActiveWeekStart };
                    result.Weeks.Add(active);
                }
                active.Tasks = result.Tasks;
            }
            return result;
        }

        private static List<TaskItem> NormalizeTasks(IEnumerable<TaskItem> tasks)
        {
            var result = new List<TaskItem>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (tasks == null) return result;
            foreach (TaskItem task in tasks)
            {
                if (task == null)
                    throw new SerializationException("The task list contains a null item.");

                string id = task.Id;
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    ids.Add(id);
                }
                result.Add(new TaskItem
                {
                    Id = id,
                    Text = task.Text ?? string.Empty,
                    Color = TaskColors.NormalizeHex(task.Color) ?? "#111111",
                    Column = task.Column >= 1 && task.Column <= 7 ? task.Column : 1,
                    DailyGroupId = task.DailyGroupId,
                    IsCompleted = task.IsCompleted,
                    BackgroundColor = TaskColors.NormalizeHex(task.BackgroundColor)
                });
            }
            return result;
        }

        private static bool IsReadFailure(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException ||
                error is SerializationException || error is XmlException ||
                error is FormatException || error is ArgumentException;
        }

        private void AddWarning(string warning)
        {
            LastWarning = LastWarning.Length == 0 ? warning : LastWarning + Environment.NewLine + warning;
        }
    }
}
