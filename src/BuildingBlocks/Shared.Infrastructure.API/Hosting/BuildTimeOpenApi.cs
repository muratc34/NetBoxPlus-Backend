using System.Reflection;

namespace Shared.Infrastructure.API.Hosting;

/// <summary>
/// Build-time OpenAPI generation (Microsoft.Extensions.ApiDescription.Server) builds the app inside
/// the <c>GetDocument.Insider</c> tool to read its endpoints. Infrastructure that needs real
/// configuration — connection strings, broker settings — is skipped then; the document does not
/// depend on it.
/// </summary>
public static class BuildTimeOpenApi
{
    public static bool IsGeneratingDocument { get; } =
        string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);
}
