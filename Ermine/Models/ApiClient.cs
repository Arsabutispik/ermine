using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Ermine.Core;
using Ermine.Helpers;
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


    public static async Task<List<Message>?> FetchMessagesAsync(string channelId, int limit = 50, string? beforeId = null)
    {
        
        var url = $"/channels/{channelId}/messages?limit={limit}&include_users=true";
        if (beforeId != null)
            url += $"&before={beforeId}";
        
        try
        {
            var response = await GetAsync<BulkMessageResponse>(url);

            if (response == null || response.Messages == null)
                return new List<Message>();

            var userLookup = response.Users.ToDictionary(u => u.Id);
            var messageLookup = response.Messages.ToDictionary(m => m.Id);

            foreach (var user in response.Users)
            {
                GlobalCache.Users[user.Id] = user;
            }

            var mappedMessages = response.Messages.Select(msg =>
            {
                userLookup.TryGetValue(msg.Author, out var matchedUser);

                List<Message>? resolvedReplies = null;
                if (msg.Replies?.Length > 0)
                {
                    resolvedReplies = new List<Message>();
                    foreach (var replyId in msg.Replies)
                    {
                        if (messageLookup.TryGetValue(replyId, out var rawReply))
                        {
                            userLookup.TryGetValue(rawReply.Author, out var replyUser);
                            var isMention = msg.Mentions != null && msg.Mentions.Contains(rawReply.Author);
                            resolvedReplies.Add(rawReply with { User = replyUser, IsMentionReply = isMention });
                        }
                    }

                    if (resolvedReplies.Count == 0)
                        resolvedReplies = null;
                }

                var finalMsg = msg with { User = matchedUser };
                finalMsg.ResolvedReplies = resolvedReplies;

                return finalMsg;

            }).ToList();

            return mappedMessages;
        }
        catch (HttpRequestException)
        {
            return new List<Message>();
        }
    }

    public static async Task SendMessageAsync(string channelId, string? content, IList<string>? attachmentIds = null, string? nonce = null)
    {
        var body = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(content))
            body["content"] = content;
        if (attachmentIds?.Count > 0)
            body["attachments"] = attachmentIds;
        if (!string.IsNullOrEmpty(nonce))
            body["nonce"] = nonce;

        await Http.PostAsJsonAsync($"{InstanceUrl}/channels/{channelId}/messages", body);
    }
    public static async Task<string?> UploadAttachmentAsync(string fileName, byte[] data, string mimeType, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var multipartContent = new MultipartFormDataContent();
    
        HttpContent fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        if (progress != null)
        {
            fileContent = new ProgressHttpContent(fileContent, progress);
        }

        multipartContent.Add(fileContent, "file", fileName);

        var response = await Http.PostAsync($"{AutumnUrl}/attachments", multipartContent, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString();
    }
    
    public static async Task<Emoji?> GetEmoji(string emojiId)
    {
        try
        {
            var response = await Http.GetAsync($"{InstanceUrl}/custom/emoji/{emojiId}");
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            
            return JsonSerializer.Deserialize<Emoji>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to fetch emoji {emojiId}: {ex.Message}");
            return null;
        }
    }

    public static async Task AckMessageAsync(string channelId, string messageId)
    {
        await Http.PostAsync($"{InstanceUrl}/channels/{channelId}/ack/{messageId}", null);
    }
}