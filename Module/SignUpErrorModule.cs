using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DiscordBot.Program;

namespace LupeonBot.Module
{
    [GuildOnly(513799663086862336)]
    public class SignUpErrorModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("가입문의", "가입안되요 채널에 문의버튼생성")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SignUpErrorNoticeAsync()
        {
            if (Context.User is not SocketGuildUser user || !user.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            string m_body = string.Empty;
            string Emote = "<:pdiamond:907957436483248159>";

            m_body += "**[ 유의사항 ]**" + Environment.NewLine
                   + Emote + " 서버가입 절차를 진행하였으나, 가입이 되지 않은 경우에만 사용바랍니다." + Environment.NewLine + Environment.NewLine
                   + Emote + " 불필요한 문의채널 생성 시 24시간동안 디스코드 이용 제한하겠습니다. " + Environment.NewLine + Environment.NewLine
                   + Emote + " 생성된 채널의 기본양식은 지켜주시기 바랍니다. ";

            var component = new ComponentBuilder()
                .WithButton(label: "문의하기", customId: "SignUpError", style: ButtonStyle.Primary)
                .Build();

            var embed = new EmbedBuilder()
                .WithColor(Discord.Color.Blue)
                .WithDescription(m_body)
                .WithFooter("Develop by. 갱프")
                .Build();

            var textCh = user.Guild.GetTextChannel(932836388217450556);
            //await textCh.SendMessageAsync(embed: embed, components: component);
            await Context.Channel.SendMessageAsync(embed: embed, components: component);
            await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
        }

        [ComponentInteraction("SignUpError")]
        public async Task SignUpErrorAsync()
        {
            // 버튼/컴포넌트 눌렀을 때는 Interaction이므로 이렇게
            await DeferAsync(ephemeral: true);

            if (Context.User is not SocketGuildUser gu)
            {
                await FollowupAsync("길드 유저 정보를 가져올 수 없습니다.", ephemeral: true);
                return;
            }

            var guild = gu.Guild;
            var userId = gu.Id;
            var channelName = $"가입문의_{userId}";

            var everyone = guild.GetRole(513799663086862336);
            var nosignup = guild.GetRole(902213602889568316);
            ulong categoryId = 932836116221030460;

            // 1) 이미 채널이 있으면 찾아서 안내만
            var exist = guild.TextChannels.FirstOrDefault(c => c.Name == channelName);
            if (exist != null)
            {
                await exist.SendMessageAsync($"{gu.Mention} 해당 채널에 양식대로 글 작성바랍니다.");
                await FollowupAsync($"이미 생성된 채널이 있어요: {exist.Mention}", ephemeral: true);
                return;
            }

            // 2) 양식 Embed 만들기
            string Emote = "<:pdiamond:907957436483248159>"; // 예시

            string desc =
                "**[신청양식]**\n" +
                $"{Emote}디스코드이름 : \n" +
                $"{Emote}가입했던 캐릭명 : \n\n" +
                $"{Emote}위 양식대로 문의해주시기 바랍니다.";

            string discordTag = $"{gu.Username}#{gu.Discriminator}";
            string dateTime = $"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToShortTimeString()}";

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithDescription(desc)
                .WithFooter($"{discordTag}({userId}) 문의일시 : {dateTime}", gu.GetAvatarUrl(ImageFormat.Auto))
                .Build();

            // 3) 버튼 컴포넌트
            var components = new ComponentBuilder()
                .WithButton(label: "종료", customId: "ExitSign", style: ButtonStyle.Danger)
                .Build();

            // 4) 채널 생성
            var created = await guild.CreateTextChannelAsync(channelName, x =>
            {
                x.CategoryId = categoryId;
            });

            // 5) 권한 오버라이트 (네 기존 값 그대로 유지)
            await created.AddPermissionOverwriteAsync(gu, new OverwritePermissions(68608, 0));
            await created.AddPermissionOverwriteAsync(everyone, new OverwritePermissions(0, 68608));
            await created.AddPermissionOverwriteAsync(nosignup, new OverwritePermissions(0, 68608));

            // 6) 채널에 메시지 전송
            await created.SendMessageAsync(
                text: $"`문의자 : {gu.Mention}",
                embed: embed,
                components: components
            );

            await FollowupAsync($"가입문의 채널을 생성했어요: {created.Mention}", ephemeral: true);
        }

        [ComponentInteraction("ExitSign")]
        public async Task CloseChannel()
        {
            // 0) 관리자 체크
            if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);
            await Method.DeleteChannelAsync(Context.Guild, (ITextChannel)Context.Channel, Context.Channel.Name);
        }
    }
}

