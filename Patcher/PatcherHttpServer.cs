using System.Net;
using System.Text;

namespace Patcher;

public sealed class PatcherHttpServer(PatcherOptions options, PatchContent content)
{
    public async Task RunAsync(CancellationToken ct)
    {
        using var listener = new HttpListener();
        string host = options.Address is "0.0.0.0" or "" ? "+" : options.Address;
        listener.Prefixes.Add($"http://{host}:{options.Port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"[patcher] не удалось занять порт {options.Port}: {ex.Message}");
            return;
        }

        Console.WriteLine($"[patcher] раздаю {content.Root} на {options.PublicUrl()}{content.Sha}/");

        using var registration = ct.Register(listener.Stop);
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            _ = ServeAsync(context);
        }
    }

    private async Task ServeAsync(HttpListenerContext context)
    {
        try
        {
            string path = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/").Trim('/');
            string relative = path;
            int slash = path.IndexOf('/');
            if (slash > 0 && IsSha(path[..slash])) relative = path[(slash + 1)..];

            if (relative.Equals("fingerprint.json", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(context, Encoding.UTF8.GetBytes(content.Json), "application/json");
                return;
            }

            if (content.Resolve(relative) is not { } full)
            {
                Console.WriteLine($"[patcher] {context.Request.RemoteEndPoint?.Address}: нет файла {path}");
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            context.Response.ContentType = ContentTypeFor(full);
            await using var file = File.OpenRead(full);
            context.Response.ContentLength64 = file.Length;
            await file.CopyToAsync(context.Response.OutputStream);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[patcher] ошибка отдачи: {ex.Message}");
            try { context.Response.Abort(); } catch { }
        }
    }

    private static async Task WriteAsync(HttpListenerContext context, byte[] body, string type)
    {
        context.Response.ContentType = type;
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }

    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}
