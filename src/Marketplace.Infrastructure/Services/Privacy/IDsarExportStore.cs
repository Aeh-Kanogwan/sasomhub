using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Privacy;

/// <summary>
/// M3 DSAR export artifact storage. The DSAR service hands it the serialized JSON package and gets
/// back a path/URI it stores on <c>DataSubjectRequest.ResultArtifactPath</c> (never the data inline).
/// Abstracted so the host (Web/Api) can point it at wwwroot, a config path, or later blob storage,
/// and so tests can substitute an in-memory store.
/// </summary>
public interface IDsarExportStore
{
    /// <summary>Persist the export <paramref name="json"/> and return the stored path/URI for the artifact.</summary>
    Task<string> SaveAsync(Guid userId, Guid requestId, string json, CancellationToken ct = default);
}

/// <summary>
/// Local-filesystem DSAR export store. Writes one JSON file per request under a configurable root
/// (<c>Dsar:ExportRoot</c> / <c>MARKETPLACE_DSAR_EXPORT_DIR</c>, default: a "dsar-exports" folder
/// beside the app). The returned path is the absolute file path; the Web layer maps it to a download
/// link that should expire after the TTL (<see cref="DataSubjectRequestService.ExportTtlDays"/>).
/// </summary>
public sealed class FileSystemDsarExportStore : IDsarExportStore
{
    private readonly string _root;
    private readonly ILogger<FileSystemDsarExportStore> _logger;

    public FileSystemDsarExportStore(IConfiguration configuration, ILogger<FileSystemDsarExportStore> logger)
    {
        _logger = logger;
        _root = Environment.GetEnvironmentVariable("MARKETPLACE_DSAR_EXPORT_DIR")
            ?? configuration["Dsar:ExportRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "dsar-exports");
    }

    public async Task<string> SaveAsync(Guid userId, Guid requestId, string json, CancellationToken ct = default)
    {
        // Per-user subfolder keeps artifacts grouped and easy to purge after the TTL.
        var dir = Path.Combine(_root, userId.ToString("N"));
        Directory.CreateDirectory(dir);
        var fileName = $"dsar-{requestId:N}.json";
        var absolutePath = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(absolutePath, json, ct);
        _logger.LogInformation("DSAR export written for request {RequestId} ({Bytes} bytes)", requestId, json.Length);
        return absolutePath;
    }
}
