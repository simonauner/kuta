# Swedish IP networks

`sweden.cidr` contains Sweden's aggregated IPv4 and IPv6 country allocations from
[ipverse/country-ip-blocks](https://github.com/ipverse/country-ip-blocks), downloaded
2026-10-30. The source data is published under
[CC0 1.0](https://github.com/ipverse/country-ip-blocks/blob/master/LICENSE).

This is an approximate language hint. Registry country allocations do not always
match a visitor's location, especially with VPNs and international operators.
The list is embedded in the API; visitor IPs are not sent to a third party.

Refresh the snapshot when updating IP detection (from the repository root), then
run the locale tests and normal project checks before deploying:

```bash
curl -fsSL https://raw.githubusercontent.com/ipverse/country-ip-blocks/master/country/se/ipv4-aggregated.txt -o /tmp/kuta-se-ipv4.txt
curl -fsSL https://raw.githubusercontent.com/ipverse/country-ip-blocks/master/country/se/ipv6-aggregated.txt -o /tmp/kuta-se-ipv6.txt
sed '/^#/d' /tmp/kuta-se-ipv4.txt /tmp/kuta-se-ipv6.txt > api/Kuta.Api/Localization/sweden.cidr
```

Update the download date above. Existing deployments keep their snapshot until
redeployed. `SwedishIp` normalizes IPv4-mapped IPv6 addresses before lookup.
The endpoint uses `Connection.RemoteIpAddress`, populated in production by the
existing Fly proxy configuration (`Fly-Client-IP`), never a country header from
the browser. It returns `Cache-Control: no-store`.
