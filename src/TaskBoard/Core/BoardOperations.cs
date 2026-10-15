using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskBoard.Core
{
    public static class BoardOperations
    {
        public static int ClearDay(BoardState state, int column)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (column < 1 || column > 7) throw new ArgumentOutOfRangeException("column");
            RequireState(state);
            // Mutate the active list in place so its WeekPlan keeps the same list.
            return state.Tasks.RemoveAll(task => task != null && task.Column == column);
        }

        public static void ApplyToEveryDay(BoardState state, TaskItem source)
        {
            RequireState(state);
            if (source == null) throw new ArgumentNullException("source");
            if (!state.Tasks.Contains(source))
                throw new ArgumentException("The source task must belong to this board.", "source");

            if (source.Column < 1 || source.Column > 7) source.Column = 1;
            string groupId = source.IsDaily ? source.DailyGroupId : Guid.NewGuid().ToString("N");
            source.DailyGroupId = groupId;
            var members = new TaskItem[7];
            members[source.Column - 1] = source;
            foreach (TaskItem task in state.Tasks)
            {
                if (task.DailyGroupId == groupId && task.Column >= 1 && task.Column <= 7 && members[task.Column - 1] == null)
                    members[task.Column - 1] = task;
            }
            // Only pending copies move to the end. Completed members keep their
            // existing order relative to the day's other completed tasks.
            var others = state.Tasks.Where(task => task.DailyGroupId != groupId ||
                (task.IsCompleted && task.Column >= 1 && task.Column <= 7 &&
                 ReferenceEquals(members[task.Column - 1], task))).ToList();

            for (int day = 1; day <= 7; day++)
            {
                TaskItem task = members[day - 1] ?? new TaskItem { BackgroundColor = source.BackgroundColor };
                task.Column = day;
                task.Text = source.Text;
                task.Color = source.Color;
                task.DailyGroupId = groupId;
                if (!task.IsCompleted) others.Add(task);
            }
            state.Tasks.Clear();
            state.Tasks.AddRange(others);
            ArrangeTasks(state);
        }

        public static void SyncDailyTask(BoardState state, TaskItem source)
        {
            RequireState(state);
            if (source == null) throw new ArgumentNullException("source");
            if (!source.IsDaily) return;
            foreach (TaskItem task in state.Tasks)
            {
                if (task.DailyGroupId != source.DailyGroupId) continue;
                task.Text = source.Text;
                task.Color = source.Color;
            }
        }

        public static void ArrangeTasks(BoardState state)
        {
            RequireState(state);
            var normal = new List<TaskItem>[7];
            var daily = new List<TaskItem>[7];
            var completed = new List<TaskItem>[7];
            for (int i = 0; i < 7; i++)
            {
                normal[i] = new List<TaskItem>();
                daily[i] = new List<TaskItem>();
                completed[i] = new List<TaskItem>();
            }
            foreach (TaskItem task in state.Tasks)
            {
                if (task == null) throw new ArgumentException("The board contains a null task.", "state");
                if (task.Column < 1 || task.Column > 7) task.Column = 1;
                (task.IsCompleted ? completed : task.IsDaily ? daily : normal)[task.Column - 1].Add(task);
            }
            state.Tasks.Clear();
            for (int i = 0; i < 7; i++)
            {
                state.Tasks.AddRange(completed[i]);
                state.Tasks.AddRange(normal[i]);
                state.Tasks.AddRange(daily[i]);
            }
        }

        public static List<TaskItem> CloneTasks(IEnumerable<TaskItem> tasks)
        {
            if (tasks == null) throw new ArgumentNullException("tasks");
            return tasks.Select(task => new TaskItem
            {
                Id = task.Id,
                Text = task.Text,
                Color = task.Color,
                Column = task.Column,
                DailyGroupId = task.DailyGroupId,
                IsCompleted = task.IsCompleted,
                BackgroundColor = task.BackgroundColor
            }).ToList();
        }

        private static void RequireState(BoardState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (state.Tasks == null) state.Tasks = new List<TaskItem>();
        }
    }
}
