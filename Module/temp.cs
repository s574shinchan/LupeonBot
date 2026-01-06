using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using LupeonBot.Client;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LupeonBot.Modules
{
    // ====== 모델 ======
    public class CertInfoRow
    {
        public string UserId { get; set; } = "";
        public string Character { get; set; } = "";
    }

    // ====== SupabaseClient (필요한 메서드만) ======
    public static class SupabaseClient
    {
        // 기존에 Init/Client 구성 해둔 거 있으면 그걸 쓰면 됨.
        // 여기서는 예시로만 둠.
        public static HttpClient Client { get; private set; } = new HttpClient();

        // ✅ userid 단건 조회
        public static async Task<CertInfoRow?> GetByUserIdAsync(string userId)
        {
            string url =
                "rest/v1/certinfo" +
                "?select=userid,character" +
                $"&userid=eq.{Uri.EscapeDataString(userId)}" +
                "&limit=1";

            var res = await Client.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new Exception($"Supabase SELECT 실패\n{body}");

            var list = JsonSerializer.Deserialize<List<CertInfoRow>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (list != null && list.Count > 0) ? list[0] : null;
        }

        // ✅ 캐릭터명 부분일치 조회
        public static async Task<List<CertInfoRow>> SearchByCharacterAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<CertInfoRow>();

            string url =
                "rest/v1/certinfo" +
                "?select=userid,character" +
                $"&character=ilike.*{Uri.EscapeDataString(keyword)}*";

            var res = await Client.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new Exception($"Supabase SELECT 실패\n{body}");

            var list = JsonSerializer.Deserialize<List<CertInfoRow>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return list ?? new List<CertInfoRow>();
        }

        // ✅ userid 단건 삭제
        public static async Task<bool> DeleteByUserIdAsync(string userId)
        {
            string url = "rest/v1/certinfo" + $"?userid=eq.{Uri.EscapeDataString(userId)}";
            var res = await Client.DeleteAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new Exception($"Supabase DELETE 실패\n{body}");
            return true;
        }
    }

    // ====== 페이지/단건 상태 캐시 ======
    public static class CertDeleteCache
    {
        public class State
        {
            public ulong OwnerDiscordId { get; set; }
            public DateTime CreatedUtc { get; set; }
            public string Input { get; set; } = "";

            // 결과 rows (userid 단건도 rows로 담아 통일)
            public List<CertInfoRow> Rows { get; set; } = new();
            public int Index { get; set; } = 0;

            public bool IsSingleMode { get; set; } = false; // userid 입력 모드
        }

        public static ConcurrentDictionary<string, State> Map { get; } = new();

        public static void Cleanup(TimeSpan maxAge)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in Map)
            {
                if (now - kv.Value.CreatedUtc > maxAge)
                    Map.TryRemove(kv.Key, out _);
            }
        }
    }

    // ====== UI 빌더 ======
    internal static class CertDeleteUi
    {
        public static Embed BuildViewEmbed(CertDeleteCache.State s)
        {
            var row = s.Rows[s.Index];

            string character = (row.Character ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (character.Length > 900) character = character.Substring(0, 900) + "…";

            var eb = new EmbedBuilder()
                .WithTitle("🧾 인증삭제 미리보기")
                .WithColor(Color.Orange)
                .WithDescription(
                    $"입력: `{s.Input}`\n" +
                    (s.IsSingleMode
                        ? $"모드: **USERID 단건**\n\n"
                        : $"모드: **캐릭터명 검색**\n페이지: **{s.Index + 1} / {s.Rows.Count}**\n\n") +
                    $"**UserId**: `{row.UserId}`\n" +
                    $"**Character**: `{character}`"
                );

            return eb.Build();
        }

        public static MessageComponent BuildComponents(string token, CertDeleteCache.State s)
        {
            var cb = new ComponentBuilder();

            // 캐릭터명 검색일 때만 Prev/Next
            if (!s.IsSingleMode && s.Rows.Count > 1)
            {
                cb.WithButton("◀ 이전", customId: $"certdel:{token}:prev",
                    style: ButtonStyle.Secondary, disabled: s.Index <= 0);

                cb.WithButton("다음 ▶", customId: $"certdel:{token}:next",
                    style: ButtonStyle.Secondary, disabled: s.Index >= s.Rows.Count - 1);
            }

            cb.WithButton("✅ 삭제", customId: $"certdel:{token}:delete", style: ButtonStyle.Danger);
            cb.WithButton("❌ 취소", customId: $"certdel:{token}:cancel", style: ButtonStyle.Secondary);

            return cb.Build();
        }

        public static Embed BuildResultEmbed(string title, string message, bool success)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithColor(success ? Color.Green : Color.DarkGrey)
                .WithDescription(message)
                .Build();
        }
    }

    // ====== 1) SlashCommand: /인증삭제 ======
    public sealed class CertDeleteSlashModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("인증삭제", "userid 또는 캐릭터명으로 certinfo 삭제(미리보기 후 삭제/취소)")]
        public async Task CertDeleteAsync([Summary("입력", "userid 또는 캐릭터명")] string input)
        {
            input = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                await RespondAsync("❌ 입력값이 비어있음", ephemeral: true);
                return;
            }

            // 캐시 청소 (10분)
            CertDeleteCache.Cleanup(TimeSpan.FromMinutes(10));

            bool isUserId = input.All(char.IsDigit);

            List<CertInfoRow> rows;
            bool singleMode;

            if (isUserId)
            {
                singleMode = true;

                var row = await SupabaseClient.GetByUserIdAsync(input);
                if (row == null)
                {
                    await RespondAsync($"⚠️ userid `{input}` 데이터가 없습니다.", ephemeral: true);
                    return;
                }
                rows = new List<CertInfoRow> { row };
            }
            else
            {
                singleMode = false;

                rows = await SupabaseClient.SearchByCharacterAsync(input);
                if (rows.Count == 0)
                {
                    await RespondAsync($"⚠️ `{input}` 검색 결과가 없습니다.", ephemeral: true);
                    return;
                }

                // (선택) 동일 userid 중복 제거(혹시 모를 중복 방지)
                rows = rows
                    .GroupBy(r => r.UserId ?? "")
                    .Select(g => g.First())
                    .Where(r => !string.IsNullOrWhiteSpace(r.UserId))
                    .ToList();

                if (rows.Count == 0)
                {
                    await RespondAsync($"⚠️ `{input}` 검색 결과가 없습니다.", ephemeral: true);
                    return;
                }
            }

            var token = Guid.NewGuid().ToString("N");
            var state = new CertDeleteCache.State
            {
                OwnerDiscordId = Context.User.Id,
                CreatedUtc = DateTime.UtcNow,
                Input = input,
                Rows = rows,
                Index = 0,
                IsSingleMode = singleMode
            };

            CertDeleteCache.Map[token] = state;

            var embed = CertDeleteUi.BuildViewEmbed(state);
            var comps = CertDeleteUi.BuildComponents(token, state);

            await RespondAsync(embed: embed, components: comps, ephemeral: true);
        }
    }

    // ====== 2) ComponentInteraction: Prev/Next/Delete/Cancel ======
    public sealed class CertDeleteComponentModule : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("certdel:*:*")]
        public async Task OnButton(string token, string action)
        {
            // “로딩중” 표시 없이 처리하고 싶으면 Defer 없이 바로 Modify/Followup 해도 되지만,
            // 안전하게 Defer 사용
            await DeferAsync(ephemeral: true);

            if (!CertDeleteCache.Map.TryGetValue(token, out var state))
            {
                await FollowupAsync("❌ 만료되었거나 이미 처리된 요청입니다.", ephemeral: true);
                return;
            }

            // 생성자만 조작 가능
            if (state.OwnerDiscordId != Context.User.Id)
            {
                await FollowupAsync("❌ 이 요청은 생성자만 조작할 수 있습니다.", ephemeral: true);
                return;
            }

            // 만료 체크
            if (DateTime.UtcNow - state.CreatedUtc > TimeSpan.FromMinutes(10))
            {
                CertDeleteCache.Map.TryRemove(token, out _);
                await ModifyOriginalResponseAsync(m =>
                {
                    m.Embed = CertDeleteUi.BuildResultEmbed("⏰ 만료", "요청이 만료되었습니다. 다시 `/인증삭제` 해주세요.", success: false);
                    m.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // Prev/Next: 같은 메시지 수정
            if (action == "prev")
            {
                state.Index = Math.Max(0, state.Index - 1);
                var embed = CertDeleteUi.BuildViewEmbed(state);
                var comps = CertDeleteUi.BuildComponents(token, state);

                await ModifyOriginalResponseAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = comps;
                });
                return;
            }

            if (action == "next")
            {
                state.Index = Math.Min(state.Rows.Count - 1, state.Index + 1);
                var embed = CertDeleteUi.BuildViewEmbed(state);
                var comps = CertDeleteUi.BuildComponents(token, state);

                await ModifyOriginalResponseAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = comps;
                });
                return;
            }

            // Cancel: 현재 표시 메시지를 "취소됨"으로 대체 + 버튼 제거
            if (action == "cancel")
            {
                CertDeleteCache.Map.TryRemove(token, out _);

                var current = state.Rows[state.Index];
                await ModifyOriginalResponseAsync(m =>
                {
                    m.Embed = CertDeleteUi.BuildResultEmbed(
                        "❌ 취소됨",
                        $"요청이 취소되었습니다.\n(대상 UserId: `{current.UserId}`)",
                        success: false
                    );
                    m.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // Delete: "현재 페이지에 표시된 row의 userid"만 삭제 + 버튼 제거
            if (action == "delete")
            {
                var current = state.Rows[state.Index];
                var userId = (current.UserId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(userId))
                {
                    CertDeleteCache.Map.TryRemove(token, out _);
                    await ModifyOriginalResponseAsync(m =>
                    {
                        m.Embed = CertDeleteUi.BuildResultEmbed("❌ 실패", "UserId가 비어있어 삭제할 수 없습니다.", success: false);
                        m.Components = new ComponentBuilder().Build();
                    });
                    return;
                }

                await SupabaseClient.DeleteByUserIdAsync(userId);

                // 나머지 row1,row2는 자동 취소 = 캐시 제거로 종료
                CertDeleteCache.Map.TryRemove(token, out _);

                await ModifyOriginalResponseAsync(m =>
                {
                    m.Embed = CertDeleteUi.BuildResultEmbed(
                        "✅ 삭제 완료",
                        $"삭제 처리되었습니다.\n삭제된 UserId: `{userId}`",
                        success: true
                    );
                    m.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // 알 수 없는 action
            await FollowupAsync("⚠️ 알 수 없는 동작입니다.", ephemeral: true);
        }
    }
}
