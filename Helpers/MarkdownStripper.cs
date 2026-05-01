using System.Text.RegularExpressions;

namespace CRM.Api.Helpers
{
    public static class MarkdownStripper
    {
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1", RegexOptions.Singleline);
            text = Regex.Replace(text, @"__(.+?)__", "$1", RegexOptions.Singleline);
            text = Regex.Replace(text, @"\*(.+?)\*", "$1", RegexOptions.Singleline);
            text = Regex.Replace(text, @"_(.+?)_", "$1", RegexOptions.Singleline);
            text = Regex.Replace(text, @"^#+\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*[-*]\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"`(.+?)`", "$1", RegexOptions.Singleline);
            text = Regex.Replace(text, @"```(.+?)```", "$1", RegexOptions.Singleline);
            text = Regex.Replace(text, @"^\s*(---|\*\*\*|___)\s*$", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\n\n\n+", "\n\n");
            text = text.Trim();

            return text;
        }
    }
}
