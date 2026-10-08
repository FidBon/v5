namespace GameLogic.Csv;

public sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;

    public IReadOnlyList<string[]> Rows { get; }
    public IReadOnlyList<string> ColumnNames { get; }

    private CsvTable(List<string[]> rows, string[] header)
    {
        Rows = rows;
        ColumnNames = header;
        _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++) _columns.TryAdd(header[i].Trim(), i);
    }

    public int Count => Rows.Count;

    public int ColumnIndex(string name) =>
        _columns.TryGetValue(name, out var index) ? index : -1;

    public string Cell(int row, int column)
    {
        if (row < 0 || row >= Rows.Count) return string.Empty;
        var cells = Rows[row];
        return column >= 0 && column < cells.Length ? cells[column] : string.Empty;
    }

    public string Cell(int row, string column) => Cell(row, ColumnIndex(column));

    public int CellInt(int row, string column) =>
        int.TryParse(Cell(row, column), out var value) ? value : 0;

    public int CellInt(int row, int column) =>
        int.TryParse(Cell(row, column), out var value) ? value : 0;

    public bool CellBool(int row, string column) =>
        Cell(row, column).Equals("true", StringComparison.OrdinalIgnoreCase);

    public bool CellBool(int row, int column) =>
        Cell(row, column).Equals("true", StringComparison.OrdinalIgnoreCase);

    public static CsvTable Load(string path)
    {
        var lines = ParseFile(path);
        if (lines.Count < 2)
            throw new InvalidDataException($"{path}: ожидались хотя бы строка колонок и строка типов");

        var header = lines[0];
        var rows = lines.Skip(2).ToList();
        return new CsvTable(rows, header);
    }

    private static List<string[]> ParseFile(string path)
    {
        var result = new List<string[]>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 && result.Count == 0) continue;
            result.Add(ParseLine(line));
        }
        return result;
    }

    private static string[] ParseLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else quoted = false;
                }
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(current.ToString()); current.Clear(); }
            else if (c != '\r') current.Append(c);
        }
        cells.Add(current.ToString());
        return cells.ToArray();
    }
}
