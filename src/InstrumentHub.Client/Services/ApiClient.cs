using System.Net.Http.Json;
using System.Text.Json;
using InstrumentHub.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
namespace InstrumentHub.Client.Services;
public sealed class ApiClient(HttpClient http,IJSRuntime js)
{
    public async Task<T> Get<T>(string path) {
        using var response=await http.GetAsync("api/"+path); await Check(response);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("The server returned an empty response.");
    }
    public async Task SaveInstrument(Instrument instrument) {
        using var response=instrument.Id==0 ? await http.PostAsJsonAsync("api/instruments",instrument) : await http.PutAsJsonAsync($"api/instruments/{instrument.Id}",instrument);
        await Check(response);
    }
    public async Task Repair(RepairLog log) { using var response=await http.PostAsJsonAsync("api/repairs",log); await Check(response); }
    public async Task Complete(int id) { using var response=await http.PostAsync($"api/repairs/{id}/complete",null); await Check(response); }
    public async Task Upload(int id,IBrowserFile file) {
        using var content=new MultipartFormDataContent(); using var stream=file.OpenReadStream(10*1024*1024);
        content.Add(new StreamContent(stream),"file",file.Name);
        using var response=await http.PostAsync($"api/instruments/{id}/certificates",content); await Check(response);
    }
    public async Task<int> Import(IBrowserFile file) {
        using var content=new MultipartFormDataContent(); using var stream=file.OpenReadStream(5*1024*1024);
        content.Add(new StreamContent(stream),"file",file.Name); using var response=await http.PostAsync("api/instruments/import",content); await Check(response);
        return (await response.Content.ReadFromJsonAsync<ImportResult>())!.Imported;
    }
    public async Task Download(string path,string fileName) {
        using var response=await http.GetAsync("api/"+path); await Check(response);
        await js.InvokeVoidAsync("instrumentHub.download",fileName,response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync()));
    }
    public async Task Backup() { using var response=await http.PostAsync("api/backups",null); await Check(response); }
    static async Task Check(HttpResponseMessage response) {
        if(response.IsSuccessStatusCode) return;
        var fallback=response.StatusCode==System.Net.HttpStatusCode.Unauthorized ? "Your session has expired. Sign out and sign in again." : "The request could not be completed. Try again.";
        try { var body=await response.Content.ReadFromJsonAsync<JsonElement>(); if(body.TryGetProperty("message",out var message)) fallback=message.GetString() ?? fallback; else if(body.TryGetProperty("detail",out var detail)) fallback=detail.GetString() ?? fallback; } catch(JsonException) { }
        throw new InvalidOperationException(fallback);
    }
}
