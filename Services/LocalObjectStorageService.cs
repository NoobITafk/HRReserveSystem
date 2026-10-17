using Microsoft.Extensions.Options;

namespace HRReserveSystem.Services;

public class LocalObjectStorageService(
    IWebHostEnvironment environment,
    ILogger<LocalObjectStorageService> logger) : IObjectStorageService
{
    private readonly string _baseStoragePath = Path.Combine(environment.ContentRootPath, "App_Data", "Storage", "Resumes");

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_baseStoragePath);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var safeFileName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(_baseStoragePath, safeFileName);

        await using var fileStream = System.IO.File.Create(physicalPath);
        await stream.CopyToAsync(fileStream, ct);

        logger.LogInformation("Saved object storage file to {PhysicalPath}", physicalPath);
        return $"/uploads/resumes/{safeFileName}";
    }

    public Task<Stream?> GetFileStreamAsync(string storagePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(storagePath);
        var physicalPath = Path.Combine(_baseStoragePath, fileName);

        if (!System.IO.File.Exists(physicalPath))
        {
            var legacyPath = Path.Combine(environment.WebRootPath, "uploads", "resumes", fileName);
            if (System.IO.File.Exists(legacyPath))
            {
                physicalPath = legacyPath;
            }
            else
            {
                return Task.FromResult<Stream?>(null);
            }
        }

        Stream stream = System.IO.File.OpenRead(physicalPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteFileAsync(string storagePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(storagePath);
        var physicalPath = Path.Combine(_baseStoragePath, fileName);
        var deleted = false;

        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
            deleted = true;
        }

        var legacyPath = Path.Combine(environment.WebRootPath, "uploads", "resumes", fileName);
        if (System.IO.File.Exists(legacyPath))
        {
            System.IO.File.Delete(legacyPath);
            deleted = true;
        }

        return Task.FromResult(deleted);
    }

    public string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }

    public Task<string?> GeneratePresignedDownloadUrlAsync(string storagePath, TimeSpan expiry, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(storagePath);
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{fileName}:{DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds()}"));
        return Task.FromResult<string?>($"/Candidates/DownloadResume?file={Uri.EscapeDataString(fileName)}&token={Uri.EscapeDataString(token)}");
    }

    public Task<(string UploadUrl, string FinalStoragePath)> GeneratePresignedUploadUrlAsync(string fileName, string contentType, TimeSpan expiry, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var safeFileName = $"{Guid.NewGuid():N}{extension}";
        var finalPath = $"/uploads/resumes/{safeFileName}";
        return Task.FromResult(("/api/storage/direct-upload", finalPath));
    }
}
