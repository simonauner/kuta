using System.Net;

// Country allocations are a language hint, never an access-control decision.
public static class SwedishIp
{
    private static readonly IPNetwork[] Networks = LoadNetworks();

    public static bool Contains(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return Array.Exists(Networks, network => network.Contains(address));
    }

    private static IPNetwork[] LoadNetworks()
    {
        using var stream = typeof(SwedishIp).Assembly.GetManifestResourceStream("Kuta.Api.Localization.sweden.cidr")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(IPNetwork.Parse).ToArray();
    }
}
