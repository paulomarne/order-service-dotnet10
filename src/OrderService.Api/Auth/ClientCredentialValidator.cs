using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OrderService.Application.Abstractions;
using OrderService.Application.Auth;

namespace OrderService.Api.Auth;

public sealed class ClientCredentialValidator(IOptions<AuthOptions> options) : IClientCredentialValidator
{
    public bool Validate(string clientId, string clientSecret)
    {
        var expected = options.Value;
        var idMatches = FixedTimeEquals(clientId.Trim(), expected.ClientId);
        var secretMatches = FixedTimeEquals(clientSecret, expected.ClientSecret);
        return idMatches && secretMatches;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
