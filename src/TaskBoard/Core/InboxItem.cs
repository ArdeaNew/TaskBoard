using System.Runtime.Serialization;

namespace TaskBoard.Core
{
    [DataContract]
    public sealed class InboxItem
    {
        public InboxItem()
        {
            Group = 1;
            Task = new TaskItem();
        }

        // 1 = Work 1, 2 = Work 2, 3 = Life.
        [DataMember(Name = "group", Order = 0)]
        public int Group { get; set; }

        [DataMember(Name = "task", Order = 1, IsRequired = true)]
        public TaskItem Task { get; set; }

        internal InboxItem Copy()
        {
            var copy = (InboxItem)MemberwiseClone();
            copy.Task = Task.Copy();
            return copy;
        }
    }
}
