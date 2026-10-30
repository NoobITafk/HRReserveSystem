using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HRReserveSystem.Services;

public class LocalObjectStorageService : IObjectStorageService
{
    private readonly string _storageRootPath;
    private readonly byte[] _hmacKey;
    private readonly ILogger<LocalObjectStorageService> _logger;

    public LocalObjectStorageService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<LocalObjectStorageService> logger)
    {
        _logger = logger;
        _storageRootPath = Path.Combine(environment.ContentRootPath, "App_Data", "Storage");
        _hmacKey = Encoding.UTF8.GetBytes(
            configuration["Storage:SigningSecret"] ?? $"{environment.ContentRootPath}_LocalObjectStorageSecret_2026_SecureKey");
    }

    public LocalObjectStorageService(
        IWebHostEnvironment environment,
        ILogger<LocalObjectStorageService> logger)
        : this(environment, new ConfigurationBuilder().Build(), logger)
    {
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default)
    {
        var isAvatar = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || fileName.Contains("avatar", StringComparison.OrdinalIgnoreCase);
        var subDir = isAvatar ? "Avatars" : "Resumes";
        var dirPath = Path.Combine(_storageRootPath, subDir);
        Directory.CreateDirectory(dirPath);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var safeFileName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(dirPath, safeFileName);

        await using var fileStream = System.IO.File.Create(physicalPath);
        await stream.CopyToAsync(fileStream, ct);

        _logger.LogInformation("Saved object storage file to {PhysicalPath}", physicalPath);
        return isAvatar ? $"/uploads/avatars/{safeFileName}" : $"/uploads/resumes/{safeFileName}";
    }

    public Task<Stream?> GetFileStreamAsync(string storagePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(storagePath);
        var resumePath = Path.Combine(_storageRootPath, "Resumes", fileName);
        var avatarPath = Path.Combine(_storageRootPath, "Avatars", fileName);

        var physicalPath = System.IO.File.Exists(resumePath) ? resumePath : (System.IO.File.Exists(avatarPath) ? avatarPath : null);
        if (physicalPath is null)
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = System.IO.File.OpenRead(physicalPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteFileAsync(string storagePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(storagePath);
        var resumePath = Path.Combine(_storageRootPath, "Resumes", fileName);
        var avatarPath = Path.Combine(_storageRootPath, "Avatars", fileName);

        var deleted = false;
        if (System.IO.File.Exists(resumePath))
        {
            System.IO.File.Delete(resumePath);
            deleted = true;
        }

        if (System.IO.File.Exists(avatarPath))
        {
            System.IO.File.Delete(avatarPath);
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
        var tokenBase64 = GeneratePresignedToken(fileName, expiry);
        return Task.FromResult<string?>($"/Candidates/DirectDownload?file={Uri.EscapeDataString(fileName)}&token={Uri.EscapeDataString(tokenBase64)}");
    }

    public string GeneratePresignedToken(string payload, TimeSpan expiry)
    {
        var expiresUnix = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        var rawPayload = $"{payload}:{expiresUnix}";
        using var hmac = new HMACSHA256(_hmacKey);
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload)));
        var token = $"{rawPayload}:{signature}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
    }

    public bool ValidatePresignedToken(string payload, string tokenBase64)
    {
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(tokenBase64));
            var parts = raw.Split(':');
            if (parts.Length != 3) return false;
            var tokenPayload = parts[0];
            if (!string.Equals(tokenPayload, payload, StringComparison.OrdinalIgnoreCase)) return false;
            if (!long.TryParse(parts[1], out var expiresUnix)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnix) return false;
            var rawPayload = $"{tokenPayload}:{expiresUnix}";
            using var hmac = new HMACSHA256(_hmacKey);
            var expectedSig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig),
                Encoding.UTF8.GetBytes(parts[2]));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to validate presigned token for payload {Payload}", payload);
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
