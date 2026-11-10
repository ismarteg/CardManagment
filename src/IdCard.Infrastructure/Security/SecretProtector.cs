using Microsoft.AspNetCore.DataProtection;

namespace IdCard.Infrastructure.Security;

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public class SecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("IdCard.Secrets.v1");
    }

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
