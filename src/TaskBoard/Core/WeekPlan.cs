using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TaskBoard.Core
{
    [DataContract]
    public sealed class WeekPlan
    {
        public WeekPlan()
        {
            Tasks = new List<TaskItem>();
        }

        [DataMember(Name = "weekStart", Order = 0, IsRequired = true)]
        public string WeekStart { get; set; }

        [DataMember(Name = "tasks", Order = 1, IsRequired = true)]
        public List<TaskItem> Tasks { get; set; }
    }
}
