using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Patcher;

public sealed class PatchContent
{
    private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

    public string Root { get; }
    public string BaseSha { get; private set; } = string.Empty;
    public string Sha { get; private set; } = string.Empty;
    public string Json { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> Files => files.Keys;
    public bool HasPatch => files.Count > 0 && Sha.Length > 0 && !string.Equals(Sha, BaseSha, StringComparison.OrdinalIgnoreCase);

    public PatchContent(PatcherOptions options)
    {
        Root = Path.GetFullPath(options.Content);
        Directory.CreateDirectory(Root);
        Build(Path.GetFullPath(options.BaseFingerprint));
    }

    public string? Resolve(string relative)
    {
        relative = relative.Replace('\\', '/');
        if (!files.ContainsKey(relative)) return null;
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        return full.StartsWith(Root, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }

    private void Build(string basePath)
    {
        if (!File.Exists(basePath))
        {
            Console.WriteLine($"[patcher] {basePath} не найден, патч выключен");
            return;
        }

        JsonObject? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(basePath))?.AsObject();
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[patcher] {basePath} разобрать не удалось: {ex.Message}");
            return;
        }

        var entries = document?["files"]?.AsArray();
        if (document is null || entries is null)
        {
            Console.WriteLine($"[patcher] в {basePath} нет списка files, патч выключен");
            return;
        }

        Version = document["version"]?.GetValue<string>() ?? string.Empty;
        BaseSha = document["sha"]?.GetValue<string>() ?? string.Empty;
        Sha = BaseSha;

        foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            files[relative] = ShaOf(File.ReadAllBytes(path));
        }

        if (files.Count == 0)
        {
            Json = document.ToJsonString(Plain);
            Console.WriteLine($"[patcher] в {Root} пусто, клиент останется на {BaseSha}");
            return;
        }

        int changed = 0, same = 0;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry?["file"]?.GetValue<string>() is not { } name) continue;
            known.Add(name);
            if (!files.TryGetValue(name, out string? sha)) continue;
            if (entry["sha"]?.GetValue<string>() == sha) { same++; continue; }
            entry["sha"] = sha;
            changed++;
        }

        int added = 0;
        foreach (var (name, sha) in files.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (known.Contains(name)) continue;
            entries.Add(new JsonObject { ["file"] = name, ["sha"] = sha });
            added++;
        }

        if (changed + added == 0)
        {
            Json = document.ToJsonString(Plain);
            Console.WriteLine($"[patcher] {files.Count} файлов совпадают с клиентскими, качать нечего, отпечаток {BaseSha}");
            return;
        }

        Sha = ShaOf(Encoding.UTF8.GetBytes(entries.ToJsonString(Plain)));
        document["sha"] = Sha;
        Json = document.ToJsonString(Plain);

        Console.WriteLine($"[patcher] в патче {changed + added} файлов (заменено {changed}, добавлено {added}, совпало {same}), отпечаток {Sha}");
    }

    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string ShaOf(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
}
