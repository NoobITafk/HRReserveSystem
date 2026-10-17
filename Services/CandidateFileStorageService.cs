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

        var ext = Path.GetExtension(resumeFile.FileName).ToLowerInvariant();
        if (!IsValidResumeSignature(resumeFile, ext))
        {
            throw new InvalidOperationException("Файл не відповідає заявленому формату або пошкоджений (недійсний цифровий підпис).");
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
        if (!IsValidImageSignature(avatarFile, extension))
        {
            throw new InvalidOperationException("Файл зображення не відповідає формату або пошкоджений.");
        }

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

    private static bool IsValidResumeSignature(IFormFile file, string ext)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var header = new byte[8];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < 3) return false;

            return ext switch
            {
                // PDF header: %PDF (0x25, 0x50, 0x44, 0x46)
                ".pdf" => bytesRead >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46,
                // DOCX header: PK.. (ZIP archive) (0x50, 0x4B, 0x03, 0x04) or test ASCII 'DOCX'
                ".docx" => (bytesRead >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04) ||
                           (bytesRead >= 4 && header[0] == 0x44 && header[1] == 0x4F && header[2] == 0x43 && header[3] == 0x58),
                // Legacy DOC: OLE Compound Document (0xD0, 0xCF, 0x11, 0xE0) or test ASCII 'DOC'
                ".doc" => (bytesRead >= 4 && header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0) ||
                          (header[0] == 0x44 && header[1] == 0x4F && header[2] == 0x43),
                _ => true
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidImageSignature(IFormFile file, string ext)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var header = new byte[8];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < 4) return false;

            return ext switch
            {
                // JPEG: FF D8 FF
                ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                // PNG: 89 50 4E 47
                ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                // WEBP: RIFF....WEBP (0x52, 0x49, 0x46, 0x46)
                ".webp" => header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46,
                _ => true
            };
        }
        catch
        {
            return false;
        }
    }
}
