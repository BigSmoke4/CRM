using System.Text.Json;

namespace Crm.Web.Services.Integrations;

/// <summary>JSON helpers: path navigation, structure exploration, record extraction.</summary>
public static class JsonExplorer
{
    /// <summary>Get element at a dotted path like "data.items" or "customers[0].name".</summary>
    public static JsonElement? GetPath(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            // handle array index or wildcard-free segments; wildcards handled by caller
            if (current.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(segment, out var idx) || idx >= current.GetArrayLength()) return null;
                current = current[idx];
            }
            else if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(segment, out current)) return null;
            }
            else return null;
        }
        return current;
    }

    /// <summary>Resolve records array: returns enumerable of JsonElements for each record.</summary>
    public static IEnumerable<JsonElement> GetRecords(JsonElement root, string? recordsPath)
    {
        JsonElement? node = string.IsNullOrWhiteSpace(recordsPath) ? root : GetPath(root, recordsPath);
        node ??= root;

        // If node is an object, look for the first array property (common API shape)
        if (node.Value.ValueKind == JsonValueKind.Object && node.Value.ValueKind != JsonValueKind.Undefined)
        {
            if (string.IsNullOrWhiteSpace(recordsPath))
            {
                foreach (var prop in node.Value.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        return prop.Value.EnumerateArray();
            }
            if (node.Value.ValueKind == JsonValueKind.Array)
                return node.Value.EnumerateArray();
            // recordsPath pointed to a wrapper object — look inside it
            foreach (var prop in node.Value.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Array)
                    return prop.Value.EnumerateArray();
            return Enumerable.Empty<JsonElement>();
        }
        if (node.Value.ValueKind == JsonValueKind.Array)
            return node.Value.EnumerateArray();
        if (node.Value.ValueKind == JsonValueKind.Undefined)
            return Enumerable.Empty<JsonElement>();
        // single object → treat as one record
        return new[] { node.Value };
    }

    /// <summary>Extract a scalar value from a record using a dotted path (supports array indices).</summary>
    public static string? GetFieldValue(JsonElement record, string path)
    {
        var el = GetPath(record, path);
        if (el == null) return null;
        return el.Value.ValueKind switch
        {
            JsonValueKind.String => el.Value.GetString(),
            JsonValueKind.Number => el.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.Value.GetRawText()
        };
    }

    /// <summary>Build a tree of JSON structure for the explorer UI.</summary>
    public static object Describe(JsonElement el, int depth = 0)
    {
        if (depth > 4) return el.ValueKind.ToString();
        return el.ValueKind switch
        {
            JsonValueKind.Object => el.EnumerateObject().Take(50)
                .ToDictionary(p => p.Name, p => Describe(p.Value, depth + 1)),
            JsonValueKind.Array => new
            {
                _type = $"array[{el.GetArrayLength()}]",
                _sample = el.GetArrayLength() > 0 ? Describe(el[0], depth + 1) : null
            },
            JsonValueKind.String => new { _type = "string", _value = Truncate(el.GetString(), 80) },
            JsonValueKind.Number => new { _type = "number", _value = el.GetRawText() },
            JsonValueKind.True or JsonValueKind.False => new { _type = "boolean", _value = el.GetRawText() },
            JsonValueKind.Null => new { _type = "null" },
            _ => new { _type = el.ValueKind.ToString() }
        };
    }

    private static string? Truncate(string? s, int max) =>
        s != null && s.Length > max ? s[..max] + "…" : s;
}
