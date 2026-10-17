namespace HRReserveSystem.Services;

public class CandidateFileStorageService(
    IWebHostEnvironment environment,
    IObjectStorageService objectStorage) : ICandidateFileStorageService
{
    public string GetResumeStorageDirectory()
    {
        return Path.Combine(environment.ContentRootPath, "App_Data", "Storage", "Resumes");
    }

    public string GetPhysicalResumePath(string? resumePath)
    {
        if (string.IsNullOrWhiteSpace(resumePath))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(resumePath);
        var privatePath = Path.Combine(GetResumeStorageDirectory(), fileName);
        if (System.IO.File.Exists(privatePath))
        {
            return privatePath;
        }

        var legacyPath = Path.Combine(environment.WebRootPath, "uploads", "resumes", fileName);
        if (System.IO.File.Exists(legacyPath))
        {
            return legacyPath;
        }

        return privatePath;
    }

    public async Task<string?> SaveResumeFileAsync(IFormFile? resumeFile)
    {
        if (resumeFile is null || resumeFile.Length == 0)
        {
            return null;
        }

        await using var stream = resumeFile.OpenReadStream();
        return await objectStorage.UploadFileAsync(stream, resumeFile.FileName, resumeFile.ContentType);
    }

    public async Task<string?> SaveAvatarFileAsync(IFormFile? avatarFile)
    {
        if (avatarFile is null || avatarFile.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
        var uploadsPath = Path.Combine(environment.WebRootPath, "uploads", "avatars");
        Directory.CreateDirectory(uploadsPath);

        var fileName = $"candidate_{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadsPath, fileName);

        await using var stream = System.IO.File.Create(physicalPath);
        await avatarFile.CopyToAsync(stream);

        return $"/uploads/avatars/{fileName}";
    }

    public void DeleteStoredResume(string? resumePath)
    {
        if (string.IsNullOrWhiteSpace(resumePath) || !resumePath.StartsWith("/uploads/resumes/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = objectStorage.DeleteFileAsync(resumePath);
    }

    public void DeleteStoredAvatar(string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath) || !avatarPath.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(avatarPath);
        var physicalPath = Path.Combine(environment.WebRootPath, "uploads", "avatars", fileName);
        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }

    public string GetResumeContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }
}
