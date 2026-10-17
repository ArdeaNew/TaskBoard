using System;
using System.Collections.Generic;
using TaskBoard.Core;

namespace TaskBoard.UI
{
    // Ordinary edits retain only the visible week; a cross-week move also keeps
    // its destination. History does not clone every archived week on each edit.
    internal sealed class TaskUndoEntry
    {
        internal List<TaskItem> Tasks { get; private set; }
        internal string DestinationWeekStart { get; private set; }
        internal WeekPlan DestinationBefore { get; private set; }

        internal TaskUndoEntry(List<TaskItem> tasks, string destinationWeekStart = null, WeekPlan destinationBefore = null)
        {
            if (tasks == null) throw new ArgumentNullException("tasks");
            Tasks = tasks;
            DestinationWeekStart = destinationWeekStart;
            DestinationBefore = destinationBefore;
        }

        internal void Restore(BoardState state)
        {
            state.Tasks = Tasks;
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
