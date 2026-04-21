// FF.WebBlazor/Services/AuthorizationMessageHandler.cs
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace FF.WebBlazor.Services;

public class AuthorizationMessageHandler(TokenStore tokenStore) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}