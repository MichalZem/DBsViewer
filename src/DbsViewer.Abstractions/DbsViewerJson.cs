using System.Text.Json;
using System.Text.Json.Serialization;

namespace DbsViewer;

/// <summary>
/// Serializační kontext generovaný při kompilaci. Blazor WebAssembly díky němu
/// nepotřebuje reflexi a jde trimmovat.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(DatabaseSchema))]
[JsonSerializable(typeof(DbTable))]
[JsonSerializable(typeof(DbRelationship))]
[JsonSerializable(typeof(IReadOnlyList<DbTable>))]
public sealed partial class DbsViewerJsonContext : JsonSerializerContext;

/// <summary>Předpřipravené nastavení serializace, aby server a klient nikdy nerozešly.</summary>
public static class DbsViewerJson
{
    /// <summary>Kompaktní varianta pro přenos po síti.</summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    /// <summary>Čitelná varianta pro export do souboru.</summary>
    public static JsonSerializerOptions Readable { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
