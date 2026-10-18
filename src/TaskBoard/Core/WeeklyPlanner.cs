using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace TaskBoard.Core
{
    public static class WeeklyPlanner
    {
        public static DateTime MondayOf(DateTime date)
        {
            return date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        }

        public static DateTime CurrentWeekStart(BoardState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            return string.IsNullOrWhiteSpace(state.ActiveWeekStart)
                ? MondayOf(DateTime.Today) : ParseWeekStart(state.ActiveWeekStart);
        }

        public static void Initialize(BoardState state, DateTime today)
        {
            EnsureState(state);
            string currentKey = Key(MondayOf(today));
            if (state.Weeks.Count == 0)
            {
                // A file from before weekly archives belongs to the current week.
                state.Weeks.Add(new WeekPlan { WeekStart = currentKey, Tasks = state.Tasks });
                state.ActiveWeekStart = currentKey;
            }
            else if (string.IsNullOrWhiteSpace(state.ActiveWeekStart) && state.Tasks.Count > 0)
            {
                WeekPlan current = Find(state, currentKey);
                if (current == null)
                {
                    current = new WeekPlan { WeekStart = currentKey, Tasks = state.Tasks };
                    state.Weeks.Add(current);
                }
                else
                    MergeLegacyTasks(current.Tasks, state.Tasks);
                state.Tasks = current.Tasks;
                state.ActiveWeekStart = currentKey;
            }
            SelectWeek(state, today, today);
            RollOverUnfinished(state, today);
        }

        public static int RollOverUnfinished(BoardState state, DateTime today)
        {
            EnsureState(state);
            if (state.Weeks.Count == 0)
            {
                Initialize(state, today);
                return 0;
            }
            CommitActiveWeek(state);
            DateTime currentMonday = MondayOf(today);
            var expired = state.Weeks.Where(plan => ParseWeekStart(plan.WeekStart) < currentMonday)
                .OrderBy(plan => ParseWeekStart(plan.WeekStart)).ToList();
            if (!expired.Any(plan => plan.Tasks.Any(task => !task.IsCompleted))) return 0;

            // Seed normal daily occurrences before removing their previous-week
            // sources. Skipped weeks contribute only tasks that actually exist.
            WeekPlan target = GetOrCreateWeek(state, currentMonday, today);
            int moved = 0;
            foreach (WeekPlan plan in expired)
            {
                // Filtering a day retains its visible order even when a loaded
                // list happens to interleave tasks from several columns.
                var unfinished = plan.Tasks.Where(task => !task.IsCompleted)
                    .OrderBy(task => task.Column).ToList();
                foreach (TaskItem task in unfinished)
                {
                    task.Column = 7;
                    // Missed daily occurrences are independent work. Keeping the
                    // group would merge several occurrences into one Sunday.
                    task.DailyGroupId = null;
                }
                plan.Tasks.RemoveAll(task => !task.IsCompleted);
                target.Tasks.AddRange(unfinished);
                moved += unfinished.Count;
            }
            ArrangeWeek(target);
            return moved;
        }

        public static bool CanMoveToNextWeek(BoardState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            // Both the following Monday and its Sunday must be representable.
            return CurrentWeekStart(state) <= DateTime.MaxValue.Date.AddDays(-13);
        }

        public static bool MoveToNextWeek(BoardState state, TaskItem task, DateTime today)
        {
            EnsureState(state);
            if (task == null || !state.Tasks.Contains(task) || !CanMoveToNextWeek(state)) return false;
            CommitActiveWeek(state);
            DateTime targetMonday = CurrentWeekStart(state).AddDays(7);
            WeekPlan target = GetOrCreateWeek(state, targetMonday, today);
            state.Tasks.Remove(task);
            task.Column = 7;
            task.DailyGroupId = null;
            target.Tasks.Add(task);
            ArrangeWeek(target);
            return true;
        }

        public static void SelectWeek(BoardState state, DateTime anyDate, DateTime today)
        {
            EnsureState(state);
            if (state.Weeks.Count == 0) Initialize(state, today);
            CommitActiveWeek(state);
            DateTime monday = MondayOf(anyDate);
            string key = Key(monday);
            WeekPlan plan = GetOrCreateWeek(state, monday, today);
            state.ActiveWeekStart = key;
            state.Tasks = plan.Tasks;
            BoardOperations.ArrangeTasks(state);
        }

        public static BoardState SnapshotForSave(BoardState state, List<TaskItem> committedActiveTasks,
            IEnumerable<InboxItem> committedInbox = null)
        {
            EnsureState(state);
            string activeKey = string.IsNullOrWhiteSpace(state.ActiveWeekStart)
                ? null : Key(ParseWeekStart(state.ActiveWeekStart));
            var snapshot = new BoardState
            {
                ActiveWeekStart = activeKey,
                Tasks = BoardOperations.CloneTasks(committedActiveTasks ?? state.Tasks),
                Inbox = InboxOperations.CloneItems(committedInbox ?? state.Inbox ?? new List<InboxItem>()),
                ExtensionData = state.ExtensionData,
                Weeks = new List<WeekPlan>()
            };
            bool activeCopied = false;
            foreach (WeekPlan plan in state.Weeks)
            {
                if (plan == null || plan.Tasks == null)
                    throw new SerializationException("The weekly archive contains an invalid plan.");
                string key = Key(ParseWeekStart(plan.WeekStart));
                bool active = !activeCopied && key == activeKey;
                snapshot.Weeks.Add(new WeekPlan
                {
                    WeekStart = key,
                    Tasks = active ? snapshot.Tasks : BoardOperations.CloneTasks(plan.Tasks)
                });
                if (active) activeCopied = true;
            }
            if (activeKey != null && !activeCopied)
                snapshot.Weeks.Add(new WeekPlan { WeekStart = activeKey, Tasks = snapshot.Tasks });
            return snapshot;
        }

        internal static DateTime ParseWeekStart(string value)
        {
            DateTime date;
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
                throw new SerializationException("The weekly archive contains an invalid week date.");
            return MondayOf(date);
        }

        internal static string Key(DateTime monday)
        {
            return monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static void CommitActiveWeek(BoardState state)
        {
            if (string.IsNullOrWhiteSpace(state.ActiveWeekStart)) return;
            string key = Key(ParseWeekStart(state.ActiveWeekStart));
            WeekPlan active = Find(state, key);
            if (active == null)
            {
                active = new WeekPlan { WeekStart = key };
                state.Weeks.Add(active);
            }
            // Undo may replace the list rather than mutate the old archive list.
            active.Tasks = state.Tasks;
            state.ActiveWeekStart = key;
        }

        private static List<TaskItem> SeedDailyTasks(BoardState state, DateTime target)
        {
            WeekPlan previous = state.Weeks.Where(plan => ParseWeekStart(plan.WeekStart) < target)
                .OrderByDescending(plan => ParseWeekStart(plan.WeekStart)).FirstOrDefault();
            var tasks = new List<TaskItem>();
            if (previous == null) return tasks;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TaskItem source in previous.Tasks)
            {
                if (!source.IsDaily || !seen.Add(source.DailyGroupId)) continue;
                for (int day = 1; day <= 7; day++)
                {
                    TaskItem previousDay = previous.Tasks.FirstOrDefault(task =>
                        task.DailyGroupId == source.DailyGroupId && task.Column == day);
                    tasks.Add(new TaskItem
                    {
                        Text = source.Text,
                        Color = source.Color,
                        Column = day,
                        DailyGroupId = source.DailyGroupId,
                        IsCompleted = false,
                        BackgroundColor = previousDay != null ? previousDay.BackgroundColor : source.BackgroundColor
                    });
                }
            }
            return tasks;
        }

        private static WeekPlan Find(BoardState state, string key)
        {
            return state.Weeks.FirstOrDefault(plan => Key(ParseWeekStart(plan.WeekStart)) == key);
        }

        private static WeekPlan GetOrCreateWeek(BoardState state, DateTime monday, DateTime today)
        {
            string key = Key(monday);
            WeekPlan plan = Find(state, key);
            if (plan == null)
            {
                plan = new WeekPlan
                {
                    WeekStart = key,
                    Tasks = monday >= MondayOf(today) ? SeedDailyTasks(state, monday) : new List<TaskItem>()
                };
                state.Weeks.Add(plan);
            }
            return plan;
        }

        private static void ArrangeWeek(WeekPlan plan)
        {
            BoardOperations.ArrangeTasks(new BoardState { Tasks = plan.Tasks });
        }

        private static void EnsureState(BoardState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (state.Tasks == null) state.Tasks = new List<TaskItem>();
            if (state.Weeks == null) state.Weeks = new List<WeekPlan>();
        }

        private static void MergeLegacyTasks(List<TaskItem> target, List<TaskItem> legacy)
        {
            if (ReferenceEquals(target, legacy)) return;
            foreach (TaskItem task in legacy)
            {
                TaskItem existing = target.FirstOrDefault(item => item.Id == task.Id);
                if (existing != null && existing.Text == task.Text && existing.Color == task.Color &&
                    existing.Column == task.Column && existing.DailyGroupId == task.DailyGroupId &&
                    existing.IsCompleted == task.IsCompleted && existing.BackgroundColor == task.BackgroundColor) continue;
                TaskItem copy = BoardOperations.CloneTasks(new[] { task })[0];
                if (existing != null) copy.Id = Guid.NewGuid().ToString("N");
                target.Add(copy);
            }
        }
    }
}
