using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LupeonBot.Cache;
using LupeonBot.Client;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DiscordBot.Program;
using static LupeonBot.Client.SupabaseClient;

namespace LupeonBot.Module
{
    public sealed class CertSelectModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("인증전체조회", "인증내역 일괄 조회")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task GetCertInfoTable() 
        {
            if (Context.User is not SocketGuildUser) return;

            // ✅ 로딩은 비공개(에페메랄)로만 처리
            await DeferAsync(ephemeral: true);

            // ✅ DB 조회 (예: Supabase)
            // rows는 userid, usernm, character(text[]), certdate, certtime 등 포함 가정
            var rows = await GetAllCertInfoAsync(); // 너가 가진 함수로 교체
            rows = rows ?? new List<CertInfoRow>();

            if (rows.Count == 0)
            {
                await Context.Channel.SendMessageAsync("조회 결과가 없습니다.");
                await DeleteOriginalResponseAsync(); // 생각중 제거
                return;
            }

            // ✅ 페이저 토큰 생성 + 상태 저장
            var token = Guid.NewGuid().ToString("N");
            CertPagerStore.States[token] = new CertPagerState
            {
                OwnerUserId = Context.User.Id,
                Rows = rows,
                Index = 0
            };

            // ✅ 첫 페이지 embed + buttons
            var state = CertPagerStore.States[token];
            var embed = BuildCertEmbed(state.Rows[state.Index], state.Index, state.Rows.Count, Context.Guild);
            var comp = BuildPagerComponents(token, state.Index, state.Rows.Count);

            // ✅ FollowupAsync 금지 → 채널에 바로 전송
            await Context.Channel.SendMessageAsync(embed: embed, components: comp);

            // ✅ 에페메랄 "생각중..." 제거
            await DeleteOriginalResponseAsync();
        }

        public sealed class CertPagerState
        {
            public ulong OwnerUserId { get; init; }
            public List<CertInfoRow> Rows { get; init; } = new();
            public int Index { get; set; }
            public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        }

        public static class CertPagerStore
        {
            // key = token
            public static readonly ConcurrentDictionary<string, CertPagerState> States = new();
        }

        // ✅ Prev
        [ComponentInteraction("cert:prev:*")]
        public async Task PagerPrevAsync(string token)
        {
            if (!CertPagerStore.States.TryGetValue(token, out var state))
            {
                await RespondAsync("세션이 만료되었습니다.", ephemeral: true);
                return;
            }

            // ✅ 조작자 제한(원 호출자만)
            if (Context.User.Id != state.OwnerUserId)
            {
                await RespondAsync("이 버튼은 호출자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            if (state.Index > 0) state.Index--;

            var row = state.Rows[state.Index];
            var embed = BuildCertEmbed(row, state.Index, state.Rows.Count, Context.Guild);
            var comp = BuildPagerComponents(token, state.Index, state.Rows.Count);

            await DeferAsync(ephemeral: true);                // 버튼 응답 ACK
            await ModifyOriginalResponseAsync(m =>
            {
                m.Embed = embed;
                m.Components = comp;
            });
        }

        // ✅ Next
        [ComponentInteraction("cert:next:*")]
        public async Task PagerNextAsync(string token)
        {
            if (!CertPagerStore.States.TryGetValue(token, out var state))
            {
                await RespondAsync("세션이 만료되었습니다.", ephemeral: true);
                return;
            }

            if (Context.User.Id != state.OwnerUserId)
            {
                await RespondAsync("이 버튼은 호출자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            if (state.Index < state.Rows.Count - 1) state.Index++;

            var row = state.Rows[state.Index];
            var embed = BuildCertEmbed(row, state.Index, state.Rows.Count, Context.Guild);
            var comp = BuildPagerComponents(token, state.Index, state.Rows.Count);

            await DeferAsync(ephemeral: true);
            await ModifyOriginalResponseAsync(m =>
            {
                m.Embed = embed;
                m.Components = comp;
            });
        }

        // ✅ Close (메시지 삭제 + 세션 제거)
        [ComponentInteraction("cert:close:*")]
        public async Task PagerCloseAsync(string token)
        {
            if (!CertPagerStore.States.TryGetValue(token, out var state))
            {
                await RespondAsync("세션이 만료되었습니다.", ephemeral: true);
                return;
            }

            if (Context.User.Id != state.OwnerUserId)
            {
                await RespondAsync("이 버튼은 호출자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            CertPagerStore.States.TryRemove(token, out _);

            await DeferAsync(ephemeral: true);
            await DeleteOriginalResponseAsync(); // ✅ 채널에 올라간 페이저 메시지 삭제
        }

        // ------------------------------
        // Embed / Components Builders
        // ------------------------------

        private Embed BuildCertEmbed(CertInfoRow row, int index, int total, SocketGuild guild)
        {
            // character가 text[] 라고 했으니 string[] 혹은 List<string> 형태 가정
            var names = row.Character ?? new List<string>();
            var clean = names.Select(x => (x ?? "").Replace("\r", " ").Replace("\n", " ").Trim())
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .ToList();
            var characterText = (clean.Count > 0) ? string.Join(", ", clean.Chunk(7).Select(c => string.Join(", ", c))) : "-";

            SocketGuildUser? User = null;

            if (ulong.TryParse(row.UserId, out var uid))
                User = Context.Guild?.GetUser(uid);

            string mfield = $"페이지 : **{index + 1} / {total}**\n\n" +
                            $"Character\n" +
                            $"`{characterText}`";

            var eb = new EmbedBuilder()
                .WithTitle($"전체 인증 정보 [{index + 1} / {total}]")
                .WithColor(Color.Green)
                .AddField("Discord", User?.Mention, true)
                .AddField("사용자명", row.UserNm, true)
                .AddField("UserId", row.UserId, true)
                .AddField("StoveId", row.StoveId, true)
                .AddField("가입일시", row.JoinDate + " " + row.JoinTime, true)
                .AddField("인증일시", row.CertTime + " " + row.CertTime, true)
                .AddField("Character", $"`{characterText}`", false)
                .WithFooter($"Develop by. 갱프");

            return eb.Build();
        }

        private static MessageComponent BuildPagerComponents(string token, int index, int total)
        {
            bool isFirst = index <= 0;
            bool isLast = index >= total - 1;

            return new ComponentBuilder()
                .WithButton("◀", customId: $"cert:prev:{token}", style: ButtonStyle.Primary, disabled: isFirst)
                .WithButton("닫기", customId: $"cert:close:{token}", style: ButtonStyle.Danger)
                .WithButton("▶", customId: $"cert:next:{token}", style: ButtonStyle.Primary, disabled: isLast)
                .Build();
        }
    }
}
