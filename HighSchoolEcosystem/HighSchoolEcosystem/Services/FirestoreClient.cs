using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Firestore;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

public sealed class FirestoreClient : IFirestoreClient
{
    private readonly HttpClient _httpClient;
    private readonly IFirebaseAuthService _authService;
    private readonly FirebaseEndpoints _endpoints;

    public FirestoreClient(HttpClient httpClient, IFirebaseAuthService authService, FirebaseEndpoints endpoints)
    {
        _httpClient = httpClient;
        _authService = authService;
        _endpoints = endpoints;
    }

    public async Task<T?> GetDocumentAsync<T>(string documentPath, CancellationToken cancellationToken = default) where T : class, new()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(documentPath));
        await ApplyAuthAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = JsonNode.Parse(json) as JsonObject;
        if (document is null || document["fields"] is not JsonObject fields) return null;

        return FirestoreValueConverter.FromFields<T>(fields);
    }

    public async Task SetDocumentAsync<T>(string documentPath, T value, CancellationToken cancellationToken = default) where T : notnull
    {
        var fields = FirestoreValueConverter.ToFields(value);
        var body = new JsonObject { ["fields"] = fields };

        // Without an updateMask, PATCH replaces the whole document. Listing every
        // field we're writing keeps it a merge, so fields we don't know about
        // (set by an admin console, say) survive.
        var maskQuery = string.Join("&", fields.Select(kv => $"updateMask.fieldPaths={Uri.EscapeDataString(kv.Key)}"));
        var uri = BuildUri(documentPath, maskQuery);

        using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        await ApplyAuthAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<FirestoreDocument<T>>> ListDocumentsAsync<T>(string collectionPath, CancellationToken cancellationToken = default) where T : class, new()
    {
        var results = new List<FirestoreDocument<T>>();
        string? pageToken = null;

        do
        {
            var query = "pageSize=300";
            if (pageToken is not null) query += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(collectionPath, query));
            await ApplyAuthAsync(request, cancellationToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            // An empty collection 404s on some paths; treat it as "nothing yet".
            if (response.StatusCode == HttpStatusCode.NotFound) break;
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (JsonNode.Parse(json) is not JsonObject page) break;

            if (page["documents"] is JsonArray documents)
            {
                foreach (var node in documents)
                {
                    if (node is not JsonObject document) continue;
                    if (document["fields"] is not JsonObject fields) continue;

                    // "name" is the full resource path; the ID is the last segment.
                    var name = document["name"]?.GetValue<string>() ?? string.Empty;
                    var id = name[(name.LastIndexOf('/') + 1)..];

                    results.Add(new FirestoreDocument<T>(id, FirestoreValueConverter.FromFields<T>(fields)));
                }
            }

            pageToken = page["nextPageToken"]?.GetValue<string>();
        }
        while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    public async Task<IReadOnlyList<FirestoreDocument<T>>> QueryByTimeAsync<T>(
        string collectionPath, string timeField, bool newestFirst, int limit,
        DateTimeOffset? after = null, CancellationToken cancellationToken = default) where T : class, new()
    {
        // runQuery is posted to the collection's PARENT, naming the collection
        // in the body: "classes/abc/messages" -> parent "classes/abc", id "messages".
        var path = collectionPath.Trim('/');
        var slash = path.LastIndexOf('/');
        var parent = slash < 0 ? string.Empty : path[..slash];
        var collectionId = slash < 0 ? path : path[(slash + 1)..];

        var structuredQuery = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collectionId }),
            ["orderBy"] = new JsonArray(new JsonObject
            {
                ["field"] = new JsonObject { ["fieldPath"] = timeField },
                ["direction"] = newestFirst ? "DESCENDING" : "ASCENDING"
            }),
            ["limit"] = limit
        };

        if (after is { } since)
        {
            // Reuse the converter so the timestamp is encoded exactly as writes are.
            var value = FirestoreValueConverter.ToFields(
                new Dictionary<string, object?> { ["v"] = since })["v"]!.DeepClone();

            structuredQuery["where"] = new JsonObject
            {
                ["fieldFilter"] = new JsonObject
                {
                    ["field"] = new JsonObject { ["fieldPath"] = timeField },
                    ["op"] = "GREATER_THAN",
                    ["value"] = value
                }
            };
        }

        var body = new JsonObject { ["structuredQuery"] = structuredQuery };
        var uri = _endpoints.FirestoreDocumentsBaseUrl.TrimEnd('/')
                + (parent.Length == 0 ? string.Empty : "/" + parent)
                + ":runQuery";

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        await ApplyAuthAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var results = new List<FirestoreDocument<T>>();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // The response is an array of { document?, readTime }. An entry with no
        // document is how Firestore says "no results", so it's skipped.
        if (JsonNode.Parse(json) is JsonArray rows)
        {
            foreach (var row in rows)
            {
                if (row?["document"] is not JsonObject document) continue;
                if (document["fields"] is not JsonObject fields) continue;

                var name = document["name"]?.GetValue<string>() ?? string.Empty;
                var id = name[(name.LastIndexOf('/') + 1)..];
                results.Add(new FirestoreDocument<T>(id, FirestoreValueConverter.FromFields<T>(fields)));
            }
        }

        return results;
    }

    public async Task<int> CountByTimeAsync(
        string collectionPath, string timeField, DateTimeOffset? after,
        CancellationToken cancellationToken = default)
    {
        var (parent, collectionId) = SplitCollectionPath(collectionPath);

        var structuredQuery = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collectionId })
        };
        if (after is { } since)
        {
            structuredQuery["where"] = new JsonObject
            {
                ["fieldFilter"] = new JsonObject
                {
                    ["field"] = new JsonObject { ["fieldPath"] = timeField },
                    ["op"] = "GREATER_THAN",
                    ["value"] = FirestoreValueConverter.ToFields(
                        new Dictionary<string, object?> { ["v"] = since })["v"]!.DeepClone()
                }
            };
        }

        var body = new JsonObject
        {
            ["structuredAggregationQuery"] = new JsonObject
            {
                ["structuredQuery"] = structuredQuery,
                ["aggregations"] = new JsonArray(new JsonObject
                {
                    ["alias"] = "n",
                    ["count"] = new JsonObject()
                })
            }
        };

        var uri = _endpoints.FirestoreDocumentsBaseUrl.TrimEnd('/')
                + (parent.Length == 0 ? string.Empty : "/" + parent)
                + ":runAggregationQuery";

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        await ApplyAuthAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // [ { "result": { "aggregateFields": { "n": { "integerValue": "5" } } } } ]
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (JsonNode.Parse(json) is JsonArray rows)
        {
            foreach (var row in rows)
            {
                var value = row?["result"]?["aggregateFields"]?["n"]?["integerValue"]?.GetValue<string>();
                if (int.TryParse(value, out var count)) return count;
            }
        }
        return 0;
    }

    public async Task<IReadOnlyList<FirestoreDocument<T>>> QueryWhereAsync<T>(
        string collectionPath, string field, string op, object value,
        CancellationToken cancellationToken = default) where T : class, new()
    {
        var (parent, collectionId) = SplitCollectionPath(collectionPath);

        var structuredQuery = new JsonObject
        {
            ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collectionId }),
            ["where"] = new JsonObject
            {
                ["fieldFilter"] = new JsonObject
                {
                    ["field"] = new JsonObject { ["fieldPath"] = field },
                    ["op"] = op,
                    // The converter encodes lists as arrayValue, which is what
                    // ARRAY_CONTAINS_ANY / IN expect.
                    ["value"] = FirestoreValueConverter.ToFields(
                        new Dictionary<string, object?> { ["v"] = value })["v"]!.DeepClone()
                }
            }
        };

        var body = new JsonObject { ["structuredQuery"] = structuredQuery };
        var uri = _endpoints.FirestoreDocumentsBaseUrl.TrimEnd('/')
                + (parent.Length == 0 ? string.Empty : "/" + parent)
                + ":runQuery";

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        await ApplyAuthAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var results = new List<FirestoreDocument<T>>();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (JsonNode.Parse(json) is JsonArray rows)
        {
            foreach (var row in rows)
            {
                if (row?["document"] is not JsonObject document) continue;
                if (document["fields"] is not JsonObject fields) continue;
                var name = document["name"]?.GetValue<string>() ?? string.Empty;
                results.Add(new FirestoreDocument<T>(
                    name[(name.LastIndexOf('/') + 1)..], FirestoreValueConverter.FromFields<T>(fields)));
            }
        }
        return results;
    }

    /// <summary>"classes/abc/messages" -> ("classes/abc", "messages").</summary>
    private static (string Parent, string CollectionId) SplitCollectionPath(string collectionPath)
    {
        var path = collectionPath.Trim('/');
        var slash = path.LastIndexOf('/');
        return slash < 0 ? (string.Empty, path) : (path[..slash], path[(slash + 1)..]);
    }

    public async Task PatchFieldsAsync(string documentPath, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default)
    {
        if (fields.Count == 0) return;

        var body = new JsonObject { ["fields"] = FirestoreValueConverter.ToFields(fields) };
        var maskQuery = string.Join("&", fields.Keys.Select(k => $"updateMask.fieldPaths={Uri.EscapeDataString(k)}"));

        using var request = new HttpRequestMessage(HttpMethod.Patch, BuildUri(documentPath, maskQuery))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        await ApplyAuthAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteDocumentAsync(string documentPath, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUri(documentPath));
        await ApplyAuthAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return; // already gone
        response.EnsureSuccessStatusCode();
    }

    private string BuildUri(string documentPath, string? query = null)
    {
        var uri = _endpoints.FirestoreDocumentsBaseUrl + documentPath.TrimStart('/');
        if (!string.IsNullOrEmpty(query)) uri += $"?{query}";
        return uri;
    }

    private async Task ApplyAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Unauthenticated (visitor) requests are sent with no Authorization header;
        // Firestore security rules decide what's visible to request.auth == null.
        if (!_authService.IsSignedIn) return;

        var idToken = await _authService.GetIdTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(idToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        }
    }
}
