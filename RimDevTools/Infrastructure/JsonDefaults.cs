using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimDevTools;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions CliOutput = Create(writeIndented: true, ignoreNulls: true);
    public static readonly JsonSerializerOptions Storage = Create(writeIndented: false, ignoreNulls: false);
    public static readonly JsonSerializerOptions StorageIndented = Create(writeIndented: true, ignoreNulls: false);

    private static JsonSerializerOptions Create(bool writeIndented, bool ignoreNulls)
        => new()
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = ignoreNulls ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
}
