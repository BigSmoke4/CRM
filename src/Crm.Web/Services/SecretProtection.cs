using Microsoft.AspNetCore.DataProtection;

namespace Crm.Web.Services;

/// <summary>
/// Encrypts/decrypts secrets (API keys, tokens, connection strings) at rest using
/// ASP.NET Core Data Protection. Keys are stored outside the database in the
/// configured key ring directory (see Program.cs), so DB dumps alone don't leak secrets.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
    string? ProtectOrNull(string? plaintext);
    string? UnprotectOrNull(string? protectedText);
}

public class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Crm.IntegrationSecrets.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
    public string? ProtectOrNull(string? plaintext) => plaintext == null ? null : Protect(plaintext);
    public string? UnprotectOrNull(string? protectedText) =>
        string.IsNullOrEmpty(protectedText) ? null : Unprotect(protectedText);
}
