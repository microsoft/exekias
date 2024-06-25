
using Microsoft.Extensions.DependencyInjection;
using Azure.Core;

namespace Exekias.Core.Azure;

/// <summary>
/// Represents a provider for retrieving token credentials.
/// </summary>
public interface ICredentialProvider
{
    /// <summary>
    /// Retrieves the token credential.
    /// </summary>
    /// <returns>The token credential.</returns>
    TokenCredential GetCredential();
}

public class CredentialProvider(TokenCredential credential) : ICredentialProvider
{
    public TokenCredential GetCredential()
    {
        return credential;
    }
}

public static class CredentialProviderExtensions
{
    public static IServiceCollection AddCredentialProvider(this IServiceCollection services, TokenCredential credential)
    {
        services.AddSingleton<ICredentialProvider>(new CredentialProvider(credential));
        return services;
    }
}