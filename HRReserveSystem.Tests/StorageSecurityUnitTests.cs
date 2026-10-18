using HRReserveSystem.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HRReserveSystem.Tests;

public class StorageSecurityUnitTests
{
    private class TestHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "HRReserveSystem";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Test";
    }

    [Fact]
    public async Task LocalObjectStorageService_PresignedToken_ValidSignature_PassesVerification()
    {
        var env = new TestHostEnvironment();
        var service = new LocalObjectStorageService(env, NullLogger<LocalObjectStorageService>.Instance);
        var testFile = "test_resume_12345.pdf";

        var url = await service.GeneratePresignedDownloadUrlAsync($"/uploads/resumes/{testFile}", TimeSpan.FromMinutes(15));
        Assert.NotNull(url);

        var uri = new Uri("http://localhost" + url);
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var token = queryParams["token"];
        Assert.False(string.IsNullOrWhiteSpace(token));

        var isValid = service.ValidatePresignedToken(testFile, token!);
        Assert.True(isValid);
    }

    [Fact]
    public async Task LocalObjectStorageService_PresignedToken_TamperedFileName_FailsVerification()
    {
        var env = new TestHostEnvironment();
        var service = new LocalObjectStorageService(env, NullLogger<LocalObjectStorageService>.Instance);
        var testFile = "original_file.pdf";

        var url = await service.GeneratePresignedDownloadUrlAsync($"/uploads/resumes/{testFile}", TimeSpan.FromMinutes(15));
        var uri = new Uri("http://localhost" + url);
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var token = queryParams["token"];

        // Attacker attempts to use token generated for original_file.pdf to download secret_admin_data.pdf
        var isValid = service.ValidatePresignedToken("secret_admin_data.pdf", token!);
        Assert.False(isValid);
    }
}
