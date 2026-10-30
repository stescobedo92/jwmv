using System.Text.Json;

namespace Jwmv.Infrastructure.Storage;

internal static class JsonFileHelper
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<T?> ReadAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return default;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }

    /// <summary>
    /// Serializes to a temporary file next to the target and moves it into place, so a crash or serialization
    /// failure never leaves a truncated config, manifest or catalog cache behind.
    /// </summary>
    public static async Task WriteAsync<T>(string filePath, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():n}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
