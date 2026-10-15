namespace TaskBoard.Core
{
    public static class TaskColors
    {
        public static string NormalizeHex(string color)
        {
            return IsValidHex(color) ? color.ToUpperInvariant() : null;
        }

        public static bool IsValidHex(string color)
        {
            if (color == null || color.Length != 7 || color[0] != '#') return false;
            for (int i = 1; i < color.Length; i++)
            {
                char c = color[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }
    }
}
