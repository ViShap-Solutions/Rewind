using System.Text;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// Reads and writes the harness's own CSV files: a header line, then one record per line, text cells quoted with
/// doubled quotes inside. Every file the harness writes is in this shape.
/// </summary>
internal static class Csv
{
    /// <summary>Every record of <paramref name="path"/> by column name, or none when the file does not exist.</summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

        if (lines.Count == 0)
        {
            return [];
        }

        var header = Split(lines[0]);

        return
        [
            .. lines.Skip(1).Select(line =>
            {
                var cells = Split(line);
                var record = new Dictionary<string, string>(StringComparer.Ordinal);

                for (var index = 0; index < header.Count; index++)
                {
                    record[header[index]] = index < cells.Count ? cells[index] : string.Empty;
                }

                return (IReadOnlyDictionary<string, string>)record;
            }),
        ];
    }

    /// <summary>Writes <paramref name="records"/> under <paramref name="header"/>, every cell quoted.</summary>
    internal static void Write(string path, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> records)
    {
        var text = new StringBuilder(string.Join(",", header)).Append('\n');

        foreach (var record in records)
        {
            text.Append(string.Join(",", record.Select(Quote))).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }

    /// <summary>A text cell, quoted so that a comma or a quote inside it stays in it.</summary>
    internal static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            char character = line[index];

            if (quoted)
            {
                if (character != '"')
                {
                    cell.Append(character);
                }
                else if (index + 1 < line.Length && line[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (character != '﻿')
            {
                cell.Append(character);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }
}
