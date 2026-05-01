using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ermine.Core;
using Serilog;

namespace Ermine.Models;

public enum LoginResultType
{
    Success,
    MfaRequired,
    AccountDisabled,
    Unauthorized
}

public record LoginResponse(
    LoginResultType Result,
    SessionResponse? Session = null,
    string? MfaTicket = null);

public record LoginPayload(string Email, string Password, string FriendlyName);

public record SessionResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("user_id")]
    string UserId);

public record LoginResult(
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("ticket")] string? Ticket,
    [property: JsonPropertyName("user_id")]
    string? UserId
);

public record MfaResponsePayload(
    [property: JsonPropertyName("totp_code")]
    string? TotpCode = null,
    [property: JsonPropertyName("recovery_code")]
    string? RecoveryCode = null,
    [property: JsonPropertyName("password")]
    string? Password = null
);

public record MfaSubmitPayload(
    [property: JsonPropertyName("mfa_ticket")]
    string MfaTicket,
    [property: JsonPropertyName("mfa_response")]
    MfaResponsePayload MfaResponse,
    [property: JsonPropertyName("friendly_name")]
    string FriendlyName = "Ermine Desktop"
);

public class ApiClient
{
    public static string InstanceUrl { get; set; } = "https://stoat.chat/api/";
    public static string AutumnUrl { get; set; } = "https://cdn.stoatusercontent.com";

    internal static readonly HttpClient Http = new(new HttpClientHandler
    {
        // Accept self-signed certificates for local/self-hosted instances
        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
    });
    
    public static void UpdateInstanceUrl(string url)
    {
        if (!url.EndsWith("/")) url += "/";
        InstanceUrl = url;
    }

    public void SaveSession(string token)
    {
        var settings = SettingsManager.Load();
        settings.SessionToken = token;
        SettingsManager.Save(settings);
    }

    public void ClearSession()
    {
        var settings = SettingsManager.Load();
        settings.SessionToken = null;
        settings.LastInstanceUrl = null;
        SettingsManager.Save(settings);

        Http.DefaultRequestHeaders.Remove("x-session-token");
        InstanceUrl = "https://stoat.chat/api/";
        AutumnUrl = "https://cdn.stoatusercontent.com";
    }

    public string TryLoadSavedSession()
    {
        var settings = SettingsManager.Load();

        if (!string.IsNullOrWhiteSpace(settings.LastInstanceUrl))
            UpdateInstanceUrl(settings.LastInstanceUrl);

        if (!string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            Http.DefaultRequestHeaders.Clear();
            Http.DefaultRequestHeaders.Add("x-session-token", settings.SessionToken);
            return settings.SessionToken;
        }

        return string.Empty;
    }

    public async Task<LoginResponse> LoginWithCredentialsAsync(string email, string password)
    {
        var payload = new LoginPayload(email, password, "Ermine Desktop");
        var response = await Http.PostAsJsonAsync($"{InstanceUrl}auth/session/login", payload);
        var rawResponse = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var data = JsonSerializer.Deserialize<LoginResult>(rawResponse);

            if (data?.Result == "Success" && !string.IsNullOrEmpty(data.Token))
            {
                var session = new SessionResponse(data.Token, data.UserId ?? "");
                Http.DefaultRequestHeaders.Clear();
                Http.DefaultRequestHeaders.Add("x-session-token", session.Token);
                return new LoginResponse(LoginResultType.Success, session);
            }

            if (data?.Result == "MFA" && !string.IsNullOrEmpty(data.Ticket))
                return new LoginResponse(LoginResultType.MfaRequired, MfaTicket: data.Ticket);

            if (data?.Result == "Disabled")
            {
                Log.Warning("Login attempt for disabled account: {Email}", email);
                return new LoginResponse(LoginResultType.AccountDisabled);
            }
        }

        return new LoginResponse(LoginResultType.Unauthorized);
    }

    public async Task<SessionResponse?> SubmitMfaAsync(string mfaTicket, string code)
    {
        // If the code is 6 digits, it's TOTP. If it's longer/different, it might be a Recovery Code.
        var responsePayload = code.Length == 6
            ? new MfaResponsePayload(code)
            : new MfaResponsePayload(RecoveryCode: code);

        var payload = new MfaSubmitPayload(
            mfaTicket,
            responsePayload
        );

        var response = await Http.PostAsJsonAsync($"{InstanceUrl}auth/session/login", payload);

        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (data?.Result == "Success" && !string.IsNullOrEmpty(data.Token))
                return new SessionResponse(data.Token, data.UserId ?? "");
        }

        var error = await response.Content.ReadAsStringAsync();
        Log.Error("MFA Verification failed: {Error}", error);
        return null;
    }
    
    /// <summary>
    /// A generic GET method for models to call their own endpoints.
    /// </summary>
    public static async Task<T?> GetAsync<T>(string endpoint)
    {
        try
        {
            if (endpoint.StartsWith("/")) endpoint = endpoint.Substring(1);
            
            return await Http.GetFromJsonAsync<T>($"{InstanceUrl}{endpoint}");
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Failed to fetch {Endpoint}. Possible invalid session token or network error.", endpoint);
            return default;
        }
    }
    
    
    public static async Task<List<Message>?> FetchMessagesAsync(string channelId, int limit = 50)
    {
        // Don't forget the include_users=true flag!
        var url = $"/channels/{channelId}/messages?limit={limit}&include_users=true";

        try
        {
            // 1. Deserialize into the new Bulk envelope
            var response = await GetAsync<BulkMessageResponse>(url);
            
            if (response == null || response.Messages == null) 
                return new List<Message>();

            // 2. Create a fast lookup dictionary for users
            var userLookup = response.Users.ToDictionary(u => u.Id);

            // 3. Map the users into the messages using the 'with' keyword
            var mappedMessages = response.Messages.Select(msg => 
            {
                // Try to find the user in the dictionary. If found, attach it.
                userLookup.TryGetValue(msg.Author, out var matchedUser);
                
                // Because records are immutable, 'with' creates a new Message instance 
                // with the User property filled in.
                return msg with { User = matchedUser };
                
            }).ToList();

            return mappedMessages;
        }
        catch (HttpRequestException)
        {
            return new List<Message>();
        }
    }
    
    public static async Task SendMessageAsync(string channelId, string? content, IList<string>? attachmentIds = null)
    {
        var body = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(content))
            body["content"] = content;
        if (attachmentIds?.Count > 0)
            body["attachments"] = attachmentIds;

        await Http.PostAsJsonAsync($"{InstanceUrl}/channels/{channelId}/messages", body);
    }
    public static async Task<string?> UploadAttachmentAsync(string fileName, byte[] data, string mimeType)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Add(fileContent, "file", fileName);

        var response = await Http.PostAsync($"{AutumnUrl}/attachments", content);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString();
    }
}