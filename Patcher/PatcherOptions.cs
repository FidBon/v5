namespace Patcher;

public sealed class PatcherOptions
{
    public string Address { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 9999;
    public string Url { get; init; } = string.Empty;
    public string Content { get; init; } = "Patcher/Content";
    public string BaseFingerprint { get; init; } = "Patcher/base-fingerprint.json";

    public string PublicUrl()
    {
        if (Url.Length > 0) return Url.EndsWith('/') ? Url : Url + "/";
        string host = Address is "0.0.0.0" or "" or "+" ? "127.0.0.1" : Address;
        return $"http://{host}:{Port}/";
    }
}
