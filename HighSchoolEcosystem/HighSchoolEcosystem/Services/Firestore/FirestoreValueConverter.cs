using System.Reflection;
using System.Text.Json.Nodes;

namespace HighSchoolEcosystem.Services.Firestore;

/// <summary>
/// Converts plain POCOs to/from the Firestore REST API's "Value" JSON shape
/// (https://firebase.google.com/docs/firestore/reference/rest/v1/Value), so
/// the rest of the app can work with ordinary C# models instead of the raw
/// wire format. Reflection-based and intentionally minimal: it supports the
/// property types our models actually use (string, bool, int/long, double,
/// enum, List&lt;string&gt;). Extend as new data shapes are introduced.
/// </summary>
public static class FirestoreValueConverter
{
    public static JsonObject ToFields(object model)
    {
        var fields = new JsonObject();
        foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead) continue;
            var value = property.GetValue(model);
            var firestoreValue = ToValue(value);
            if (firestoreValue is not null)
            {
                fields[property.Name] = firestoreValue;
            }
        }
        return fields;
    }

    /// <summary>
    /// Builds a fields object from an explicit name/value map, for partial
    /// updates where writing the whole model would clobber fields this client
    /// doesn't know about.
    /// </summary>
    public static JsonObject ToFields(IReadOnlyDictionary<string, object?> values)
    {
        var fields = new JsonObject();
        foreach (var (name, value) in values)
        {
            var firestoreValue = ToValue(value);
            if (firestoreValue is not null) fields[name] = firestoreValue;
        }
        return fields;
    }

    private static JsonObject? ToValue(object? value)
    {
        switch (value)
        {
            case null:
                return new JsonObject { ["nullValue"] = null };
            case string s:
                return new JsonObject { ["stringValue"] = s };
            case bool b:
                return new JsonObject { ["booleanValue"] = b };
            case int or long:
                return new JsonObject { ["integerValue"] = value.ToString() };
            case double or float:
                return new JsonObject { ["doubleValue"] = Convert.ToDouble(value) };
            case Enum e:
                return new JsonObject { ["stringValue"] = e.ToString() };
            case DateTimeOffset dto:
                // Firestore timestamps are RFC 3339 in UTC.
                return new JsonObject { ["timestampValue"] = dto.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") };
            case DateTime dt:
                return new JsonObject { ["timestampValue"] = dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") };
            case System.Collections.IEnumerable enumerable and not string:
                var values = new JsonArray();
                foreach (var item in enumerable)
                {
                    var itemValue = ToValue(item);
                    if (itemValue is not null) values.Add(itemValue);
                }
                return new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = values } };
            default:
                return new JsonObject { ["stringValue"] = value.ToString() };
        }
    }

    public static T FromFields<T>(JsonObject fields) where T : new()
    {
        var model = new T();
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite) continue;
            if (!fields.TryGetPropertyValue(property.Name, out var node) || node is not JsonObject valueNode) continue;

            var value = FromValue(valueNode, property.PropertyType);
            if (value is not null)
            {
                property.SetValue(model, value);
            }
        }
        return model;
    }

    private static object? FromValue(JsonObject valueNode, Type targetType)
    {
        if (valueNode.TryGetPropertyValue("stringValue", out var stringNode) && stringNode is not null)
        {
            var raw = stringNode.GetValue<string>();
            if (targetType.IsEnum) return Enum.Parse(targetType, raw, ignoreCase: true);
            return raw;
        }

        if (valueNode.TryGetPropertyValue("timestampValue", out var timestampNode) && timestampNode is not null)
        {
            var raw = timestampNode.GetValue<string>();
            if (!DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return null;
            }
            return targetType == typeof(DateTime) ? parsed.UtcDateTime : parsed;
        }

        if (valueNode.TryGetPropertyValue("booleanValue", out var boolNode) && boolNode is not null)
            return boolNode.GetValue<bool>();

        if (valueNode.TryGetPropertyValue("integerValue", out var intNode) && intNode is not null)
        {
            var raw = intNode.GetValue<string>();
            if (targetType == typeof(long)) return long.Parse(raw);
            return int.Parse(raw);
        }

        if (valueNode.TryGetPropertyValue("doubleValue", out var doubleNode) && doubleNode is not null)
            return doubleNode.GetValue<double>();

        if (valueNode.TryGetPropertyValue("arrayValue", out var arrayNode) && arrayNode is JsonObject arrayObj)
        {
            var list = new List<string>();
            if (arrayObj.TryGetPropertyValue("values", out var valuesNode) && valuesNode is JsonArray valuesArray)
            {
                foreach (var item in valuesArray)
                {
                    if (item is JsonObject itemObj && itemObj.TryGetPropertyValue("stringValue", out var itemString) && itemString is not null)
                    {
                        list.Add(itemString.GetValue<string>());
                    }
                }
            }
            return list;
        }

        return null;
    }
}
