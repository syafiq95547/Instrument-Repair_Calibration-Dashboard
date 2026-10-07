using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using InstrumentHub.Shared;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
namespace InstrumentHub.Client.Services;
public sealed class Session(IJSRuntime js) : AuthenticationStateProvider
{
    public string? Token { get; private set; }
    public string Name { get; private set; } = "";
    public bool DemoMode { get; private set; }
    DateTime expires;
    public async Task Initialize(string baseAddress)
    {
        using var client=new HttpClient { BaseAddress=new Uri(baseAddress) };
        try {
            var health=await client.GetFromJsonAsync<Health>("api/health"); DemoMode=health?.Mode=="demo";
            var saved=await js.InvokeAsync<string?>("sessionStorage.getItem","instrumenthub.session");
            if(saved is not null) { var login=JsonSerializer.Deserialize<LoginResponse>(saved); if(login is not null && login.ExpiresAt>DateTime.UtcNow) Apply(login); }
            if(Token is null && DemoMode && await js.InvokeAsync<string?>("sessionStorage.getItem","instrumenthub.signedout") is null)
                await Login(client,"admin@instrumenthub.local","DemoCalibration!2026");
        } catch(HttpRequestException) { } catch(JsonException) { await js.InvokeVoidAsync("sessionStorage.removeItem","instrumenthub.session"); }
    }
    public async Task Login(HttpClient client,string email,string password)
    {
        var response=await client.PostAsJsonAsync("api/auth/login",new LoginRequest(email,password));
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode==System.Net.HttpStatusCode.TooManyRequests ? "Too many attempts. Try again in a minute." : "The email or password is incorrect.");
        var login=(await response.Content.ReadFromJsonAsync<LoginResponse>())!; Apply(login);
        await js.InvokeVoidAsync("sessionStorage.setItem","instrumenthub.session",JsonSerializer.Serialize(login));
        await js.InvokeVoidAsync("sessionStorage.removeItem","instrumenthub.signedout");
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
    void Apply(LoginResponse response) { Token=response.Token; Name=response.Name; expires=response.ExpiresAt; }
    public async Task Logout() { Token=null; Name=""; await js.InvokeVoidAsync("sessionStorage.removeItem","instrumenthub.session"); await js.InvokeVoidAsync("sessionStorage.setItem","instrumenthub.signedout","true"); NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); }
    public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(Token is not null && expires>DateTime.UtcNow ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,Name)],"jwt")) : new ClaimsPrincipal(new ClaimsIdentity())));
    sealed record Health(string Status,string Mode);
}
public sealed class JwtHandler(Session session) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
        if(session.Token is not null) request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",session.Token);
        return base.SendAsync(request,ct);
    }
}
