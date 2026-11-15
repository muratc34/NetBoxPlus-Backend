namespace NetBoxPlus.DemoSeeder.Gateway;

/// <summary>
/// Turns a blob address handed out for browsers into one this process can reach. A SAS signature
/// does not cover the host, so the rewritten address stays valid.
/// </summary>
internal sealed class StorageAddresses
{
    private readonly string? _public;
    private readonly string? _reachable;

    public StorageAddresses(Uri? publicEndpoint, Uri? reachableEndpoint)
    {
        if ((publicEndpoint is null) != (reachableEndpoint is null))
        {
            throw new ArgumentException("Set both storage endpoints or neither.");
        }

        _public = publicEndpoint?.AbsoluteUri.TrimEnd('/');
        _reachable = reachableEndpoint?.AbsoluteUri.TrimEnd('/');
    }

    public Uri ToReachable(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (_public is null || _reachable is null)
        {
            return address;
        }

        var text = address.AbsoluteUri;
        var underPublic = text.StartsWith(_public, StringComparison.OrdinalIgnoreCase)
            && (text.Length == _public.Length || text[_public.Length] is '/' or '?');
        return underPublic ? new Uri(_reachable + text[_public.Length..]) : address;
    }
}
