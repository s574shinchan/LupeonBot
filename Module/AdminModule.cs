using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LupeonBot.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LupeonBot.Client.SupabaseClient;

namespace LupeonBot.Module
{
    public class AdminModule : InteractionModuleBase<SocketInteractionContext>
    {
        private const ulong BanLogChannelId = 598534025380102169;

        [SlashCommand("추방", "추방대상과 사유를 입력하여 추방합니다. (관리자전용)")]
        public async Task UserBanAsync(
            [Summary(description: "추방할 대상자")] string? 추방대상 = null, 
            [Summary(description: "추방 사유")] string? 추방사유 = null)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            // ✅ 입력 검증
            if (string.IsNullOrWhiteSpace(추방대상))
            {
                await RespondAsync("추방대상은 반드시 입력해야 합니다.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(추방사유))
            {
                await RespondAsync("추방사유는 반드시 입력해야 합니다.", ephemeral: true);
                return;
            }
            
            string reason = 추방사유.Trim();

            ulong targetId;
            string displayName;
            string mentionText;
            string? iconUrl = null;

            if (!ulong.TryParse(추방대상!.Trim(), out targetId))
            {
                await RespondAsync("유저ID는 숫자만 입력해주세요.", ephemeral: true);
                return;
            }

            // 가능하면 유저 정보도 가져와서 표시(실패해도 밴은 가능)
            var fetched = await Context.Client.GetUserAsync(targetId);
            if (fetched != null)
            {
                displayName = $"{fetched.Username}#{fetched.Discriminator}";
                mentionText = fetched.Mention;
                iconUrl = fetched.GetAvatarUrl(ImageFormat.Auto) ?? fetched.GetDefaultAvatarUrl();
            }
            else
            {
                displayName = targetId.ToString();
                mentionText = targetId.ToString();
            }

            // ✅ 핵심: ID로 밴 (서버에 없어도 100% 가능)
            await Context.Guild.AddBanAsync(targetId, pruneDays: 7, reason: reason);

            // ✅ 커맨드 응답
            await RespondAsync($"{displayName} 차단완료\n차단사유 : {reason}");

            // ✅ Embed 로그 (기존 포맷 유지)
            string s_disCord = "";
            s_disCord += $"**``유  저 : ``**{mentionText} ({displayName})\n";
            s_disCord += $"**``아이디 : ``**{targetId}";

            var banEmbed = new EmbedBuilder()
                .WithColor((Discord.Color)System.Drawing.Color.IndianRed)
                .WithAuthor(displayName, iconUrl)
                .WithTitle("**추방 및 차단(BAN)**")
                .AddField("ㅤ", s_disCord)
                .AddField("ㅤ", $"**``사유 : ``**{reason}")
                .AddField("ㅤ", "**``해당 조치에 대한 소명 및 이의제기는 문의 및 신고 채널을 이용해주시기 바랍니다. ``**", true)
                .WithFooter($"Develop by. 갱프　　　　　조치일시 : {DateTime.Now:yyyy-MM-dd HH:mm}");

            var logCh = Context.Guild.GetTextChannel(BanLogChannelId);
            if (logCh != null)
                await logCh.SendMessageAsync(embed: banEmbed.Build());

        }

        [SlashCommand("인증내역조회", "인증된 정보를 조회합니다. (관리자전용)")]
        public async Task GetCertUserInfoAsync([Summary(description: "디스코드 ID 또는 캐릭터명")] string? 조회대상 = null)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            조회대상 = (조회대상 ?? "").Trim();
            if (string.IsNullOrWhiteSpace(조회대상))
            {
                await RespondAsync("조회할 **디스코드 ID** 또는 **캐릭터명**을 입력해주세요.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            CertInfoRow? row;

            try
            {
                row = await FindCertInfoAsync(조회대상);
            }
            catch (Exception ex)
            {
                await FollowupAsync($"조회 중 오류가 발생했습니다.\n```{ex.Message}```", ephemeral: true);
                return;
            }

            if (row == null)
            {
                await FollowupAsync($"조회 결과가 없습니다. (입력: `{조회대상}`)", ephemeral: true);
                return;
            }

            // 출력용 문자열 구성
            string userId = row.UserId ?? "(없음)";
            string userNm = string.IsNullOrWhiteSpace(row.UserNm) ? "(없음)" : row.UserNm!;
            string stoveId = string.IsNullOrWhiteSpace(row.StoveId) ? "(없음)" : row.StoveId!;
            string chars = (row.Character?.Any() == true) ? string.Join("/", row.Character) : "(없음)";

            string joinDt = $"{row.JoinDate ?? "-"} {row.JoinTime ?? ""}".Trim();
            string certDt = $"{row.CertDate ?? "-"} {row.CertTime ?? ""}".Trim();

            // 디스코드 멘션 가능한지 시도 (userid가 ulong이면)
            string mention = userId;
            if (ulong.TryParse(userId, out var uid))
            {
                mention = $"<@{uid}>";
            }

            var eb = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("✅ 인증내역 조회 결과")
                .AddField("입력항목 : ", $"`{조회대상}`", inline: true)
                .AddField("Discord", $"{mention}", inline: true)
                .AddField("UserId", $"`{userId}`", inline: true)
                .AddField("사용자명", userNm, inline: true)
                .AddField("가입일시", joinDt, inline: true)
                .AddField("인증일시", certDt, inline: true)
                .AddField("StoveId", stoveId, inline: true)
                .AddField("캐릭터명", chars, inline: false)
                .WithFooter("Develop by. 갱프");

            await FollowupAsync(embed: eb.Build(), ephemeral: true);
        }
        
        [SlashCommand("역할일괄부여", "메인역할인 '루페온' 역할을 모든 유저에게 일괄로 부여합니다. (미인증제외, 관리자전용)")]
        public async Task SetMainRoleAddByAllUser()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            int total = gu.Guild.Users.Count;
            int processed = 0, added = 0, skipped = 0, failed = 0;

            EmbedBuilder BuildProgressEmbed(string desc, Color color)
            {
                return new EmbedBuilder()
                    .WithTitle("🔄 루페온 역할 지급 진행중")
                    .WithColor(color)
                    .WithDescription(desc)
                    .AddField("전체 유저", total, true)
                    .AddField("처리됨", processed, true)
                    .AddField("지급 성공", added, true)
                    .AddField("스킵", skipped, true)
                    .AddField("실패", failed, true)
                    .WithFooter($"시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }

            var msg = await Context.Channel.SendMessageAsync(embed: BuildProgressEmbed("시작합니다...", Color.Orange).Build());

            foreach (var user in gu.Guild.Users)
            {
                processed++;

                try
                {
                    if (user.IsBot) { skipped++; continue; }
                    if (user.Roles.Any(r => r.Id == 902213602889568316)) { skipped++; continue; }
                    if (user.Roles.Any(r => r.Id == 1457383863943954512)) { skipped++; continue; }

                    await user.AddRoleAsync(1457383863943954512);
                    added++;
                    await Task.Delay(500);
                }
                catch
                {
                    failed++;
                }

                if (processed % 5 == 0 || processed == total)
                {
                    await msg.ModifyAsync(m => m.Embed = BuildProgressEmbed($"처리 중... `{processed}/{total}`", Color.Orange).Build());
                }
            }

            // 완료
            var done = new EmbedBuilder()
                .WithTitle("✅ 루페온 역할 지급 완료")
                .WithColor(Color.Green)
                .WithDescription("모든 작업이 완료되었습니다.")
                .AddField("전체 유저", total, true)
                .AddField("지급 성공", added, true)
                .AddField("스킵", skipped, true)
                .AddField("실패", failed, true)
                .WithFooter($"완료: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .Build();

            await msg.ModifyAsync(m => m.Embed = done);
        }
    }
}


