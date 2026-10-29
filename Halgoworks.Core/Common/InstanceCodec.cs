using System.IO.Compression;
using System.Text.Json;

namespace Halgoworks.Core;

/// <summary>
/// Packs an instance into a URL-safe string (JSON, deflated, base64url) so a link can reproduce
/// exactly what's on screen, and unpacks it again with size limits and validation.
/// </summary>
public static class InstanceCodec
{
    // Bump when the instance shape changes in a way old links can't follow.
    private const char Version = '1';
    private const int MaxPayloadChars = 8_000;
    private const int MaxJsonBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Encode<T>(T instance)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(instance, Json);
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(json);
        return Version + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Decodes a payload and runs <paramref name="check"/> on it. Returns the instance, or null with a
    /// reason the page can show.
    /// </summary>
    public static T? Decode<T>(string payload, Func<T?, string?> check, out string? problem) where T : class
    {
        problem = null;
        try
        {
            if (payload.Length is < 2 or > MaxPayloadChars || payload[0] != Version)
            {
                problem = "the link is from a different version of Halgoworks or was cut short";
                return null;
            }

            var base64 = payload[1..].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            using var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = input.Read(chunk)) > 0)
            {
                json.Write(chunk, 0, read);
                if (json.Length > MaxJsonBytes)
                {
                    problem = "the shared example is too large";
                    return null;
                }
            }

            var instance = JsonSerializer.Deserialize<T>(json.ToArray(), Json);
            problem = check(instance);
            return problem is null ? instance : null;
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or JsonException or NotSupportedException or ArgumentException)
        {
            problem = "the link is damaged";
            return null;
        }
    }
}
