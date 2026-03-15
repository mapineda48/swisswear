using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SwissWear.Domain.Entities;
using SwissWear.Infrastructure.Data;
using SwissWear.Domain.Contracts;
using SwissWear.Infrastructure.Services;

namespace SwissWear.Tests;

public class ChatAuditServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ChatAuditService _service;

    public ChatAuditServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        _serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();

        var logger = new Mock<ILogger<ChatAuditService>>();

        _service = new ChatAuditService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            config,
            logger.Object);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public void Constructor_ThrowsWhenConnectionStringMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new ChatAuditService(
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                config,
                new Mock<ILogger<ChatAuditService>>().Object));
    }

    [Fact]
    public async Task LogMessageAsync_TextMessage_PersistsToDatabase()
    {
        var message = new ChatMessage("u1", "User-abc", "Hello world", DateTime.UtcNow);

        await _service.LogMessageAsync(message, "10.0.0.1", "Mozilla/5.0");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.ChatMessageLogs.SingleAsync();

        Assert.Equal("User-abc", log.UserName);
        Assert.Equal("Text", log.MessageType);
        Assert.Equal("Hello world", log.MessageText);
        Assert.Null(log.MediaUrls);
        Assert.Equal("10.0.0.1", log.SenderIp);
        Assert.Equal("Mozilla/5.0", log.UserAgent);
    }

    [Fact]
    public async Task LogMessageAsync_EmptyText_StoresNull()
    {
        var message = new ChatMessage("u1", "User-abc", "   ", DateTime.UtcNow);

        await _service.LogMessageAsync(message, "10.0.0.1", null);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.ChatMessageLogs.SingleAsync();

        Assert.Null(log.MessageText);
    }

    [Fact]
    public async Task LogMessageAsync_TruncatesLongUserAgent()
    {
        var longAgent = new string('A', 600);
        var message = new ChatMessage("u1", "User-abc", "Hi", DateTime.UtcNow);

        await _service.LogMessageAsync(message, "10.0.0.1", longAgent);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.ChatMessageLogs.SingleAsync();

        Assert.Equal(500, log.UserAgent!.Length);
    }

    [Fact]
    public async Task LogMessageAsync_DoesNotThrow_OnError()
    {
        // Use a disposed provider to simulate a DB error
        var badServices = new ServiceCollection();
        badServices.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase("bad-db"));
        var badProvider = badServices.BuildServiceProvider();
        badProvider.Dispose(); // Force failure

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();

        var service = new ChatAuditService(
            badProvider.GetRequiredService<IServiceScopeFactory>(),
            config,
            new Mock<ILogger<ChatAuditService>>().Object);

        var message = new ChatMessage("u1", "User-abc", "Hello", DateTime.UtcNow);

        // Should not throw
        await service.LogMessageAsync(message, "10.0.0.1", null);
    }

    [Fact]
    public void IChatAuditService_IsImplemented()
    {
        Assert.IsAssignableFrom<IChatAuditService>(_service);
    }
}
