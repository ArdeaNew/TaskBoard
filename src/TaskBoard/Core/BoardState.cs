using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TaskBoard.Core
{
    [DataContract]
    public sealed class BoardState : IExtensibleDataObject
    {
        public BoardState()
        {
            Tasks = new List<TaskItem>();
            Weeks = new List<WeekPlan>();
        }

        // Filtering this list by Monday–Sunday Column (1–7) preserves task order.
        [DataMember(Name = "tasks", Order = 0, IsRequired = true)]
        public List<TaskItem> Tasks { get; set; }

        [DataMember(Name = "activeWeekStart", Order = 4, EmitDefaultValue = false)]
        public string ActiveWeekStart { get; set; }

        [DataMember(Name = "weeks", Order = 5)]
        public List<WeekPlan> Weeks { get; set; }

        // Unknown fields from earlier files round-trip without an active feature
        // model. This includes retired records and window-size preferences.
        public ExtensionDataObject ExtensionData { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Weeks = new List<WeekPlan>();
        }
    }
}
