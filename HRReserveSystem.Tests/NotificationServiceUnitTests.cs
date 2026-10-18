using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRReserveSystem.Tests;

public class NotificationServiceUnitTests
{
    private static (ApplicationDbContext Db, SqliteConnection Connection) CreateInMemorySqlite()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    [Fact]
    public async Task MarkAsReadAsync_WhenTargetIsBroadcastNotification_Succeeds()
    {
        var (context, conn) = CreateInMemorySqlite();
        using (conn)
        using (context)
        {
            var notification = new AppNotification
            {
                Title = "Broadcast Notice",
                Message = "System upgrade",
                TargetUrl = "/Home",
                RecruiterId = null,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            context.AppNotifications.Add(notification);
            await context.SaveChangesAsync();

            var service = new AppNotificationService(context);
            var (success, forbidden) = await service.MarkAsReadAsync(notification.Id, "recruiter1", isAdmin: false);

            Assert.True(success);
            Assert.False(forbidden);

            var updated = await context.AppNotifications.FindAsync(notification.Id);
            Assert.True(updated!.IsRead);
        }
    }

    [Fact]
    public async Task MarkAsReadAsync_WhenTargetBelongsToAnotherRecruiterAndUserIsNotAdmin_ReturnsForbidden_IDOR_Defense()
    {
        var (context, conn) = CreateInMemorySqlite();
        using (conn)
        using (context)
        {
            var recruiter1 = new Recruiter { FullName = "Recruiter One", Email = "r1@test.com", Login = "recruiter1", Role = "Recruiter" };
            var recruiter2 = new Recruiter { FullName = "Recruiter Two", Email = "r2@test.com", Login = "recruiter2", Role = "Recruiter" };
            context.Recruiters.AddRange(recruiter1, recruiter2);
            await context.SaveChangesAsync();

            var notification = new AppNotification
            {
                Title = "Private Offer",
                Message = "Confidential",
                TargetUrl = "/Candidates",
                RecruiterId = recruiter2.Id.ToString(), // Owned by recruiter2
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            context.AppNotifications.Add(notification);
            await context.SaveChangesAsync();

            var service = new AppNotificationService(context);
            // Recruiter1 attempts to mark Recruiter2's notification as read
            var (success, forbidden) = await service.MarkAsReadAsync(notification.Id, "recruiter1", isAdmin: false);

            Assert.False(success);
            Assert.True(forbidden);

            var unchanged = await context.AppNotifications.FindAsync(notification.Id);
            Assert.False(unchanged!.IsRead);
        }
    }

    [Fact]
    public async Task MarkAsReadAsync_WhenTargetBelongsToAnotherRecruiterButUserIsAdmin_Succeeds()
    {
        var (context, conn) = CreateInMemorySqlite();
        using (conn)
        using (context)
        {
            var recruiter = new Recruiter { FullName = "Staff Recruiter", Email = "staff@test.com", Login = "staff", Role = "Recruiter" };
            context.Recruiters.Add(recruiter);
            await context.SaveChangesAsync();

            var notification = new AppNotification
            {
                Title = "Staff Task",
                Message = "Review",
                TargetUrl = "/Candidates",
                RecruiterId = recruiter.Id.ToString(),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            context.AppNotifications.Add(notification);
            await context.SaveChangesAsync();

            var service = new AppNotificationService(context);
            var (success, forbidden) = await service.MarkAsReadAsync(notification.Id, "adminUser", isAdmin: true);

            Assert.True(success);
            Assert.False(forbidden);

            var updated = await context.AppNotifications.FindAsync(notification.Id);
            Assert.True(updated!.IsRead);
        }
    }
}
