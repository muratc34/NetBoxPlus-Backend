using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Shared.Application.Validation;
using Shared.Domain;
using Shared.Infrastructure.API.Errors;

namespace Shared.Infrastructure.API.Tests;

public sealed class ErrorResponseTests
{
    private const string ExceptionMessage = "connection string Password=hunter2";

    [Fact]
    public async Task FailedResult_IsWrittenAsProblemDetails()
    {
        await using var app = await TestApp.StartAsync(Map);

        var response = await app.Client.GetAsync("/conflict", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await Json(response);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal("Conflict", body.GetProperty("title").GetString());
        Assert.Equal("The item already exists.", body.GetProperty("detail").GetString());
        Assert.Equal("https://netboxplus.dev/errors/item-conflict", body.GetProperty("type").GetString());
        Assert.Equal("Item.Conflict", body.GetProperty("code").GetString());
        Assert.Equal("/conflict", body.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ValidationError_ListsFieldErrorsAndCodes()
    {
        await using var app = await TestApp.StartAsync(Map);

        var response = await app.Client.GetAsync("/invalid", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Email is required.", body.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("Register.EmailRequired", body.GetProperty("errorCodes").GetProperty("email")[0].GetString());
        Assert.Equal(ValidationError.ErrorCode, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SuccessfulResultWithValue_Returns200WithTheValue()
    {
        await using var app = await TestApp.StartAsync(Map);

        var response = await app.Client.GetAsync("/value", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(42, await response.Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuccessfulResultWithoutValue_Returns204()
    {
        await using var app = await TestApp.StartAsync(Map);

        var response = await app.Client.GetAsync("/done", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_Returns500WithoutLeakingDetails()
    {
        await using var app = await TestApp.StartAsync(Map);

        var response = await app.Client.GetAsync("/throw", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledException_IsLoggedOnceAsError()
    {
        await using var app = await TestApp.StartAsync(Map);

        await app.Client.GetAsync("/throw", TestContext.Current.CancellationToken);

        var errors = app.Services.GetFakeLogCollector().GetSnapshot().Where(r => r.Level >= LogLevel.Error).ToList();
        var record = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(record.Exception);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        await using var app = await TestApp.StartAsync(Map);

        var response = await app.Client.GetAsync("/missing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TypeBaseUriFromConfiguration_IsUsed()
    {
        await using var app = await TestApp.StartAsync(Map, configure: builder =>
            builder.Configuration["ErrorResponses:TypeBaseUri"] = "https://errors.example/v1/");

        var body = await Json(await app.Client.GetAsync("/conflict", TestContext.Current.CancellationToken));

        Assert.Equal("https://errors.example/v1/item-conflict", body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task InvalidTypeBaseUri_FailsAtStartup()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => TestApp.StartAsync(Map, configure: builder =>
            builder.Configuration["ErrorResponses:TypeBaseUri"] = "https://errors.example/no-trailing-slash"));
    }

    private static void Map(WebApplication app)
    {
        app.MapGet("/conflict", () => Result.Failure(Error.Conflict("Item.Conflict", "The item already exists.")).ToHttpResult());
        app.MapGet("/invalid", () => Result.Failure<int>(new ValidationError(
            [new ValidationFailure("Email", "Register.EmailRequired", "Email is required.")])).ToHttpResult());
        app.MapGet("/value", () => Result.Success(42).ToHttpResult());
        app.MapGet("/done", () => Result.Success().ToHttpResult());
        app.MapGet("/throw", IResult () => throw new InvalidOperationException(ExceptionMessage));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
}
