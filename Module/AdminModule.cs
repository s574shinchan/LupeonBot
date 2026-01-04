using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public class AdminModule : InteractionModuleBase<SocketInteractionContext>
    {
        private const ulong BanLogChannelId = 598534025380102169;

        [SlashCommand("추방", "추방대상과 사유를 입력하여 추방합니다.")]
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
    }
}
