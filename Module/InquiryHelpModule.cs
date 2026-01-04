using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;

using LupeonBot.Client;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public class InquiryHelpModule : InteractionModuleBase<SocketInteractionContext>
    {
        private const ulong AdminRoleId = 557635038607573002;          // 관리자 역할
        private const ulong CategoryId = 884010216671309854;    // 문의 채널 카테고리
        private string mStdLv = ""; // 파일에서 읽어온 값(이미 갖고 있는 방식대로 세팅)        
        private string Emote = "<:pdiamond:907957436483248159>";

        [SlashCommand("신고공지", "문의및신고 공지를 표시합니다. (관리자전용)")]
        public async Task NoticeAsync()
        {
            if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            ComponentBuilder component = new ComponentBuilder()
                .WithButton(label: "문의하기", customId: "Inquiry", style: ButtonStyle.Primary)
                .WithButton(label: "신고하기", customId: "Help", style: ButtonStyle.Danger)
                .WithButton(label: "인증갱신", customId: "CertUpdate", style: ButtonStyle.Success);

            string m_body = string.Empty;
            string Emote = "<:pdiamond:907957436483248159>";

            m_body += "**[ 이용 방법 ]**" + Environment.NewLine;
            m_body += Emote + " 문의하기 : 루페온 디스코드와 관련된 내용 문의" + Environment.NewLine;
            m_body += Emote + " 신고하기 : 루페온 디스코드를 통해 일어난 일 신고" + Environment.NewLine;
            m_body += Emote + " 인증갱신 : 거래소 인증 후 캐릭을 변경하는 경우" + Environment.NewLine + Environment.NewLine;
            m_body += "**[ 유의사항 ]**" + Environment.NewLine;
            m_body += Emote + " **채널생성 후 5분이상 내용작성이 없을 경우 타임아웃 1주일 입니다.**";

            var NewEx = new EmbedBuilder()
                .WithTitle("고객센터 • 루페온")
                .WithColor(Discord.Color.Blue)
                .WithDescription(m_body)
                .WithFooter("Develop by. 갱프");

            //await admin.Guild.GetTextChannel(884395336959918100).SendMessageAsync(embed: NewEx.Build(), components: component.Build());
            await Context.Channel.SendMessageAsync(embed: NewEx.Build(), components: component.Build());
            await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
        }

        [ComponentInteraction("Inquiry")]
        public async Task InquiryAsync()
        {
            await DeferAsync(ephemeral: true);

            if (Context.User is not SocketGuildUser gu)
            {
                return;
            }

            var guild = gu.Guild;
            string m_disCord = Context.User.Username;
            ulong s_userid = Context.User.Id;
            string dateTime = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString();

            string m_Description =
                "**[문의 및 건의사항]**\n" +
                $"{Emote}문의 및 건의하실 내용을 해당 채널에 남겨주세요.\n" +
                $"{Emote}범위 : 루페온 디스코드와 관련된 모든내용";
            var 문의건의 = new EmbedBuilder()
               .WithColor(Color.Blue)
               .WithDescription(m_Description)
               .WithFooter($"{m_disCord}({s_userid}) 일시 : {dateTime}", Context.User.GetAvatarUrl(ImageFormat.Auto));

            var m_Inquiry = new ComponentBuilder()
                .WithButton(label: "종료", customId: "ChDispose", style: ButtonStyle.Danger)
                .WithButton(label: "타임아웃", customId: "TimeOut", style: ButtonStyle.Primary);

            string channelName = $"-문의_{s_userid}";

            // ✅ 기존 채널 있으면 그 채널로 안내만
            var exist = guild.TextChannels.FirstOrDefault(c => c.Name == channelName);
            if (exist != null)
            {
                await exist.SendMessageAsync($"{Context.User.Mention} 문의 및 건의 내용을 해당 채널에 작성해주시기 바랍니다");
                await FollowupAsync($"이미 문의 채널이 있어요: {exist.Mention}", ephemeral: true);
                return;
            }

            // ✅ 새 채널 생성
            var adminRole = guild.GetRole(AdminRoleId);

            string m_Text =
                $"**요청자 :** {Context.User.Mention}\n" +
                $"**관리자 :** {adminRole.Mention}";

            var channel = await guild.CreateTextChannelAsync(channelName, props => { props.CategoryId = CategoryId; });

            // ✅ 권한: 본인만 보기/쓰기 + 관리자 역할 보기/쓰기 (deny @everyone)
            // @everyone 차단 (안전)
            await channel.AddPermissionOverwriteAsync(guild.EveryoneRole, new OverwritePermissions(viewChannel: PermValue.Deny));

            // 요청자 허용
            await channel.AddPermissionOverwriteAsync(gu,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow));

            // 관리자 역할 허용
            await channel.AddPermissionOverwriteAsync(adminRole,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow));

            await channel.SendMessageAsync(text: m_Text, embed: 문의건의.Build(), components: m_Inquiry.Build());

            await FollowupAsync($"문의 채널을 만들었어요: {channel.Mention}", ephemeral: true);
        }

        [ComponentInteraction("Help")]
        public async Task HelpAsync()
        {
            if (Context.User is not SocketGuildUser gu)
            {
                return;
            }

            var guild = gu.Guild;
            string m_disCord = Context.User.Username;
            ulong s_userid = Context.User.Id;
            string dateTime = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString();

            // ✅ Embed 내용
            string m_Description =
                "**[신고하기]**\n" +
                $"{Emote}신고하실 내용을 해당 채널에 남겨주세요.\n" +
                $"{Emote}스크린샷 및 신고대상 디스코드 정보를 적어주세요.\n" +
                $"{Emote}신고범위 : 루페온 디스코드에서 발생한 일\n" +
                $"{Emote}고유ID 확인방법\n" +
                $"{Emote}사용자설정 - 고급 - 개발자모드 후 상대방 프로필에서 우클릭하여 ID 복사하기";

            var 문의신고 = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithDescription(m_Description)
                .WithFooter($"{m_disCord}({s_userid}) 일시 : {dateTime}", Context.User.GetAvatarUrl(ImageFormat.Auto));

            // ✅ 버튼
            var m_help = new ComponentBuilder()
                .WithButton(label: "종료", customId: "ChDispose", style: ButtonStyle.Danger)
                .WithButton(label: "타임아웃", customId: "TimeOut", style: ButtonStyle.Primary);

            // ✅ 기존 채널 있으면 안내만
            string chName = $"-신고_{s_userid}";
            var exist = guild.TextChannels.FirstOrDefault(c => c.Name == chName);

            if (exist != null)
            {
                await exist.SendMessageAsync($"{Context.User.Mention} 신고 내용을 해당 채널에 작성해주시기 바랍니다");
                await FollowupAsync($"이미 신고 채널이 있어요: {exist.Mention}", ephemeral: true);
                return;
            }

            // ✅ 역할 확인
            var adminRole = guild.GetRole(AdminRoleId);

            string m_Text =
                $"**요청자 :** {Context.User.Mention}\n" +
                $"**관리자 :** {adminRole.Mention}";

            // ✅ 채널 생성
            var channel = await guild.CreateTextChannelAsync(chName, props =>
            {
                props.CategoryId = CategoryId;
            });

            // ✅ 권한: 본인만 보기/쓰기 + 관리자 역할 보기/쓰기 (deny @everyone)
            // @everyone 차단 (안전)
            await channel.AddPermissionOverwriteAsync(guild.EveryoneRole, new OverwritePermissions(viewChannel: PermValue.Deny));

            // 요청자 허용
            await channel.AddPermissionOverwriteAsync(gu,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow));

            // 관리자 역할 허용
            await channel.AddPermissionOverwriteAsync(adminRole,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow));

            await channel.SendMessageAsync(text: m_Text, embed: 문의신고.Build(), components: m_help.Build());

            await FollowupAsync($"신고 채널을 만들었어요: {channel.Mention}", ephemeral: true);
        }

        [ComponentInteraction("CertUpdate")]
        public async Task CertUpdateAsync()
        {
            try
            {
                var path = Path.Combine(Environment.CurrentDirectory, "ExchangeInfo.txt");
                var exchangeInfo = File.Exists(path) ? File.ReadAllText(path) : "";

                var mInfo = exchangeInfo.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (mInfo.Length > 0)
                    mStdLv = Method.GetSplitString(mInfo[0], ':', 1).Trim();
                else
                    mStdLv = "";
            }
            catch
            {
                // 파일 IO 에러가 나도 아래에서 빈값 처리로 빠지게 둠
                mStdLv = "";
            }

            // 기준레벨 없으면 안내
            if (string.IsNullOrWhiteSpace(mStdLv))
            {                
                await RespondAsync("관리자에게 문의해주세요.", ephemeral: true);
                return;
            }

            var guildUser = Context.User as SocketGuildUser;
            int mRoleYn = 0;
            foreach (var role in guildUser.Roles)
            {
                if (role.Id == 1264901726251647086)
                {
                    mRoleYn++;
                    break;
                }
            }

            if (mRoleYn == 0)
            {
                await RespondAsync("거래소 역할을 보유 중이지 않습니다. 인증갱신을 할 수 없습니다.", ephemeral: true);
                return;
            }

            // 모달 띄우기
            await Context.Interaction.RespondWithModalAsync<CertModalData>("CertUpdateModal");
        }

        public class CertModalData : IModal
        {
            public string Title => "인증정보갱신";

            [InputLabel("로스트아크 캐릭터명을 입력해주세요.")]
            [ModalTextInput("NickName", placeholder: "인증받고자하는 캐릭터명", maxLength: 20)]
            public string NickName { get; set; } = "";

            [InputLabel("스토브 프로필 링크를 입력해주세요.")]
            [ModalTextInput("StoveUrl", placeholder: "예) https://profile.onstove.com/ko/123456", maxLength: 50)]
            public string StoveUrl { get; set; } = "";
        }

        [ModalInteraction("CertUpdateModal")]
        public async Task Modal_CertModal(CertModalData data)
        {
            string m_NickNm = "";
            string m_StoveId = "";

            m_NickNm = (data.NickName ?? "").Trim();

            if (Context.User is not SocketGuildUser user)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(m_NickNm))
            {
                await RespondAsync("❌ 캐릭터명을 입력해주세요.", ephemeral: true);
                return;
            }

            // 시간이 걸릴 수 있으니 defer
            await RespondAsync("인증 데이터를 확인 중입니다.", ephemeral: true);

            // 기준 충족 -> 프로필 조회 (네 기존 함수 그대로)
            await Method.GetSimpleProfile(m_NickNm);
            // ===============================================

            if (Method.TryExtractStoveId(data.StoveUrl, out var stoveId, out var url))
            {
                m_StoveId = stoveId;
            }
            else
            {
                await ModifyOriginalResponseAsync(m => m.Content = "❌ 스토브 프로필 링크가 올바르지 않습니다.");
                return;
            }

            try
            {
                var path = Path.Combine(Environment.CurrentDirectory, "ExchangeInfo.txt");
                var exchangeInfo = File.Exists(path) ? File.ReadAllText(path) : "";

                var mInfo = exchangeInfo.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (mInfo.Length > 0)
                    mStdLv = Method.GetSplitString(mInfo[0], ':', 1).Trim();
                else
                    mStdLv = "";
            }
            catch
            {
                // 파일 IO 에러가 나도 아래에서 빈값 처리로 빠지게 둠
                mStdLv = "";
            }

            // 아이템레벨 파싱: "Lv.1640.00" 형태 대응
            if (!Method.TryParseItemLevel(Method.m_아이템레벨, out var itemLv))
            {
                await FollowupAsync($"❌ 아이템레벨을 파싱하지 못했습니다: `{Method.m_아이템레벨}`", ephemeral: true);
                return;
            }

            if (!Method.TryParseStdLevel(mStdLv, out var stdLv))
            {
                await FollowupAsync($"❌ 기준레벨 설정값이 올바르지 않습니다: `{mStdLv}`", ephemeral: true);
                return;
            }

            // 기준 미달
            if (itemLv < stdLv)
            {
                string failDesc = $"캐릭명 : {m_NickNm}\n" +
                                  $"아이템 : {Method.m_아이템레벨}\n" +
                                  $"해당 캐릭터는 인증 기준레벨 미달 입니다.\n" +
                                  $"거래소인증은 {mStdLv} 이상의 캐릭으로만 가능합니다.";

                var s_embed = new EmbedBuilder()
                    .WithAuthor("🚨 요청실패")
                    .WithDescription(failDesc);

                await FollowupAsync(embed: s_embed.Build(), ephemeral: true);
                return;
            }

            var dbRow = await SupabaseClient.GetCertInfoByUserIdAsync(user.Id.ToString());

            if (dbRow != null)
            {
                var (ok, body) = await SupabaseClient.UpdateCertOnlyAsync(
                    userId: user.Id.ToString(),
                    stoveId: dbRow.StoveId,
                    characters: Method.m_보유캐릭,
                    certDate: DateTime.Now.ToString("yyyy-MM-dd"),
                    certTime: DateTime.Now.ToString("HH:mm")
                    );

                if (!ok)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ DB 업데이트 실패\n```{body}```");
                    return;
                }

                // StoveId 비교
                if (!string.Equals(dbRow.StoveId, m_StoveId, StringComparison.Ordinal))
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 저장된 정보와 신청자의 스토브 계정이 다릅니다.");
                    return;
                }

                //string m_Context = "갱신대상 : " + user.Mention + "``(" + user.Id.ToString() + ")``" + Environment.NewLine + Environment.NewLine
                //                 + "갱신캐릭 : ``'" + m_NickNm + "'``" + Environment.NewLine + Environment.NewLine
                //                 + "위 정보로 거래소 인증이 완료되었습니다.";

                //var s_embed = new EmbedBuilder()
                //    .WithAuthor("✅ 갱신완료")
                //    .WithDescription(m_Context)
                //    .WithColor(Color.Green)
                //    .WithThumbnailUrl(user.GetAvatarUrl(ImageFormat.Auto))
                //    .WithFooter("Develop by. 갱프　　　　　　　　　갱신일시 : " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString());

                //await ModifyOriginalResponseAsync(m => m.Content = "정상적으로 처리되었습니다.");
                //await ModifyOriginalResponseAsync(m => m.Embed = s_embed.Build());
            }
            else
            {
                string joindate = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd") ?? "";
                string jointime = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm") ?? "";

                var (ok, body) = await SupabaseClient.UpsertCertInfoAsync(
                    userId: user.Id.ToString(),
                    stoveId: m_StoveId,
                    userNm: user.Username,
                    characters: Method.m_보유캐릭_배열,
                    joinDate: joindate,
                    joinTime: jointime, 
                    certDate: DateTime.Now.ToString("yyyy-MM-dd"),
                    certTime: DateTime.Now.ToString("HH:mm")
                    );

                if (!ok)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ DB 업데이트 실패\n```{body}```");
                    return;
                }
            }

            string m_Context = "갱신대상 : " + user.Mention + "``(" + user.Id.ToString() + ")``" + Environment.NewLine + Environment.NewLine
                                 + "갱신캐릭 : ``'" + m_NickNm + "'``" + Environment.NewLine + Environment.NewLine
                                 + "위 정보로 거래소 인증이 완료되었습니다.";

            var ComPeleteEmbed = new EmbedBuilder()
                .WithAuthor("✅ 갱신완료")
                .WithDescription(m_Context)
                .WithColor(Color.Green)
                .WithThumbnailUrl(user.GetAvatarUrl(ImageFormat.Auto))
                .WithFooter("Develop by. 갱프　　　　　　　　　갱신일시 : " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString());

            await ModifyOriginalResponseAsync(m => m.Content = "정상적으로 처리되었습니다.");
            await ModifyOriginalResponseAsync(m => m.Embed = ComPeleteEmbed.Build());
        }

        // ✅ 종료 버튼
        [ComponentInteraction("ChDispose")]
        public async Task ChannelDisposeAsync()
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

        // ✅ 타임아웃 버튼
        [ComponentInteraction("TimeOut")]
        public async Task TimeOutHIAsync()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true); // 메시지 수정할 거라 defer
            
            var parts = Context.Channel.Name.Split('_');
            if (parts.Length < 2 || !ulong.TryParse(parts[1], out var s_userid))
            {
                await FollowupAsync("채널명에서 유저 ID를 추출할 수 없음 (형식: 문의채널_유저ID)", ephemeral: true);
                return;
            }

            var target = gu.Guild.GetUser(s_userid);
            if (target == null)
            {
                await FollowupAsync("해당 유저를 찾을 수 없음", ephemeral: true);
                return;
            }

            // ✅ 타임아웃 1일
            await target.SetTimeOutAsync(span: TimeSpan.FromDays(1), 
                                         options: new RequestOptions { AuditLogReason = "문의 및 신고 채널 생성 후 5분이상 무응답"});
            
            // ✅ 안내(에페메랄 + 채널에도 남기고 싶으면 아래 SendMessageAsync 추가)
            await FollowupAsync($"{target.Mention} 타임아웃 적용 완료", ephemeral: true);

            await Task.Delay(1000);

            // ✅ 채널 삭제
            await Method.DeleteChannelAsync(Context.Guild, (ITextChannel)Context.Channel, Context.Channel.Name);
        }
    }
}
