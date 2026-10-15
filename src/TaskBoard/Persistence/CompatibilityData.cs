using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using TaskBoard.Core;

namespace TaskBoard.Persistence
{
    // Framework extension data mishandles empty strings and some JSON names.
    // Keep unknown member trees opaque and restore them verbatim in the JSON
    // reader/writer representation, without reviving retired feature models.
    internal static class CompatibilityData
    {
        private static readonly ConditionalWeakTable<ExtensionDataObject, XmlDocument> UnknownMembers =
            new ConditionalWeakTable<ExtensionDataObject, XmlDocument>();

        public static BoardState Read(Stream stream, DataContractJsonSerializer serializer)
        {
            XmlDocument document = ReadJson(stream);
            BoardState state;
            using (var reader = new XmlNodeReader(document))
                state = serializer.ReadObject(reader) as BoardState;
            if (state == null || state.ExtensionData == null) return state;

            var unknown = new XmlDocument();
            unknown.AppendChild(unknown.CreateElement("opaque"));
            foreach (XmlNode member in document.DocumentElement.ChildNodes)
                if (!IsCurrentMember(member))
                    unknown.DocumentElement.AppendChild(unknown.ImportNode(member, true));
            if (unknown.DocumentElement.HasChildNodes)
                UnknownMembers.Add(state.ExtensionData, unknown);
            return state;
        }

        public static void Write(Stream stream, BoardState state, DataContractJsonSerializer serializer)
        {
            XmlDocument unknown;
            if (state.ExtensionData == null || !UnknownMembers.TryGetValue(state.ExtensionData, out unknown))
            {
                serializer.WriteObject(stream, state);
                return;
            }

            XmlDocument document;
            using (var buffer = new MemoryStream())
            {
                var currentData = new DataContractJsonSerializer(typeof(BoardState),
                    new DataContractJsonSerializerSettings { IgnoreExtensionDataObject = true });
                currentData.WriteObject(buffer, state);
                buffer.Position = 0;
                document = ReadJson(buffer);
            }
            XmlNode root = document.DocumentElement;
            for (XmlNode member = root.FirstChild; member != null; )
            {
                XmlNode next = member.NextSibling;
                if (!IsCurrentMember(member)) root.RemoveChild(member);
                member = next;
            }
            foreach (XmlNode member in unknown.DocumentElement.ChildNodes)
                root.AppendChild(document.ImportNode(member, true));
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false))
            {
                root.WriteTo(writer);
                writer.Flush();
            }
        }

        private static XmlDocument ReadJson(Stream stream)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream,
                Encoding.UTF8, XmlDictionaryReaderQuotas.Max, null))
            {
                var document = new XmlDocument { PreserveWhitespace = true };
                document.Load(reader);
                return document;
            }
        }

        private static bool IsCurrentMember(XmlNode member)
        {
            return member.Name == "tasks" || member.Name == "activeWeekStart" || member.Name == "weeks";
        }
    }
}
