using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public partial class CertNoticeModule : InteractionModuleBase<SocketInteractionContext>
    {
        // ===== 네 서버 환경에 맞게 ID만 맞춰줘 =====
        private const ulong EveryoneRoleId = 513799663086862336;       // @everyone
        private const ulong TradeRoleId = 1264901726251647086;      // 거래소 역할(deny)
        private const ulong CertCategoryId = 595596190666588185;       // 인증채널 카테고리
        private const ulong GuideChannelId = 653484646260277248;       // 가이드 채널(링크)
        private const ulong CheckChannelId = 1000806935634919454;
        // =========================================

        // ===== 기존 전역/멤버 변수(네 코드에 있던 것들) =====
        private string mStdLv = ""; // 파일에서 읽어온 값(이미 갖고 있는 방식대로 세팅)
        private string m_NickNm = "";
        private string m_disCord = "";
        private ulong s_userid = 0;

        // GetServerInfo / GetProfile에서 채워진다고 가정
        private string m_서버 = "";
        private string m_직업 = "";
        private string m_아이템레벨 = "";  // 예: "Lv.1640.00"
        private string m_ImgLink = "";
        // ===================================================

        // ---------------------------
        // 0) 신청공지
        // ---------------------------
        [SlashCommand("신청공지", "거래소신청 공지를 표시합니다. (관리자전용)")]
        public async Task CertNotice()
        {
            var gu = Context.User as SocketGuildUser;

            if (!gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
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

            // 2) 기준레벨 없으면 안내 embed + 버튼 (ephemeral)
            if (string.IsNullOrWhiteSpace(mStdLv))
            {
                string mTempMsg =
                    "값을 지정해야합니다." + Environment.NewLine +
                    "입력버튼을 눌러서 값을 입력해주세요. (입력예시 : 1640.00)" + Environment.NewLine +
                    "입력 후 재실행버튼을 눌러서 공지를 정상적으로 표시하세요.";

                var tempEmbed = new EmbedBuilder()
                    .WithAuthor("기준레벨이 세팅되어 있지 않습니다.")
                    .WithColor(Color.DarkOrange)
                    .WithDescription(mTempMsg);

                var comp = new ComponentBuilder()
                    .WithButton(label: "레벨입력", customId: "SetStdLv", style: ButtonStyle.Primary)
                    .WithButton(label: "재실행", customId: "ReNotice", style: ButtonStyle.Success);

                await RespondAsync(embed: tempEmbed.Build(), components: comp.Build(), ephemeral: true);
                return;
            }

            await Method.SendNoticeAsync(Context.Channel, mStdLv, CheckChannelId);
            await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
        }

        // ---------------------------
        // 1) "역할신청" 버튼 핸들러
        // ---------------------------
        [ComponentInteraction("Cert")]
        public async Task Btn_Cert()
        {
            var guildUser = Context.User as SocketGuildUser;
            if (guildUser == null)
            {
                await RespondAsync("❌ 길드에서만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            // ✅ 기존 로직: everyone 제외한 역할이 하나도 없으면 차단
            int mRoleYn = 0;
            foreach (var role in guildUser.Roles)
            {
                if (role.Id != EveryoneRoleId) mRoleYn++;
            }

            if (mRoleYn == 0)
            {
                await RespondAsync("인증에 필요한 최소역할이 없습니다. 직업역할을 부여받으시기 바랍니다.", ephemeral: true);
                return;
            }

            // 모달 띄우기
            await Context.Interaction.RespondWithModalAsync<CertModalData>("CertModal");
        }

        // ---------------------------
        // 2) CertModal 제출 핸들러
        // ---------------------------
        [ModalInteraction("CertModal")]
        public async Task Modal_CertModal(CertModalData data)
        {
            var guildUser = Context.User as SocketGuildUser;
            if (guildUser == null)
            {
                await RespondAsync("❌ 길드에서만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            m_NickNm = (data.NickName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(m_NickNm))
            {
                await RespondAsync("❌ 캐릭터명을 입력해주세요.", ephemeral: true);
                return;
            }

            // 시간이 걸릴 수 있으니 defer
            await DeferAsync(ephemeral: true);

            // 기준 충족 -> 프로필 조회 (네 기존 함수 그대로)
            await Method.GetSimpleProfile(m_NickNm);
            // ===============================================

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

            // 디스코드 표시명
            m_disCord = Context.User.Username;
            s_userid = Context.User.Id;

            string m_dateTime = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString();

            string m_Emote = "<:pdiamond:907957436483248159>";
            string m_Emote3 = "<:reddiamond:1010548405765931080>";

            var guideChannelMention = $"<#{GuideChannelId}>";

            // 안내 embed
            string guideDesc = "**[거래소 인증방법]**\n" +
                              $"{m_Emote}{guideChannelMention}채널 확인\n" +
                              "**[유의사항]**\n" +
                              $"{m_Emote3} **``관리자가 확인 후 역할을 부여하기 때문에 일정시간이 소요됩니다.``**";

            var 인증채널 = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithDescription(guideDesc)
                .WithFooter($"{m_disCord}({s_userid}) 신청일시 : {m_dateTime}", Context.User.GetAvatarUrl(ImageFormat.Auto));

            // 캐릭터 정보 embed
            string charDesc =
                $"서ㅤ버 : {Method.m_서버}\n" +
                $"직ㅤ업 : {Method.m_직업}\n" +
                $"아이템 : {Method.m_아이템레벨}\n" +
                $"캐릭명 : {m_NickNm}\n";

            var m_charInfo = new EmbedBuilder()
                .WithAuthor("🔍 캐릭터정보 조회")
                .WithDescription(charDesc)
                .WithColor((Color)System.Drawing.Color.SkyBlue)
                .WithFooter($"Develop by. 갱프　　　　　　　　신청일시 : {m_dateTime}", Context.User.GetAvatarUrl(ImageFormat.Auto))
                .WithImageUrl(Method.StoveProfileImagePath)
                .WithThumbnailUrl(Method.m_ImgLink);

            var comps = new ComponentBuilder()
                .WithButton(label: "인증완료", customId: "Complete", style: ButtonStyle.Success)
                .WithButton(label: "채널종료", customId: "ExitCert", style: ButtonStyle.Danger)
                .WithButton(label: "타임아웃", customId: "CertTimeOut", style: ButtonStyle.Primary);

            // 이미 채널 있으면 거기로 안내 후 메시지
            var existing = guildUser.Guild.TextChannels.FirstOrDefault(c => c.Name == $"인증채널_{s_userid}");
            if (existing != null)
            {
                await existing.SendMessageAsync($"{guildUser.Mention} 해당 채널에 양식대로 글 작성바랍니다.");
                await FollowupAsync($"이미 인증채널이 있습니다: {existing.Mention}", ephemeral: true);
                return;
            }

            // 권한 세팅(기존 로직 유지)
            var everyone = guildUser.Guild.GetRole(EveryoneRoleId);
            var trade = guildUser.Guild.GetRole(TradeRoleId);

            var permissions = new List<Overwrite>
        {
            // 원본 그대로: allow/deny 비트값(68608) 쓰는 방식 유지
            new Overwrite(everyone.Id, PermissionTarget.Role, new OverwritePermissions(0, 68608)),
            new Overwrite(trade.Id,    PermissionTarget.Role, new OverwritePermissions(0, 68608)),
            new Overwrite(guildUser.Id, PermissionTarget.User, new OverwritePermissions(68608, 0))
        };

            // 채널 생성은 RestTextChannel 반환
            Discord.Rest.RestTextChannel created;
            try
            {
                created = await guildUser.Guild.CreateTextChannelAsync($"인증채널_{s_userid}", x =>
                {
                    x.CategoryId = CertCategoryId;
                    x.PermissionOverwrites = permissions;
                    x.Topic = $"거래소 인증채널 - {guildUser.Username}";
                });
            }
            catch (Exception ex)
            {
                await FollowupAsync($"❌ 채널 생성 실패: {ex.Message}", ephemeral: true);
                return;
            }

            string headerText = $"신청자 : {guildUser.Mention}\n신청캐릭 : {m_NickNm}";
            await created.SendMessageAsync(text: headerText,
                                           embeds: new[] { 인증채널.Build(), m_charInfo.Build() },
                                           components: comps.Build());

            await FollowupAsync($"✅ 인증채널이 생성되었습니다: <#{created.Id}>", ephemeral: true);
        }

        public class CertModalData : IModal
        {
            public string Title => "인증하기";

            [InputLabel("로스트아크 캐릭터명을 입력하세요.")]
            [ModalTextInput("NickName", placeholder: "인증받고자하는 캐릭터명", maxLength: 20)]
            public string NickName { get; set; } = "";
        }

        [ComponentInteraction("SetStdLv")]
        public async Task Btn_SetStdLv()
        {
            var gu = Context.User as SocketGuildUser;

            if (!gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            var modal = new ModalBuilder()
                .WithTitle("기준레벨 입력")
                .WithCustomId("SetStdLvModal")
                .AddTextInput(label: "기준레벨 (예: 1680.00)",
                              customId: "StdLv",
                              placeholder: "1680.00",
                              style: TextInputStyle.Short,
                              required: true,
                              maxLength: 10);

            await RespondWithModalAsync(modal.Build());
        }

        public class SetStdLvModalData : IModal
        {
            public string Title => "기준레벨 입력";

            [InputLabel("기준레벨 (예: 1640.00)")]
            [ModalTextInput("StdLv", placeholder: "1640.00", maxLength: 10)]
            public string StdLv { get; set; } = "";
        }

        [ModalInteraction("SetStdLvModal")]
        public async Task Modal_SetStdLv(SetStdLvModalData data)
        {
            var gu = Context.User as SocketGuildUser;

            if (!gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            var raw = (data.StdLv ?? "").Trim();

            if (!Method.TryParseStdLevel(raw, out var stdLv))
            {
                await RespondAsync($"❌ 숫자 형식이 올바르지 않습니다: `{raw}`", ephemeral: true);
                return;
            }

            // ✅ 파일 저장
            var path = Path.Combine(Environment.CurrentDirectory, "ExchangeInfo.txt");
            File.WriteAllText(path, $"StdLv:{stdLv:0.00}");

            // ✅ 클래스 멤버 갱신 (이거 중요)
            mStdLv = $"{stdLv:0.00}";

            await RespondAsync($"✅ 기준레벨이 `{mStdLv}` 로 저장되었습니다.\n이제 **재실행** 버튼을 눌러 공지를 다시 띄워주세요.", ephemeral: true);
        }

        [ComponentInteraction("ReNotice")]
        public async Task Btn_ReNotice()
        {
            var gu = Context.User as SocketGuildUser;

            if (!gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            // 기준레벨이 여전히 없으면 안내
            if (string.IsNullOrWhiteSpace(mStdLv))
            {
                await FollowupAsync("❌ 아직 기준레벨이 설정되지 않았습니다. **레벨입력**부터 해주세요.", ephemeral: true);
                return;
            }

            await Method.SendNoticeAsync(Context.Channel, mStdLv, CheckChannelId);
            await FollowupAsync("✅ 공지를 다시 표시했습니다.", ephemeral: true);
        }

        [SlashCommand("레벨초기화", "거래소 인증 기준레벨을 초기화합니다. (관리자 전용)")]
        public async Task ResetStdLv()
        {
            var gu = Context.User as SocketGuildUser;
            if (gu == null)
            {
                await RespondAsync("❌ 길드에서만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            if (!gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            try
            {
                var path = Path.Combine(Environment.CurrentDirectory, "ExchangeInfo.txt");

                // ✅ 지금 코드가 읽는 키(StdLv:)로 초기화
                File.WriteAllText(path, "StdLv:");

                // ✅ 메모리 값도 같이 비움
                mStdLv = "";

                await RespondAsync("✅ 기준레벨 초기화 완료.\n기존 신청공지를 삭제 후 `/신청공지`로 다시 공지 표시하세요.", ephemeral: true);
            }
            catch (Exception ex)
            {
                await RespondAsync($"❌ 초기화 실패: {ex.Message}", ephemeral: true);
            }
        }
    }
}
