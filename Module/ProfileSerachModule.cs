using Discord;
using Discord.Interactions;
using DiscordBot;

using LupeonBot.Client;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static LupeonBot.Module.ProfileModule;

namespace LupeonBot.Module
{
    public class ProfileSerachModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("프로필", "로스트아크 캐릭터 프로필을 조회합니다.")]
        public async Task ProfileAsync([Summary(description: "캐릭터 이름")] string 캐릭터명)
        {
            // ✅ 슬래시는 3초 내 응답 필요 → 먼저 Defer(대기표시)
            await DeferAsync();

            try
            {
                using var api = new LostArkApiClient(Program.LostArkJwt);

                var prof = await api.GetArmoryProfilesAsync(캐릭터명);
                if (prof == null) throw new Exception("프로필 응답이 비어있음");

                var siblings = await api.GetSiblingsAsync(캐릭터명) ?? new List<CharacterSibling>();
                var arkRaw = await api.GetArkGridRawAsync(캐릭터명);
                var arkGrid = FindJobEngravingText(arkRaw);

                // ✅ 아크패시브 가져오기
                var ark = await api.GetArmoryArkPassiveAsync(캐릭터명);
                var arkText = FormatArkPassive(ark);

                var profile = new SimpleProfile
                {
                    서버 = prof.ServerName ?? "",
                    직업 = prof.CharacterClassName ?? "",
                    아이템레벨 = prof.ItemMaxLevel ?? prof.ItemAvgLevel ?? "",
                    원정대레벨 = prof.ExpeditionLevel.ToString() ?? "",
                    전투력 = prof.CombatPower?.ToString() ?? "",
                    아크패시브 = arkText ?? "",
                    길드 = prof.GuildName ?? "",
                    칭호 = prof.Title ?? "",
                    각인 = arkGrid, // 각인 따로 처리
                    캐릭터명 = 캐릭터명,
                    ImgLink = prof.CharacterImage ?? "",
                    보유캐릭 = BuildSiblingsLineText(siblings, 캐릭터명),
                    보유캐릭_목록 = BuildSiblingsListText(siblings, 캐릭터명),
                    보유캐릭수 = siblings.Count.ToString()
                };

                // ✅ Embed 구성
                var eb = new EmbedBuilder()
                    .WithTitle($"📌 {profile.캐릭터명} [{profile.서버}]")
                    .WithColor(Color.DarkBlue)
                    .AddField("원정대", $"{profile.원정대레벨}", true)
                    .AddField("길드", string.IsNullOrWhiteSpace(profile.길드) ? "-" : profile.길드, true)
                    .AddField("칭호", string.IsNullOrWhiteSpace(profile.칭호) ? "-" : profile.칭호, true)
                    .AddField("직업", profile.직업, true)
                    .AddField("아이템레벨", profile.아이템레벨, true)
                    .AddField("전투력", string.IsNullOrWhiteSpace(profile.전투력) ? "-" : profile.전투력, true)
                    .AddField("아크 패시브 : " + profile.각인, profile.아크패시브, false)
                    .WithFooter("Develop by. 갱프")
                    .WithThumbnailUrl(profile.ImgLink);

                // 보유 캐릭 리스트가 너무 길면 잘라서 출력(디스코드 제한 대비)
                if (!string.IsNullOrWhiteSpace(profile.보유캐릭))
                {
                    var text = profile.보유캐릭;
                    if (text.Length > 900) text = text.Substring(0, 900) + "\n...";
                    eb.AddField($"보유 캐릭 : {profile.보유캐릭수}", text, false);
                }

                await FollowupAsync(embed: eb.Build());
            }
            catch (Exception ex)
            {
                await FollowupAsync($"❌ 조회 실패: `{ex.Message}`");
            }
        }

        private static string FindJobEngravingText(JsonElement arkRoot)
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

        private static bool TryParseJson(string json, out JsonElement root)
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
        private static string ExtractNameTagBoxValue(JsonElement ttRoot)
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

        private static string FormatArkPassive(ArkPassiveResponse ark)
        {
            if (ark?.IsArkPassive != true || ark.Points == null || ark.Points.Count == 0)
                return "-";

            var parts = ark.Points.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => $"{p.Name} {p.Value} ({p.Description})");

            return string.Join("\n", parts);
        }

        // ✅ 여기 "직업각인"만 화이트리스트로 넣어두면 됨
        private static readonly HashSet<string> JobEngravings = new(StringComparer.OrdinalIgnoreCase)
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

        private static string BuildSiblingsLineText(List<CharacterSibling> siblings, string excludeName = null)
        {
            if (siblings == null || siblings.Count == 0) return "";

            var target = (excludeName ?? "").Trim();

            var list = siblings
                .Select(x => (x.CharacterName ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n.Equals(target, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return string.Join("/", list);
        }

        private static List<string> BuildSiblingsListText(List<CharacterSibling> siblings, string excludeName = null)
        {
            if (siblings == null || siblings.Count == 0)
                return new List<string>();

            var target = (excludeName ?? "").Trim();

            var list = siblings
                .Select(x => (x.CharacterName ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n.Equals(target, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return list;
        }

    }
}
