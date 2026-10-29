using System.Text;
using Microsoft.AspNetCore.Components;

namespace Halgoworks.Services;

/// <summary>Share links: the instance codec lives in <see cref="Halgoworks.Core.InstanceCodec"/>; this builds the URL.</summary>
public static class ShareState
{
    public static string Encode<T>(T instance) => Halgoworks.Core.InstanceCodec.Encode(instance);

    public static T? Decode<T>(string payload, Func<T?, string?> check, out string? problem) where T : class =>
        Halgoworks.Core.InstanceCodec.Decode(payload, check, out problem);

    /// <summary>Builds an absolute link to a lab with the given query values (nulls are left out).</summary>
    public static string Link(NavigationManager nav, string slug, params (string Key, string? Value)[] query)
    {
        var sb = new StringBuilder(nav.BaseUri).Append(slug);
        var first = true;
        foreach (var (key, value) in query)
        {
            if (value is null) continue;
            sb.Append(first ? '?' : '&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
            first = false;
        }

        return sb.ToString();
    }
}
