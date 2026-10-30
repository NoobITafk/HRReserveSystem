namespace HRReserveSystem.Services;

public interface ICandidateFileStorageService
{
    string GetResumeStorageDirectory();
    string GetPhysicalResumePath(string? resumePath);
    Task<string?> SaveResumeFileAsync(IFormFile? resumeFile);
    Task<string?> SaveAvatarFileAsync(IFormFile? avatarFile);
    void DeleteStoredResume(string? resumePath);
    Task DeleteStoredResumeAsync(string? resumePath);
    void DeleteStoredAvatar(string? avatarPath);
    Task DeleteStoredAvatarAsync(string? avatarPath);
    string GetResumeContentType(string fileName);
}
