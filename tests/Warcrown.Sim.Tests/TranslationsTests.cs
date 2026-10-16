using System.Text;
using System.Text.RegularExpressions;

namespace Warcrown.Sim.Tests;

/// <summary>
/// Guards Translations/strings.csv (keys,en,ru): one record per line, three fields, unique keys, and the
/// same {n} placeholders in every language. A broken quote would otherwise silently swallow the rows after it.
/// </summary>
public class TranslationsTests
{
    private static List<List<string>> ReadCsv(string text, out int physicalLines)
    {
        var records = new List<List<string>>();
        var field = new StringBuilder();
        var record = new List<string>();
        bool quoted = false;
        physicalLines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                {
                    if (c == '\n')
                        physicalLines++;
                    field.Append(c);
                }
                continue;
            }
            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\n':
                    physicalLines++;
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                    break;
                case '\r':
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        Assert.That(quoted, Is.False, "an unterminated quote at the end of the file");
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }

    [Test]
    public void StringsFileIsWellFormed()
    {
        string text = File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Translations", "strings.csv"));
        var records = ReadCsv(text, out _);

        Assert.That(records[0], Is.EqualTo(new[] { "keys", "en", "ru" }));
        foreach (var record in records)
        {
            Assert.That(record, Has.Count.EqualTo(3), $"record '{record[0]}' must have key, en and ru");
            Assert.That(record.Any(f => f.Contains('\n')), Is.False,
                $"record '{record[0]}' spans several lines (a broken quote?); write line breaks as \\n");
        }
        var keys = records.Select(r => r[0]).ToList();
        Assert.That(keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty, "duplicate keys");
    }

    [Test]
    public void LanguagesUseTheSamePlaceholders()
    {
        string text = File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Translations", "strings.csv"));
        var placeholder = new Regex(@"\{(\d+)(:[^}]*)?\}");
        foreach (var record in ReadCsv(text, out _).Skip(1))
        {
            var en = placeholder.Matches(record[1]).Select(m => m.Groups[1].Value).Distinct().Order();
            var ru = placeholder.Matches(record[2]).Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.That(ru, Is.EqualTo(en), $"placeholders of '{record[0]}'");
        }
    }
}
