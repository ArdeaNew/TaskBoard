using System;
using System.Runtime.Serialization;

namespace TaskBoard.Core
{
    [DataContract]
    public sealed class TaskItem
    {
        public TaskItem()
        {
            Id = Guid.NewGuid().ToString("N");
            Text = string.Empty;
            Color = "#111111";
            Column = 1;
        }

        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "text", Order = 1)]
        public string Text { get; set; }

        [DataMember(Name = "color", Order = 2)]
        public string Color { get; set; }

        // 1–7 are Monday–Sunday. Legacy column 0 is migrated to Monday on load.
        [DataMember(Name = "column", Order = 3)]
        public int Column { get; set; }

        [DataMember(Name = "dailyGroupId", Order = 4, EmitDefaultValue = false)]
        public string DailyGroupId { get; set; }

        [DataMember(Name = "isCompleted", Order = 5, EmitDefaultValue = false)]
        public bool IsCompleted { get; set; }

        [DataMember(Name = "backgroundColor", Order = 6, EmitDefaultValue = false)]
        public string BackgroundColor { get; set; }

        public bool IsDaily { get { return !string.IsNullOrEmpty(DailyGroupId); } }
    }
}
