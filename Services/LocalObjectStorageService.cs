using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HRReserveSystem.Services;

public class LocalObjectStorageService(
    IWebHostEnvironment environment,
    ILogger<LocalObjectStorageService> logger) : IObjectStorageService
{
    private readonly string _baseStoragePath = Path.Combine(environment.ContentRootPath, "App_Data", "Storage", "Resumes");
    private readonly byte[] _hmacKey = Encoding.UTF8.GetBytes(environment.ContentRootPath + "_LocalObjectStorageSecret_2026_SecureKey");

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
        var expiresUnix = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        var payload = $"{fileName}:{expiresUnix}";
        using var hmac = new HMACSHA256(_hmacKey);
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        var token = $"{payload}:{signature}";
        var tokenBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        return Task.FromResult<string?>($"/Candidates/DirectDownload?file={Uri.EscapeDataString(fileName)}&token={Uri.EscapeDataString(tokenBase64)}");
    }

    public bool ValidatePresignedToken(string fileName, string tokenBase64)
    {
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(tokenBase64));
            var parts = raw.Split(':');
            if (parts.Length != 3) return false;
            var tokenFile = parts[0];
            if (!string.Equals(tokenFile, fileName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!long.TryParse(parts[1], out var expiresUnix)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnix) return false;
            var payload = $"{tokenFile}:{expiresUnix}";
            using var hmac = new HMACSHA256(_hmacKey);
            var expectedSig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig),
                Encoding.UTF8.GetBytes(parts[2]));
        }
        catch
        {
            return false;
        }
    }

    public Task<(string UploadUrl, string FinalStoragePath)> GeneratePresignedUploadUrlAsync(string fileName, string contentType, TimeSpan expiry, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var safeFileName = $"{Guid.NewGuid():N}{extension}";
        var finalPath = $"/uploads/resumes/{safeFileName}";
        return Task.FromResult(("/api/storage/direct-upload", finalPath));
    }
}
