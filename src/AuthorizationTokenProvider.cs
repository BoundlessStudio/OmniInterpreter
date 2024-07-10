using Azure.Core;
using Azure.Identity;

namespace BoundlessAi.OmniInterpreter;

public interface IAuthorizationTokenProvider
{
  Task<string> GetToken();
}

public class DefaultAzureCredentialTokenProvider : IAuthorizationTokenProvider
{
  private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

  private readonly TokenRequestContext context;
  private readonly DefaultAzureCredential credential;
  
  private AccessToken token;
  
  public DefaultAzureCredentialTokenProvider()
  {
    this.context = new TokenRequestContext(["https://dynamicsessions.io/.default"]);
    this.credential = new DefaultAzureCredential();
  }

  public async Task<string> GetToken()
  {
    if (token.Token == null || token.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
    {
      await _lock.WaitAsync();
      try
      {
        if (token.Token == null || token.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
          token = await credential.GetTokenAsync(this.context);
        }
      }
      finally
      {
        _lock.Release();
      }
    }

    return token.Token;
  }
}