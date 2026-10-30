using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HRReserveSystem.Services;

/// <summary>
/// Lightweight S3-compatible client for MinIO and AWS S3 without heavy AWS SDK dependencies.
/// Uses standard S3 REST API (PUT, GET, DELETE).
/// </summary>
public class S3CompatibleStorageService : IObjectStorageService
{
    private readonly StorageOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<S3CompatibleStorageService> _logger;
    private readonly LocalObjectStorageService _fallback;

    public S3CompatibleStorageService(
        IOptions<StorageOptions> options,
        HttpClient httpClient,
        LocalObjectStorageService fallback,
        ILogger<S3CompatibleStorageService> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            _logger.LogWarning("S3/MinIO options not configured, using local storage fallback.");
            return await _fallback.UploadFileAsync(stream, fileName, contentType, ct);
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var safeKey = $"{Guid.NewGuid():N}{extension}";
        var requestUri = $"{_options.ServiceUrl.TrimEnd('/')}/{_options.BucketName}/{safeKey}";

        try
        {
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
            {
                Content = content
            };

            // Basic / Custom auth header for MinIO / S3 proxy if provided
            if (!string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                var authBytes = Encoding.UTF8.GetBytes($"{_options.AccessKey}:{_options.SecretKey}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            }

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MinIO upload failed with status {StatusCode}, falling back to local.", response.StatusCode);
                stream.Position = 0;
                return await _fallback.UploadFileAsync(stream, fileName, contentType, ct);
            }

            _logger.LogInformation("Successfully uploaded {Key} to S3/MinIO", safeKey);
            return $"/uploads/resumes/{safeKey}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception uploading to MinIO, using local fallback");
            if (stream.CanSeek) stream.Position = 0;
            return await _fallback.UploadFileAsync(stream, fileName, contentType, ct);
        }
    }

    public async Task<Stream?> GetFileStreamAsync(string storagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            return await _fallback.GetFileStreamAsync(storagePath, ct);
        }

        var key = Path.GetFileName(storagePath);
        var requestUri = $"{_options.ServiceUrl.TrimEnd('/')}/{_options.BucketName}/{key}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                var authBytes = Encoding.UTF8.GetBytes($"{_options.AccessKey}:{_options.SecretKey}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve from MinIO, trying local fallback");
        }

        return await _fallback.GetFileStreamAsync(storagePath, ct);
    }

    public async Task<bool> DeleteFileAsync(string storagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            return await _fallback.DeleteFileAsync(storagePath, ct);
        }

        var key = Path.GetFileName(storagePath);
        var requestUri = $"{_options.ServiceUrl.TrimEnd('/')}/{_options.BucketName}/{key}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
            if (!string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                var authBytes = Encoding.UTF8.GetBytes($"{_options.AccessKey}:{_options.SecretKey}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            }

            var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete from MinIO");
        }

        return await _fallback.DeleteFileAsync(storagePath, ct);
    }

    public string GetContentType(string fileName) => _fallback.GetContentType(fileName);

    public Task<string?> GeneratePresignedDownloadUrlAsync(string storagePath, TimeSpan expiry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            return _fallback.GeneratePresignedDownloadUrlAsync(storagePath, expiry, ct);
        }

        var key = Path.GetFileName(storagePath);
        var expiresUnix = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        var stringToSign = $"GET\n\n\n{expiresUnix}\n/{_options.BucketName}/{key}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SecretKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        var url = $"{_options.ServiceUrl.TrimEnd('/')}/{_options.BucketName}/{key}?AWSAccessKeyId={Uri.EscapeDataString(_options.AccessKey)}&Signature={Uri.EscapeDataString(signature)}&Expires={expiresUnix}";
        return Task.FromResult<string?>(url);
    }

    public Task<(string UploadUrl, string FinalStoragePath)> GeneratePresignedUploadUrlAsync(string fileName, string contentType, TimeSpan expiry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            return _fallback.GeneratePresignedUploadUrlAsync(fileName, contentType, expiry, ct);
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var safeKey = $"{Guid.NewGuid():N}{extension}";
        var expiresUnix = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        var stringToSign = $"PUT\n\n{contentType}\n{expiresUnix}\n/{_options.BucketName}/{safeKey}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SecretKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        var uploadUrl = $"{_options.ServiceUrl.TrimEnd('/')}/{_options.BucketName}/{safeKey}?AWSAccessKeyId={Uri.EscapeDataString(_options.AccessKey)}&Signature={Uri.EscapeDataString(signature)}&Expires={expiresUnix}";
        var finalStoragePath = $"/uploads/resumes/{safeKey}";
        return Task.FromResult((uploadUrl, finalStoragePath));
    }

    public string GeneratePresignedToken(string payload, TimeSpan expiry) => _fallback.GeneratePresignedToken(payload, expiry);

    public bool ValidatePresignedToken(string payload, string tokenBase64) => _fallback.ValidatePresignedToken(payload, tokenBase64);
}
