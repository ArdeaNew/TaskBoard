using System.Windows.Markup;
using System.Windows.Media;

namespace TaskBoard.Rendering
{
    public static class Typography
    {
        private static readonly FontFamily taskFont = CreateTaskFont();

        public static FontFamily TaskFont { get { return taskFont; } }

        private static FontFamily CreateTaskFont()
        {
            var font = new FontFamily();
            font.FamilyNames.Add(XmlLanguage.GetLanguage("en-US"), "Task Board");
            // Map punctuation together with the CJK glyphs, independently of the
            // current Windows display language. Latin text remains monospaced.
            font.FamilyMaps.Add(new FontFamilyMap
            {
                Unicode = "2E80-2FFF,3000-303F,3100-312F,31A0-31BF,31C0-31EF,3400-4DBF,4E00-9FFF,F900-FAFF,FE10-FE1F,FE30-FE4F,FF00-FFEF,20000-2FA1F",
                Target = "SimSun"
            });
            font.FamilyMaps.Add(new FontFamilyMap
            {
                Unicode = "0000-10FFFF",
                Target = "Consolas, SimSun"
            });
            return font;
        }
    }
}
