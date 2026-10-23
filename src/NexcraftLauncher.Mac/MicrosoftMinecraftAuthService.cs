using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NexcraftLauncherMac;

public sealed record AuthenticatedMinecraftAccount(string Username, string Uuid, string AccessToken);

/// <summary>Microsoft device-code sign-in followed by Xbox Live, XSTS and Minecraft Services authentication.</summary>
public sealed class MicrosoftMinecraftAuthService
{
    // Public client ID used by the Minecraft Launcher ecosystem; no client secret is embedded.
    private const string ClientId = "00000000402b5328";
    private static readonly Uri DeviceCodeUri = new("https://login.live.com/oauth20_connect.srf");
    private static readonly Uri TokenUri = new("https://login.live.com/oauth20_token.srf");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<AuthenticatedMinecraftAccount> SignInAsync(
        Action<string> showUserCode,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        using var deviceResponse = await _http.PostAsync(DeviceCodeUri,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = "service::user.auth.xboxlive.com::MBI_SSL",
                ["response_type"] = "device_code"
            }), cancellationToken);
        var deviceText = await deviceResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!deviceResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Microsoft не выдал код входа: " + SafeError(deviceText));
        using var deviceDoc = JsonDocument.Parse(deviceText);
        var device = deviceDoc.RootElement;
        var deviceCode = Required(device, "device_code");
        var userCode = Required(device, "user_code");
        var verificationUri = GetString(device, "verification_uri") ?? "https://microsoft.com/devicelogin";
        var message = GetString(device, "message") ?? $"Откройте {verificationUri} и введите код {userCode}.";
        var expiresIn = GetInt(device, "expires_in", 900);
        var interval = Math.Max(2, GetInt(device, "interval", 5));
        showUserCode(message);

        string microsoftToken = "";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            using var tokenResponse = await _http.PostAsync(TokenUri,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = ClientId,
                    ["device_code"] = deviceCode
                }), cancellationToken);
            var tokenText = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            using var tokenDoc = JsonDocument.Parse(tokenText);
            if (tokenResponse.IsSuccessStatusCode)
            {
                microsoftToken = Required(tokenDoc.RootElement, "access_token");
                break;
            }

            var error = GetString(tokenDoc.RootElement, "error") ?? "";
            if (error == "authorization_pending") { status?.Report("Ожидаю подтверждение входа Microsoft…"); continue; }
            if (error == "slow_down") { interval += 5; continue; }
            if (error is "authorization_declined" or "bad_verification_code" or "expired_token")
                throw new InvalidOperationException("Вход Microsoft не завершён: " + error);
            throw new InvalidOperationException("Ошибка входа Microsoft: " + SafeError(tokenText));
        }

        if (string.IsNullOrWhiteSpace(microsoftToken))
            throw new TimeoutException("Истёк срок действия кода Microsoft. Нажми «Играть» и попробуй снова.");

        status?.Report("Проверяю аккаунт Xbox…");
        using var xboxResponse = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, cancellationToken);
        var xboxText = await xboxResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(xboxResponse, xboxText, "Xbox Live");
        using var xboxDoc = JsonDocument.Parse(xboxText);
        var xboxToken = Required(xboxDoc.RootElement, "Token");
        var userHash = xboxDoc.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()
            ?? throw new InvalidDataException("Xbox не вернул идентификатор пользователя.");

        status?.Report("Проверяю доступ к Minecraft…");
        using var xstsResponse = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xboxToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, cancellationToken);
        var xstsText = await xstsResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(xstsResponse, xstsText, "Xbox XSTS");
        using var xstsDoc = JsonDocument.Parse(xstsText);
        var xstsToken = Required(xstsDoc.RootElement, "Token");

        using var mcResponse = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        }, cancellationToken);
        var mcText = await mcResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(mcResponse, mcText, "Minecraft Services");
        using var mcDoc = JsonDocument.Parse(mcText);
        var minecraftToken = Required(mcDoc.RootElement, "access_token");

        using var entitlementRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore");
        entitlementRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
        using var entitlementResponse = await _http.SendAsync(entitlementRequest, cancellationToken);
        var entitlementText = await entitlementResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(entitlementResponse, entitlementText, "проверка лицензии Minecraft");
        using var entitlementDoc = JsonDocument.Parse(entitlementText);
        if (!entitlementDoc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            throw new InvalidOperationException("У этого Microsoft-аккаунта не найдена лицензия Minecraft Java Edition. Запуск полной игры недоступен.");

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
        using var profileResponse = await _http.SendAsync(profileRequest, cancellationToken);
        var profileText = await profileResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(profileResponse, profileText, "профиль Minecraft");
        using var profileDoc = JsonDocument.Parse(profileText);
        var username = Required(profileDoc.RootElement, "name");
        var uuid = Required(profileDoc.RootElement, "id");
        status?.Report($"Вход выполнен: {username}");
        return new AuthenticatedMinecraftAccount(username, uuid, minecraftToken);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _http.SendAsync(request, ct);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string step)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ошибка {step} ({(int)response.StatusCode}): {SafeError(body)}");
    }

    private static string SafeError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return GetString(doc.RootElement, "error_description") ??
                   GetString(doc.RootElement, "errorMessage") ??
                   GetString(doc.RootElement, "error") ??
                   $"HTTP-ошибка ({body.Length} байт ответа).";
        }
        catch (JsonException) { return "Сервер вернул неожиданный ответ."; }
    }

    private static string Required(JsonElement element, string property) =>
        GetString(element, property) is { Length: > 0 } value
            ? value : throw new InvalidDataException($"В ответе сервера отсутствует поле {property}.");

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement element, string property, int fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.TryGetInt32(out var result) ? result : fallback;
}
