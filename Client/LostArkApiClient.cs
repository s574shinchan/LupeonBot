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

    // “프로필→네 전역 변수” 채우는 전용 헬퍼(원하면 제거해도 됨)
    public static class LostArkProfileMapper
    {
        public static async Task FillProgramAsync(LostArkApiClient api, string characterName, CancellationToken ct = default)
        {
            var prof = await api.GetArmoryProfilesAsync(characterName, ct);
            if (prof == null) throw new Exception("프로필 응답이 비어있음");

            Program.m_캐릭터명 = prof.CharacterName ?? "";
            Program.m_서버 = prof.ServerName ?? "";
            Program.m_직업 = prof.CharacterClassName ?? "";
            Program.m_원정대레벨 = prof.ExpeditionLevel.ToString();
            Program.m_아이템레벨 = prof.ItemMaxLevel ?? prof.ItemAvgLevel ?? "";
            Program.m_전투력 = prof.CombatPower ?? "";
            Program.m_칭호 = prof.Title ?? "";
            Program.m_길드 = prof.GuildName ?? "";
            Program.m_ImgLink = prof.CharacterImage ?? "";

            var arkRaw = await api.GetArkGridRawAsync(characterName, ct);
            var arkGrid = FindJobEngravingText(arkRaw);
            Program.m_각인 = arkGrid;

            var siblings = await api.GetSiblingsAsync(characterName, ct) ?? new List<CharacterSibling>();
            Program.m_보유캐릭수 = siblings.Count.ToString();
            Program.m_보유캐릭 = BuildSiblingsGroupedLineText(siblings);

            // ✅ 아크패시브 가져오기
            var ark = await api.GetArmoryArkPassiveAsync(characterName, ct);
            var arkText = FormatArkPassive(ark);
            Program.m_아크패시브 = arkText;
        }

        public static string BuildSiblingsGroupedLineText(List<CharacterSibling> siblings)
        {
            if (siblings == null || siblings.Count == 0) return "";

            const int MAX_PER_LINE = 5;

            var groups = siblings
            .Where(s => !string.IsNullOrWhiteSpace(s.ServerName) && !string.IsNullOrWhiteSpace(s.CharacterName))
            .GroupBy(s => s.ServerName!.Trim())
            .OrderBy(g => g.Key == "루페온" ? 0 : 1)  // ⭐ 루페온 최우선
            .ThenBy(g => g.Key);                     // 그 외 가나다순

            var lines = new List<string>();

            foreach (var g in groups)
            {
                // 서버 헤더
                lines.Add($"[{g.Key}]");

                // 캐릭명 정리 (개행 제거 + 정렬)
                var names = g
                    .OrderByDescending(s => ParseItemLevel(s.ItemMaxLevel ?? s.ItemAvgLevel))
                    .ThenBy(s => s.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .Select(s => s.CharacterName!.Replace("\r", "").Replace("\n", "").Trim())
                    .ToList();

                // ✅ 5개씩 끊어서 여러 줄로
                for (int i = 0; i < names.Count; i += MAX_PER_LINE)
                {
                    var chunk = names.Skip(i).Take(MAX_PER_LINE);
                    lines.Add(string.Join(" | ", chunk) + " |");
                }

                lines.Add(""); // 서버 간 공백
            }

            // 마지막 공백 줄 제거
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            return string.Join("\n", lines);
        }

        public static string FormatArkPassive(ArkPassiveResponse ark)
        {
            if (ark?.IsArkPassive != true || ark.Points == null || ark.Points.Count == 0)
                return "-";

            var parts = ark.Points.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => $"{p.Name} {p.Value} ({p.Description})");

            return string.Join("\n", parts);
        }

        // ✅ 여기 "직업각인"만 화이트리스트로 넣어두면 됨
        public static readonly HashSet<string> JobEngravings = new(StringComparer.OrdinalIgnoreCase)
        {
            // 버서커
            "광기","광전사의 비기",
            // 디스트로이어
            "분노의 망치","중력 수련",
            // 워로드
            "전투 태세","고독한 기사",
            // 홀나이트
            "축복의 오라","심판자",
            // 슬레이어
            "처단자","포식자",
            // 가디언 나이트(용기사)
            "빛의 기사","해방자",
            // 인파
            "극의: 체술","충격 단련",
            // 배틀마스터
            "초심","오의 강화",
            // 기공사
            "역천지체","세맥타통",
            // 창술사
            "절정","절제",
            // 스트라이커
            "일격필살","오의난무",
            // 브레이커
            "권왕파천무","수라의 길",
            // 데빌헌터
            "강화 무기","핸드거너",
            // 블래스터
            "화력 강화","포격 강화",
            // 호크아이
            "죽음의 습격","두 번째 동료",
            // 스카우터
            "아르데타인의 기술","진화의 유산",
            // 건슬링어
            "피스메이커","사냥의 시간",
            // 바드
            "절실한 구원","진실된 용맹",
            // 소서리스
            "점화","환류",
            // 서머너
            "상급 소환사","넘치는 교감",
            // 아르카나
            "황후의 은총","황제의 칙령",
            // 블레이드
            "버스트","잔재된 기운",
            // 데모닉
            "멈출 수 없는 충동","완벽한 억제",
            // 리퍼
            "달의 소리","갈증",
            // 소울
            "만월의 집행자","그믐의 경계",
            // 도화가
            "만개","회귀",
            // 기상술사
            "질풍노도","이슬비",
            // 환수사
            "야성","환수각성"
        };

        public static string FindJobEngravingText(JsonElement arkRoot)
        {
            // ArkPassiveParser.FindJobEngravingText 내용 그대로 복붙
            // arkRoot: { IsArkPassive, Points, Effects }
            if (arkRoot.ValueKind != JsonValueKind.Object) return "-";
            if (!arkRoot.TryGetProperty("Effects", out var effects) || effects.ValueKind != JsonValueKind.Array) return "-";

            foreach (var eff in effects.EnumerateArray())
            {
                // 1) ToolTip JSON 문자열 파싱해서 NameTagBox에서 이름 뽑기
                if (!eff.TryGetProperty("ToolTip", out var ttEl) || ttEl.ValueKind != JsonValueKind.String) continue;
                var ttJson = ttEl.GetString();
                if (string.IsNullOrWhiteSpace(ttJson)) continue;

                if (!TryParseJson(ttJson, out var ttRoot)) continue;

                var name = ExtractNameTagBoxValue(ttRoot);   // "절실한 구원"
                if (string.IsNullOrWhiteSpace(name)) continue;

                // 2) 직업각인 화이트리스트에 있는 것만 통과
                if (!JobEngravings.Contains(name)) continue;

                return name;
            }

            return "-";
        }

        public static decimal ParseItemLevel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            s = s.Replace(",", "").Trim();
            return decimal.TryParse(s, out var v) ? v : 0m;
        }

        public static bool TryParseJson(string json, out JsonElement root)
        {
            root = default;
            try
            {
                using var doc = JsonDocument.Parse(json);
                root = doc.RootElement.Clone();
                return root.ValueKind == JsonValueKind.Object;
            }
            catch { return false; }
        }

        // ToolTip JSON에서 Element_xxx 중 type==NameTagBox인 value 반환
        public static string ExtractNameTagBoxValue(JsonElement ttRoot)
        {
            foreach (var prop in ttRoot.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;

                if (!prop.Value.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                    continue;

                if (!string.Equals(typeEl.GetString(), "NameTagBox", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!prop.Value.TryGetProperty("value", out var valEl) || valEl.ValueKind != JsonValueKind.String)
                    continue;

                return valEl.GetString();
            }
            return null;
        }
    }

}
