using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskBoard.Core
{
    public static class InboxOperations
    {
        public static InboxItem Add(BoardState state, int group)
        {
            EnsureState(state);
            ValidateGroup(group);
            var item = new InboxItem { Group = group };
            state.Inbox.Add(item);
            return item;
        }

        public static bool Remove(BoardState state, InboxItem item)
        {
            EnsureState(state);
            return item != null && state.Inbox.Remove(item);
        }

        public static bool ChangeGroup(BoardState state, InboxItem item, int group)
        {
            EnsureState(state);
            ValidateGroup(group);
            if (item == null || !state.Inbox.Contains(item) || item.Group == group) return false;
            state.Inbox.Remove(item);
            item.Group = group;
            state.Inbox.Add(item);
            return true;
        }

        public static bool MoveFromBoard(BoardState state, TaskItem task, int group)
        {
            EnsureState(state);
            ValidateGroup(group);
            if (task == null || !state.Tasks.Contains(task) || string.IsNullOrWhiteSpace(task.Text)) return false;
            state.Tasks.Remove(task);
            EnsureUniqueId(task, state.Inbox.Select(item => item.Task));
            task.DailyGroupId = null;
            task.Column = 1;
            state.Inbox.Add(new InboxItem { Group = group, Task = task });
            return true;
        }

        public static bool Schedule(BoardState state, InboxItem item, int column)
        {
            EnsureState(state);
            if (column < 1 || column > 7 || item == null || item.Task == null ||
                !state.Inbox.Contains(item) || string.IsNullOrWhiteSpace(item.Task.Text)) return false;
            state.Inbox.Remove(item);
            TaskItem task = item.Task;
            EnsureUniqueId(task, state.Tasks);
            task.Column = column;
            task.DailyGroupId = null;
            state.Tasks.Add(task);
            // Keep completed tasks together while appending pending work last.
            BoardOperations.ArrangeTasks(state);
            return true;
        }

        public static List<InboxItem> CloneItems(IEnumerable<InboxItem> items)
        {
            if (items == null) throw new ArgumentNullException("items");
            return items.Select(item => item.Copy()).ToList();
        }

        private static void EnsureUniqueId(TaskItem task, IEnumerable<TaskItem> destination)
        {
            var ids = new HashSet<string>(destination.Where(item => item != null)
                .Select(item => item.Id), StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(task.Id) && !ids.Contains(task.Id)) return;
            do { task.Id = Guid.NewGuid().ToString("N"); } while (ids.Contains(task.Id));
        }

        private static void ValidateGroup(int group)
        {
            if (group < 1 || group > 3) throw new ArgumentOutOfRangeException("group");
        }

        private static void EnsureState(BoardState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (state.Tasks == null) state.Tasks = new List<TaskItem>();
            if (state.Inbox == null) state.Inbox = new List<InboxItem>();
        }
    }
}
