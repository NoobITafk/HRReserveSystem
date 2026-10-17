namespace HRReserveSystem.Services;

public class StorageOptions
{
    public string Provider { get; set; } = "Local"; // "Local" or "MinIO" / "S3"
    public string ServiceUrl { get; set; } = "";
    public string BucketName { get; set; } = "hrreserve-resumes";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public bool ForcePathStyle { get; set; } = true;
}

public interface IObjectStorageService
{
    Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream?> GetFileStreamAsync(string storagePath, CancellationToken ct = default);
    Task<bool> DeleteFileAsync(string storagePath, CancellationToken ct = default);
    string GetContentType(string fileName);
    Task<string?> GeneratePresignedDownloadUrlAsync(string storagePath, TimeSpan expiry, CancellationToken ct = default);
    Task<(string UploadUrl, string FinalStoragePath)> GeneratePresignedUploadUrlAsync(string fileName, string contentType, TimeSpan expiry, CancellationToken ct = default);
}
