using System.Security.Cryptography.X509Certificates;

namespace Jwmv.Infrastructure.Security;

/// <summary>
/// The certificate authorities that keyless (Fulcio) signing certificates must chain to.
/// </summary>
public sealed class SigstoreTrustRoot(IReadOnlyList<X509Certificate2> roots, IReadOnlyList<X509Certificate2> intermediates)
{
    // Sigstore public-good instance, published by https://github.com/sigstore/root-signing
    // (targets/fulcio_v1.crt.pem and targets/fulcio_intermediate_v1.crt.pem).
    // Root:         O=sigstore.dev, CN=sigstore               valid 2021-10-07 .. 2031-10-05
    //               SHA-256 3B:A7:B6:CC:4E:95:46:9D:4D:33:4B:49:CB:25:7A:D8:53:70:76:FA:84:B0:CA:87:FF:4E:CF:E6:A5:46:80:C1
    // Intermediate: O=sigstore.dev, CN=sigstore-intermediate  valid 2022-04-13 .. 2031-10-05
    //               SHA-256 15:D7:95:34:82:26:B4:64:9F:75:0F:58:02:59:2C:39:3B:EE:7C:C5:3C:3B:86:98:21:75:B7:AD:08:7E:FE:47
    private const string FulcioRootPem = """
-----BEGIN CERTIFICATE-----
MIIB9zCCAXygAwIBAgIUALZNAPFdxHPwjeDloDwyYChAO/4wCgYIKoZIzj0EAwMw
KjEVMBMGA1UEChMMc2lnc3RvcmUuZGV2MREwDwYDVQQDEwhzaWdzdG9yZTAeFw0y
MTEwMDcxMzU2NTlaFw0zMTEwMDUxMzU2NThaMCoxFTATBgNVBAoTDHNpZ3N0b3Jl
LmRldjERMA8GA1UEAxMIc2lnc3RvcmUwdjAQBgcqhkjOPQIBBgUrgQQAIgNiAAT7
XeFT4rb3PQGwS4IajtLk3/OlnpgangaBclYpsYBr5i+4ynB07ceb3LP0OIOZdxex
X69c5iVuyJRQ+Hz05yi+UF3uBWAlHpiS5sh0+H2GHE7SXrk1EC5m1Tr19L9gg92j
YzBhMA4GA1UdDwEB/wQEAwIBBjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBRY
wB5fkUWlZql6zJChkyLQKsXF+jAfBgNVHSMEGDAWgBRYwB5fkUWlZql6zJChkyLQ
KsXF+jAKBggqhkjOPQQDAwNpADBmAjEAj1nHeXZp+13NWBNa+EDsDP8G1WWg1tCM
WP/WHPqpaVo0jhsweNFZgSs0eE7wYI4qAjEA2WB9ot98sIkoF3vZYdd3/VtWB5b9
TNMea7Ix/stJ5TfcLLeABLE4BNJOsQ4vnBHJ
-----END CERTIFICATE-----
""";

    private const string FulcioIntermediatePem = """
-----BEGIN CERTIFICATE-----
MIICGjCCAaGgAwIBAgIUALnViVfnU0brJasmRkHrn/UnfaQwCgYIKoZIzj0EAwMw
KjEVMBMGA1UEChMMc2lnc3RvcmUuZGV2MREwDwYDVQQDEwhzaWdzdG9yZTAeFw0y
MjA0MTMyMDA2MTVaFw0zMTEwMDUxMzU2NThaMDcxFTATBgNVBAoTDHNpZ3N0b3Jl
LmRldjEeMBwGA1UEAxMVc2lnc3RvcmUtaW50ZXJtZWRpYXRlMHYwEAYHKoZIzj0C
AQYFK4EEACIDYgAE8RVS/ysH+NOvuDZyPIZtilgUF9NlarYpAd9HP1vBBH1U5CV7
7LSS7s0ZiH4nE7Hv7ptS6LvvR/STk798LVgMzLlJ4HeIfF3tHSaexLcYpSASr1kS
0N/RgBJz/9jWCiXno3sweTAOBgNVHQ8BAf8EBAMCAQYwEwYDVR0lBAwwCgYIKwYB
BQUHAwMwEgYDVR0TAQH/BAgwBgEB/wIBADAdBgNVHQ4EFgQU39Ppz1YkEZb5qNjp
KFWixi4YZD8wHwYDVR0jBBgwFoAUWMAeX5FFpWapesyQoZMi0CrFxfowCgYIKoZI
zj0EAwMDZwAwZAIwPCsQK4DYiZYDPIaDi5HFKnfxXx6ASSVmERfsynYBiX2X6SJR
nZU84/9DZdnFvvxmAjBOt6QpBlc4J/0DxvkTCqpclvziL6BCCPnjdlIB3Pu3BxsP
mygUY7Ii2zbdCdliiow=
-----END CERTIFICATE-----
""";

    public static SigstoreTrustRoot PublicGood { get; } = new(
        [X509Certificate2.CreateFromPem(FulcioRootPem)],
        [X509Certificate2.CreateFromPem(FulcioIntermediatePem)]);

    public IReadOnlyList<X509Certificate2> Roots { get; } = roots;

    public IReadOnlyList<X509Certificate2> Intermediates { get; } = intermediates;
}
