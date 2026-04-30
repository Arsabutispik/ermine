using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Ermine.Models;

public class GatewayClient
{
    private readonly string _token;
    private readonly ClientWebSocket _ws = new();

    public GatewayClient(string token)
    {
        _token = token;
    }

    public event Action<ReadyEvent>? OnReady;
    public event Action<Message>? OnMessageReceived;

    private record AutumnConfig([property: JsonPropertyName("url")] string Url);
    private record FeaturesConfig([property: JsonPropertyName("autumn")] AutumnConfig Autumn);

    private record InstanceConfig(
        [property: JsonPropertyName("ws")] string WsUrl,
        [property: JsonPropertyName("features")] FeaturesConfig Features
    );

    public async Task StartAsync()
    {
        try
        {
            using var httpHandler = new System.Net.Http.HttpClientHandler { ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true };
            using var http = new System.Net.Http.HttpClient(httpHandler) { BaseAddress = new Uri(ApiClient.InstanceUrl) };
            var config = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<InstanceConfig>(http, "");
            var wsUrl = config?.WsUrl ?? "wss://ws.revolt.chat";
            if (config?.Features.Autumn.Url != null)
            {
                ApiClient.AutumnUrl = config.Features.Autumn.Url;
            }

            _ws.Options.RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            await _ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
            Log.Information("Connected to Gateway at {Url}.", wsUrl);

            var authPayload = new AuthenticatePayload("Authenticate", _token);
            var authJson = JsonSerializer.Serialize(authPayload);
            var authBytes = Encoding.UTF8.GetBytes(authJson);

            await _ws.SendAsync(new ArraySegment<byte>(authBytes), WebSocketMessageType.Text, true,
                CancellationToken.None);

            _ = ReceiveLoopAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to connect to gateway.");
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];

        while (_ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    Log.Warning("WebSocket closed by server.");
                    return; // Exit the loop entirely
                }

                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);

            try
            {
                var baseEvent = await JsonSerializer.DeserializeAsync<BaseGatewayEvent>(ms);

                if (baseEvent?.Type == "Ready")
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var readyData = await JsonSerializer.DeserializeAsync<ReadyEvent>(ms, options);
                    if (readyData != null)
                    {
                        var serverCount = readyData.Servers?.Count ?? 0;
                        Log.Information("Received Ready payload with {Count} servers, {UserCount} users, {ChannelCount} channels.", serverCount, readyData.Users?.Count ?? 0, readyData.Channels?.Count ?? 0);
                        OnReady?.Invoke(readyData);
                    }
                }
                else if (baseEvent?.Type == "Authenticated")
                {
                    Log.Information("Gateway authentication accepted.");
                } 
                else if (baseEvent?.Type == "Message")
                {
                    ms.Seek(0, SeekOrigin.Begin);
                
                    var message = await JsonSerializer.DeserializeAsync<Message>(ms);
                    if (message != null)
                    {
                        OnMessageReceived?.Invoke(message);
                    }
                }
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse gateway message.");
            }
        }
    }
}