using System.Diagnostics;
using System.Diagnostics.Metrics;
using Auth.Domain.RefreshTokens;

namespace Auth.Application;

/// <summary>How a sign-in ended, as the <c>result</c> tag spells it. Customers only ever see one answer.</summary>
internal static class SignInResults
{
    public const string Succeeded = "succeeded";

    /// <summary>Unknown address, wrong password, or an admin sign-in without the permission.</summary>
    public const string Failed = "failed";

    /// <summary>A locked or disabled account.</summary>
    public const string Refused = "refused";
}

/// <summary>The <c>NetBoxPlus.Auth</c> meter.</summary>
public sealed class AuthMetrics
{
    public const string MeterName = "NetBoxPlus.Auth";

    private readonly Counter<long> _signIns;

    public AuthMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _signIns = meterFactory.Create(MeterName)
            .CreateCounter<long>("netboxplus.auth.sign_ins", "{sign_in}", "Sign-in attempts by kind (customer, admin) and result.");
    }

    internal void SignIn(SessionKind kind, string result)
        => _signIns.Add(1, new TagList { { "kind", kind == SessionKind.Admin ? "admin" : "customer" }, { "result", result } });
}
