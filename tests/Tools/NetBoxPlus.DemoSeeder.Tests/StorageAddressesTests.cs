using NetBoxPlus.DemoSeeder.Gateway;

namespace NetBoxPlus.DemoSeeder.Tests;

public sealed class StorageAddressesTests
{
    private static readonly StorageAddresses InContainer = new(new Uri("http://127.0.0.1:10000"), new Uri("http://azurite:10000"));

    [Fact]
    public void ABrowserAddress_IsTurnedIntoTheReachableOne_SignatureKept()
        => Assert.Equal(
            new Uri("http://azurite:10000/devstoreaccount1/incoming/a.png?sv=2025&sig=abc"),
            InContainer.ToReachable(new Uri("http://127.0.0.1:10000/devstoreaccount1/incoming/a.png?sv=2025&sig=abc")));

    [Fact]
    public void AnotherAddress_IsLeftAlone()
    {
        var otherPort = new Uri("http://127.0.0.1:10001/devstoreaccount1/a.png");
        Assert.Equal(otherPort, InContainer.ToReachable(otherPort));
        Assert.Equal(new Uri("https://storage.example.test/a.png"), InContainer.ToReachable(new Uri("https://storage.example.test/a.png")));

        // A longer name that merely starts with the endpoint's is another host.
        var account = new StorageAddresses(new Uri("http://storage.test/account"), new Uri("http://azurite:10000/account"));
        Assert.Equal(new Uri("http://storage.test/account2/a.png"), account.ToReachable(new Uri("http://storage.test/account2/a.png")));
        Assert.Equal(new Uri("http://azurite:10000/account/a.png"), account.ToReachable(new Uri("http://storage.test/account/a.png")));
    }

    [Fact]
    public void WithoutEndpoints_NothingChanges()
    {
        var address = new Uri("http://127.0.0.1:10000/devstoreaccount1/a.png");
        Assert.Equal(address, new StorageAddresses(null, null).ToReachable(address));
    }

    [Fact]
    public void OneEndpointAlone_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new StorageAddresses(new Uri("http://127.0.0.1:10000"), null));
        Assert.Throws<ArgumentException>(() => new StorageAddresses(null, new Uri("http://azurite:10000")));
    }
}
