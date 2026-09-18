using System.Text;
using System.Text.RegularExpressions;

namespace Vault.Core;

/// <summary>
/// Pure Auto-Type helpers: sequence parsing + window-title matching.
/// No Win32 calls here, so this is fully unit-testable.
/// </summary>
public enum AutoTypeTokenKind
{
    Text,
    Username,
    Password,
    Tab,
    Enter,
}

public sealed record AutoTypeToken(AutoTypeTokenKind Kind, string Text = "");

public static class AutoTypeSequence
{
    public const string Default = "{USERNAME}{TAB}{PASSWORD}{ENTER}";

    /// <summary>
    /// Parses e.g. "{USERNAME}{TAB}{PASSWORD}{ENTER}". Unknown {TAGS} stay literal text.
    /// </summary>
    public static IReadOnlyList<AutoTypeToken> Parse(string? sequence)
    {
        var tokens = new List<AutoTypeToken>();
        if (string.IsNullOrEmpty(sequence))
            return tokens;

        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length > 0)
            {
                tokens.Add(new AutoTypeToken(AutoTypeTokenKind.Text, sb.ToString()));
                sb.Clear();
            }
        }

        for (int i = 0; i < sequence.Length;)
        {
            if (sequence[i] == '{')
            {
                int end = sequence.IndexOf('}', i + 1);
                if (end < 0)
                {
                    sb.Append(sequence[i]);
                    i++;
                    continue;
                }
                string tag = sequence.Substring(i + 1, end - i - 1).Trim().ToUpperInvariant();
                AutoTypeToken? token = tag switch
                {
                    "USERNAME" => new AutoTypeToken(AutoTypeTokenKind.Username),
                    "PASSWORD" => new AutoTypeToken(AutoTypeTokenKind.Password),
                    "TAB" => new AutoTypeToken(AutoTypeTokenKind.Tab),
                    "ENTER" => new AutoTypeToken(AutoTypeTokenKind.Enter),
                    _ => null,
                };
                if (token is null)
                    sb.Append(sequence.Substring(i, end - i + 1)); // unknown tag -> literal
                else
                {
                    Flush();
                    tokens.Add(token);
                }
                i = end + 1;
            }
            else
            {
                sb.Append(sequence[i]);
                i++;
            }
        }
        Flush();
        return tokens;
    }

    /// <summary>Regex match with timeout. Invalid patterns never match (never throw).</summary>
    public static bool TitleMatches(string? title, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        try
        {
            return Regex.IsMatch(
                title ?? "", pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200));
        }
        catch
        {
            return false;
        }
    }
}
