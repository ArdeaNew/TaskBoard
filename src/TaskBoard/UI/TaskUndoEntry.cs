using System;
using System.Collections.Generic;
using TaskBoard.Core;

namespace TaskBoard.UI
{
    // Snapshot only the collections an action changes. Inbox edits do not copy
    // weeks, and transfers retain both sides for a single chronological history.
    internal sealed class TaskUndoEntry
    {
        internal List<TaskItem> Tasks { get; private set; }
        internal string DestinationWeekStart { get; private set; }
        internal WeekPlan DestinationBefore { get; private set; }
        internal List<InboxItem> InboxBefore { get; private set; }

        internal TaskUndoEntry(List<TaskItem> tasks, string destinationWeekStart = null,
            WeekPlan destinationBefore = null, List<InboxItem> inboxBefore = null)
        {
            if (tasks == null && inboxBefore == null) throw new ArgumentNullException("tasks");
            Tasks = tasks;
            DestinationWeekStart = destinationWeekStart;
            DestinationBefore = destinationBefore;
            InboxBefore = inboxBefore;
        }

        internal void Restore(BoardState state)
        {
            if (Tasks != null) state.Tasks = Tasks;
            if (InboxBefore != null) state.Inbox = InboxBefore;
            if (DestinationWeekStart != null)
            {
                int index = state.Weeks.FindIndex(plan => plan.WeekStart == DestinationWeekStart);
                if (DestinationBefore == null)
                {
                    if (index >= 0) state.Weeks.RemoveAt(index);
                }
                else if (index >= 0) state.Weeks[index] = DestinationBefore;
                else state.Weeks.Add(DestinationBefore);
            }
            var active = state.Weeks.Find(plan => plan.WeekStart == state.ActiveWeekStart);
            if (active != null) active.Tasks = state.Tasks;
        }
    }
}
