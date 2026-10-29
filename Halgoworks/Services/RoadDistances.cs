using System.Globalization;
using System.Text;
using System.Text.Json;
using Halgoworks.Core.Vrp;

namespace Halgoworks.Services;

/// <summary>
/// Road distances and route shapes from the public OSRM demo server (OpenStreetMap data). Its usage policy
/// asks for at most one request per second, so requests queue up behind each other, and every answer is
/// cached for the session. Any failure comes back as a short reason; callers fall back to straight lines.
/// </summary>
public sealed class RoadDistances : IDisposable
{
    public const string Server = "https://router.project-osrm.org";

    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(1100);

    private readonly HttpClient _http = new() { BaseAddress = new Uri(Server), Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, double[]> _tables = [];
    private readonly Dictionary<string, IReadOnlyList<double[]>> _shapes = [];
    private DateTime _last = DateTime.MinValue;

    /// <summary>Identifies an instance's stop locations, so stale answers can be told apart.</summary>
    public static string Key(VrpInstance instance) =>
        string.Join(";", Enumerable.Range(0, instance.Customers.Count + 1).Select(n => Coord(instance.LatLon(n))));

    /// <summary>
    /// Driving distances in km between every pair of nodes (depot first), row-major. OSRM's distances differ a little
    /// by direction because of one-way streets; the algorithms here assume symmetric distances, so each pair gets the average.
    /// </summary>
    public async Task<(double[]? Km, string? Problem)> TableAsync(VrpInstance instance, CancellationToken cancel = default)
    {
        var key = Key(instance);
        if (_tables.TryGetValue(key, out var cached)) return (cached, null);
        var (json, problem) = await GetAsync($"/table/v1/driving/{key}?annotations=distance", cancel);
        if (json is null) return (null, problem);
        using var doc = json;
        var n = instance.Customers.Count + 1;
        if (!doc.RootElement.TryGetProperty("distances", out var rows) || rows.GetArrayLength() != n) return (null, "the routing server sent an unexpected answer");

        var meters = new double?[n, n];
        var i = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var j = 0;
            foreach (var cell in row.EnumerateArray())
            {
                meters[i, j] = cell.ValueKind == JsonValueKind.Number ? cell.GetDouble() : null;
                j++;
            }

            i++;
        }

        var km = new double[n * n];
        for (var a = 0; a < n; a++)
        for (var b = 0; b < n; b++)
        {
            if (a == b) continue;
            if (meters[a, b] is not { } ab || meters[b, a] is not { } ba) return (null, "some stops can't be reached by road");
            km[a * n + b] = Math.Round((ab + ba) / 2 / 1000, 2);
        }

        _tables[key] = km;
        return (km, null);
    }

    /// <summary>The road path through the given nodes, as [lat, lon] points, or null if the server can't say.</summary>
    public async Task<IReadOnlyList<double[]>?> ShapeAsync(VrpInstance instance, IReadOnlyList<int> nodes, CancellationToken cancel = default)
    {
        var key = string.Join(";", nodes.Select(n => Coord(instance.LatLon(n))));
        if (_shapes.TryGetValue(key, out var cached)) return cached;
        var (json, _) = await GetAsync($"/route/v1/driving/{key}?overview=full&geometries=geojson", cancel);
        if (json is null) return null;
        using var doc = json;
        try
        {
            var coordinates = doc.RootElement.GetProperty("routes")[0].GetProperty("geometry").GetProperty("coordinates");
            var points = coordinates.EnumerateArray().Select(p => new[] { p[1].GetDouble(), p[0].GetDouble() }).ToList();
            _shapes[key] = points;
            return points;
        }
        catch (Exception e) when (e is KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<(JsonDocument? Json, string? Problem)> GetAsync(string path, CancellationToken cancel)
    {
        await _gate.WaitAsync(cancel);
        try
        {
            var wait = _last + Spacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancel);
            _last = DateTime.UtcNow;
            using var response = await _http.GetAsync(path, cancel);
            if ((int)response.StatusCode == 429) return (null, "the public routing server is busy right now");
            if (!response.IsSuccessStatusCode) return (null, $"the routing server answered {(int)response.StatusCode}");
            var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.GetString() != "Ok")
            {
                var message = code.GetString();
                doc.Dispose();
                return (null, $"the routing server couldn't plan these stops ({message})");
            }

            return (doc, null);
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return (null, "the routing server didn't answer in time");
        }
        catch (HttpRequestException)
        {
            return (null, "the routing server can't be reached from here");
        }
        catch (JsonException)
        {
            return (null, "the routing server sent an unreadable answer");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Coord((double Lat, double Lon) p) =>
        new StringBuilder().Append(p.Lon.ToString("0.######", CultureInfo.InvariantCulture)).Append(',').Append(p.Lat.ToString("0.######", CultureInfo.InvariantCulture)).ToString();

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
