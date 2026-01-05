using DiscordBot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LupeonBot.Client
{
    public sealed class LostArkApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json;

        public LostArkApiClient(string jwtToken)
        {
            if (string.IsNullOrWhiteSpace(jwtToken))
                throw new ArgumentException("JWT token is empty.", nameof(jwtToken));

            _http = new HttpClient
            {
                BaseAddress = new Uri("https://developer-lostark.game.onstove.com/")
            };

            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", jwtToken.Trim());

            _json = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
        }

        public void Dispose() => _http.Dispose();

        public Task<ArmoryProfilesResponse> GetArmoryProfilesAsync(string characterName, CancellationToken ct = default)
        => GetAsync<ArmoryProfilesResponse>($"armories/characters/{Uri.EscapeDataString(characterName)}/profiles", ct);

        public Task<List<CharacterSibling>> GetSiblingsAsync(string characterName, CancellationToken ct = default)
        => GetAsync<List<CharacterSibling>>($"characters/{Uri.EscapeDataString(characterName)}/siblings", ct);

        public Task<ArkPassiveResponse> GetArmoryArkPassiveAsync(string characterName, CancellationToken ct = default)
        => GetAsync<ArkPassiveResponse>($"armories/characters/{Uri.EscapeDataString(characterName)}/arkpassive", ct);

        private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
        {
            const int maxRetry = 3;

            for (int attempt = 0; attempt < maxRetry; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, path);
                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var delay = GetRetryAfterDelay(res) ?? TimeSpan.FromSeconds(1.5 + attempt);
                    await Task.Delay(delay, ct);
                    continue;
                }

                if ((int)res.StatusCode >= 500 && attempt < maxRetry - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 + attempt), ct);
                    continue;
                }

                if (!res.IsSuccessStatusCode)
                {
                    var body = await SafeReadStringAsync(res);
                    throw new HttpRequestException(
                    $"LostArk API error: {(int)res.StatusCode} {res.ReasonPhrase}\nPATH: {path}\nBODY: {body}"
                    );
                }

                await using var stream = await res.Content.ReadAsStreamAsync(ct);
                return await JsonSerializer.DeserializeAsync<T>(stream, _json, ct);
            }

            return default;
        }

        public Task<JsonElement> GetArkGridRawAsync(string characterName, CancellationToken ct = default)
        => GetRawAsync($"armories/characters/{Uri.EscapeDataString(characterName)}/arkpassive", ct);

        public async Task<JsonElement> GetRawAsync(string path, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                $"LostArk API error: {(int)res.StatusCode} {res.ReasonPhrase}\nPATH: {path}\nBODY: {body}"
                );
            }

            var jsonText = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonText);
            return doc.RootElement.Clone(); // ⚠️ Clone 필수
        }

        private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage res)
        {
            if (res.Headers.TryGetValues("Retry-After", out var values))
            {
                var v = values.FirstOrDefault();
                if (int.TryParse(v, out int sec)) return TimeSpan.FromSeconds(sec);
            }
            return null;
        }

        private static async Task<string> SafeReadStringAsync(HttpResponseMessage res)
        {
            try { return await res.Content.ReadAsStringAsync(); }
            catch { return ""; }
        }
    }

    // DTO들 (필요한 것만 최소)
    public sealed class ArmoryProfilesResponse
    {
        public string CharacterName { get; set; }
        public string ServerName { get; set; }
        public string CharacterClassName { get; set; }
        public int CharacterLevel { get; set; }
        public int ExpeditionLevel { get; set; }
        public string Title { get; set; }
        public string GuildName { get; set; }
        public string ItemAvgLevel { get; set; }
        public string ItemMaxLevel { get; set; }
        public string TownName { get; set; }
        public int TownLevel { get; set; }
        public string PvpGradeName { get; set; }
        public List<ArmoryStat> Stats { get; set; }
        public string CombatPower { get; set; }
        public string CharacterImage { get; set; }
    }

    public sealed class ArmoryStat
    {
        public string Type { get; set; }
        public string Value { get; set; }
        public JsonElement Tooltip { get; set; }
    }

    public sealed class CharacterSibling
    {
        public string CharacterName { get; set; }
        public string ServerName { get; set; }
        public string CharacterClassName { get; set; }
        public string ItemAvgLevel { get; set; }
        public string ItemMaxLevel { get; set; }
    }

    public sealed class ArkPassiveResponse
    {
        public bool IsArkPassive { get; set; }
        public List<ArkPassivePoint> Points { get; set; }
    }

    public sealed class ArkPassivePoint
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public string Tooltip { get; set; }
        public string Description { get; set; } // "6랭크 22레벨"
    }
}
