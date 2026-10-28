using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Shared.Application.Behaviors;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Shared.Application.Tests;

public sealed class LoggingBehaviorTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task Success_LogsTheRequestNameAndDuration()
    {
        var logger = new FakeLogger<LoggingBehavior<PingCommand, Result>>();
        var behavior = new LoggingBehavior<PingCommand, Result>(logger, _time);

        await behavior.Handle(new PingCommand(), Advance(250, Result.Success()), TestContext.Current.CancellationToken);

        var record = logger.LatestRecord;
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(nameof(PingCommand), record.GetStructuredStateValue("RequestName"));
        Assert.Contains("250", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_LogsTheErrorCodeAndType()
    {
        var logger = new FakeLogger<LoggingBehavior<FailingCommand, Result>>();
        var behavior = new LoggingBehavior<FailingCommand, Result>(logger, _time);

        await behavior.Handle(new FailingCommand(), Advance(10, Result.Failure(FailingHandler.Conflict)), TestContext.Current.CancellationToken);

        var record = logger.LatestRecord;
        Assert.Equal(FailingHandler.Conflict.Code, record.GetStructuredStateValue("ErrorCode"));
        Assert.Equal(nameof(ErrorType.Conflict), record.GetStructuredStateValue("ErrorType"));
    }

    [Fact]
    public async Task RequestContents_AreNotLogged()
    {
        const string secret = "s3cr3t-password";
        var logger = new FakeLogger<LoggingBehavior<LoginCommand, Result>>();
        var behavior = new LoggingBehavior<LoginCommand, Result>(logger, _time);

        await behavior.Handle(new LoginCommand("user@example.com", secret), Advance(1, Result.Success()), TestContext.Current.CancellationToken);

        Assert.All(logger.Collector.GetSnapshot(), record =>
        {
            Assert.DoesNotContain(secret, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("user@example.com", record.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Exception_PropagatesWithoutAnErrorLog()
    {
        var logger = new FakeLogger<LoggingBehavior<ThrowingCommand, Result>>();
        var behavior = new LoggingBehavior<ThrowingCommand, Result>(logger, _time);

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new ThrowingCommand(),
            _ => throw new InvalidOperationException("Boom."),
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Error);
    }

    private RequestHandlerDelegate<Result> Advance(int milliseconds, Result result) => _ =>
    {
        _time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        return Task.FromResult(result);
    };

    private sealed record LoginCommand(string Email, string Password) : ICommand;
}
