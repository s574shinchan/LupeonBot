using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot;
using LupeonBot.Cache;
using LupeonBot.Client;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static DiscordBot.Program;
using static LupeonBot.Client.SupabaseClient;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace LupeonBot.Module
{
    [Group("거래소", "거래소 인증/갱신/조회/삭제")]
    public sealed class CertCommandModule : InteractionModuleBase<SocketInteractionContext>
    {
        #region 거래소인증 버튼 공지 및 레벨 초기화

        #region 상수
        // ===== 네 서버 환경에 맞게 ID만 맞춰줘 =====
        private const ulong EveryoneRoleId = 513799663086862336;       // @everyone
        private const ulong TradeRoleId = 1264901726251647086;      // 거래소 역할(deny)
        private const ulong CertCategoryId = 595596190666588185;       // 인증채널 카테고리
        private const ulong GuideChannelId = 653484646260277248;       // 가이드 채널(링크)
        private const ulong CheckChannelId = 1000806935634919454;
        // =========================================
        #endregion 상수

        #region 변수
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
        #endregion 변수

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

        // 1) "역할신청" 버튼 핸들러
        [ComponentInteraction("Cert", ignoreGroupNames: true)]
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

        // 2) CertModal 제출 핸들러
        [ModalInteraction("CertModal", ignoreGroupNames: true)]
        public async Task Modal_CertModal(CertModalData data)
        {
            try
            {
                var guildUser = Context.User as SocketGuildUser;

                m_NickNm = (data.NickName ?? "").Trim();

                if (string.IsNullOrWhiteSpace(m_NickNm))
                {
                    await RespondAsync("❌ 캐릭터명을 입력해주세요.", ephemeral: true);
                    return;
                }

                // 시간이 걸릴 수 있으니 defer
                await DeferAsync(ephemeral: true);

                // 기준 충족 -> 프로필 조회 (네 기존 함수 그대로)
                var profile = await ProfileMethod.GetSimpleProfile(m_NickNm);
                // ===============================================

                if (profile == null)
                {
                    await RespondAsync("❌ 캐릭터명을 확인해주세요.", ephemeral: true);
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
                if (!Method.TryParseItemLevel(profile.아이템레벨, out var itemLv))
                {
                    await FollowupAsync($"❌ 아이템레벨을 파싱하지 못했습니다: `{profile.아이템레벨}`", ephemeral: true);
                    return;
                }

                if (!Method.TryParseStdLevel(mStdLv, out var stdLv))
                {
                    await FollowupAsync($"❌ 관리자에게 문의해주세요.", ephemeral: true);
                    return;
                }

                // 기준 미달
                if (itemLv < stdLv)
                {
                    string failDesc = $"캐릭명 : {m_NickNm}\n" +
                                      $"아이템 : {profile.아이템레벨}\n" +
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
                    $"서ㅤ버 : {profile.서버}\n" +
                    $"직ㅤ업 : {profile.직업}\n" +
                    $"아이템 : {profile.아이템레벨}\n" +
                    $"캐릭명 : {m_NickNm}\n";

                var m_charInfo = new EmbedBuilder()
                    .WithAuthor("🔍 캐릭터정보 조회")
                    .WithDescription(charDesc)
                    .WithColor((Color)System.Drawing.Color.SkyBlue)
                    .WithFooter($"Develop by. 갱프　　　　　　　　신청일시 : {m_dateTime}", Context.User.GetAvatarUrl(ImageFormat.Auto))
                    .WithImageUrl(Method.StoveProfileImagePath)
                    .WithThumbnailUrl(profile.ImgLink);

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
            catch (Exception)
            {

            }
        }

        public class CertModalData : IModal
        {
            public string Title => "인증하기";

            [InputLabel("로스트아크 캐릭터명을 입력하세요.")]
            [ModalTextInput("NickName", placeholder: "인증받고자하는 캐릭터명", maxLength: 20)]
            public string NickName { get; set; } = "";
        }

        [ComponentInteraction("SetStdLv", ignoreGroupNames: true)]
        public async Task Btn_SetStdLv()
        {
            var gu = Context.User as SocketGuildUser;

            if (!gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            await RespondWithModalAsync<SetStdLvModalData>("SetStdLvModal");
        }

        public class SetStdLvModalData : IModal
        {
            public string Title => "기준레벨 입력";

            [InputLabel("기준레벨 (예: 1640.00)")]
            [ModalTextInput("StdLv", placeholder: "1640.00", maxLength: 10)]
            public string StdLv { get; set; } = "";
        }

        [ModalInteraction("SetStdLvModal", ignoreGroupNames: true)]
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

        [ComponentInteraction("ReNotice", ignoreGroupNames: true)]
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

        #endregion 거래소인증 버튼 공지 및 레벨 초기화

        #region 인증절차
        [ComponentInteraction("Complete", ignoreGroupNames: true)]
        public async Task CompleteAsync()
        {
            // 0) 관리자 체크
            if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            // 1) 버튼 응답 선점(타임아웃 방지) - 네가 하던 "진행중" 멘트
            await RespondAsync("인증에 필요한 작업이 진행 중입니다.", ephemeral: true);

            // 2) 대상 userid: 채널명 "인증채널_1234..."
            ulong s_userid = Convert.ToUInt64(Context.Channel.Name.Replace("인증채널_", string.Empty));
            SocketGuildUser target = admin.Guild.GetUser(s_userid);

            if (target == null)
            {
                await ModifyOriginalResponseAsync(m => m.Content = "대상 유저를 찾을 수 없습니다.");
                return;
            }

            // 3) 디스코드 표기명 (네 코드 그대로, 단 검사 대상은 target)
            string m_disCord = target.Username;
            string userNmTag = target.Mention;

            // 4) 생성일(계정 생성일) & 날짜/시간 포맷
            DateTime createDate = new DateTime(target.CreatedAt.Year, target.CreatedAt.Month, target.CreatedAt.Day);

            DateTime toDay = DateTime.UtcNow.AddHours(9);
            TimeSpan tmGap = toDay.Subtract(createDate);

            if (tmGap.TotalDays < 7)
            {
                await ModifyOriginalResponseAsync(m => m.Content = "❌ 계정 생성 7일 미만이라 인증 불가");
                return;
            }

            try
            {
                // 5) 신청캐릭 찾기: 최근 메시지에서 봇 메시지 "신청캐릭 :" 파싱
                string m_NickNm = "";
                string m_UserUrl = "";
                string m_StoveId = "";

                await foreach (RestMessage msg in Context.Channel.GetMessagesAsync(99).Flatten())
                {
                    // 신청자 링크 파싱 (유저가 보낸 메시지)
                    if (!msg.Author.IsBot && (msg.Content?.Contains("profile.onstove.com/ko/") ?? false))
                    {
                        if (Method.TryExtractStoveId(msg.Content, out var stoveId, out var url))
                        {
                            m_StoveId = stoveId;
                            m_UserUrl = url; // 실제 URL 저장
                        }
                        else
                        {
                            await ModifyOriginalResponseAsync(m => m.Content = "❌ 스토브 프로필 링크가 올바르지 않습니다.");
                            return;
                        }
                    }

                    if (msg.Author.IsBot)
                    {
                        if (msg.Content.Contains("신청캐릭 :"))
                        {
                            string tmpMsg = msg.Content.Replace("\n", "^").Replace("신청캐릭 : ", string.Empty).Trim();
                            m_NickNm = Method.GetSplitString(tmpMsg, '^', 1);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(m_NickNm))
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 신청캐릭 정보를 찾지 못했습니다.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(m_UserUrl) || string.IsNullOrWhiteSpace(m_StoveId))
                {
                    // ✅ 너 조건: StoveId 없으면 저장 안함
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 스토브 프로필 링크(끝 숫자)를 찾지 못했습니다. 링크를 채널에 다시 올려주세요.");
                    return;
                }

                // 6) 프로필 + 보유캐릭 가져오기
                var profile = await ProfileMethod.GetCertProfile(m_NickNm);

                // 7) DB 기존 데이터 조회 (UserId 기준)
                var dbRow = await GetCertInfoByUserIdAsync(s_userid.ToString());

                if (dbRow != null)
                {
                    // 1) StoveId 비교
                    if (!string.Equals(dbRow.StoveId, m_StoveId, StringComparison.Ordinal))
                    {
                        await ModifyOriginalResponseAsync(m => m.Content = "❌ 저장된 정보와 신청자의 스토브 계정이 다릅니다.");
                        return;
                    }

                    // 2) DB 캐릭 배열
                    var dbChars = new HashSet<string>(
                        dbRow.Character ?? Enumerable.Empty<string>(),
                        StringComparer.OrdinalIgnoreCase);

                    //var dbChars = (dbRow.Character ?? "")
                    //    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    //    .Select(x => x.Trim())
                    //    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // 3) 신청자 캐릭이 DB에 모두 포함되어 있는지 확인
                    if (!dbChars.Contains(m_NickNm.Trim()))
                    {
                        await ModifyOriginalResponseAsync(m => m.Content = $"❌ 디스코드 정보는 일치하지만, 신청 캐릭터 `{m_NickNm}` 이(가) DB 캐릭 목록에 존재하지 않습니다.");
                        return;
                    }

                    //foreach (var ch in dbChars)
                    //{
                    //    if (!m_NickNm.Contains(ch))
                    //    {
                    //        await ModifyOriginalResponseAsync(m => m.Content = $"❌ 디스코드 정보는 일치, 신청캐릭 `{m_NickNm}` 이(가) DB 캐릭 목록에 존재하지 않습니다.");
                    //        return;
                    //    }
                    //}

                    // 5) 전부 통과 → 이미 가입
                    await ModifyOriginalResponseAsync(m => m.Content = "❗ 이미 가입된 정보입니다.");
                    return;
                }

                string m_Context = string.Empty;
                m_Context += "인증대상 : ``'" + m_disCord + "(" + s_userid.ToString() + ")'``" + Environment.NewLine + Environment.NewLine;
                m_Context += "인증캐릭 : ``'" + m_NickNm + "'``" + Environment.NewLine + Environment.NewLine;
                m_Context += "위 정보로 거래소 인증이 완료되었습니다.";

                var s_embed = new EmbedBuilder();
                s_embed.WithAuthor("✅ 인증완료");
                s_embed.WithDescription(m_Context);
                s_embed.WithColor(Color.Green);
                s_embed.WithThumbnailUrl(target.GetAvatarUrl(ImageFormat.Auto));
                s_embed.WithFooter("ㆍ인증일시 : " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString());

                // ✅ 2) 먼저 원본 응답 수정
                await ModifyOriginalResponseAsync(m => m.Content = "인증완료");
                await ModifyOriginalResponseAsync(m => m.Embed = s_embed.Build());

                if (dbRow == null)
                {
                    // ✅ 여기 도달하면 DB에 없음 → 추가(Upsert로 넣으면 됨)
                    var (ok, body) = await SupabaseClient.UpsertCertInfoAsync(
                        userId: s_userid.ToString(),
                        stoveId: m_StoveId,
                        userNm: m_disCord,
                        characters: profile.보유캐릭_목록,
                        joinDate: target.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"),
                        joinTime: target.CreatedAt.ToLocalTime().ToString("HH:mm"),
                        certDate: toDay.ToString("yyyy-MM-dd"),
                        certTime: toDay.ToString("HH:mm")
                    );

                    if (!ok)
                    {
                        await admin.Guild.GetTextChannel(693460815067611196).SendMessageAsync($"❌ DB 저장 실패\n```{body}```");

                        await ModifyOriginalResponseAsync(m => m.Content = $"❌ DB 저장 실패\n```{body}```");
                        return;
                    }

                    // ✅ 4) DB 저장 성공했으면 역할 부여
                    SocketRole mRole = admin.Guild.GetRole(Convert.ToUInt64(1458795051030286499));
                    await target.AddRoleAsync(mRole);

                    // ✅ 5) 인증성공 메세지 로그(너 기존)
                    string Safe(string v) => string.IsNullOrWhiteSpace(v) ? "-" : v;

                    var embed = new EmbedBuilder()
                        .WithAuthor("✅ 인증 완료")
                        .WithColor(Color.Blue)
                        .AddField("닉네임", userNmTag, true)
                        .AddField("이름", $"`{Safe(m_disCord)}`", true)
                        .AddField("ID", $"`{s_userid}`", true)
                        .AddField("캐릭터", $"`{Safe(m_NickNm)}`", true)
                        .AddField("아이템 레벨", $"`{Safe(profile.아이템레벨)}`", true)
                        .AddField("전투력", $"`{Safe(profile.전투력)}`", true)
                        .AddField("인증일", $"`{DateTime.Now.ToString("yyyy-MM-dd")}`", true)
                        .AddField("인증시간", $"`{DateTime.Now.ToString("HH:mm")}`", true)
                        .AddField("인증자", $"`{admin}`", true)
                        .WithFooter("Develop by. 갱프");

                    string m_complete = userNmTag + "인증목록에 추가 및 역할부여완료";
                    await admin.Guild.GetTextChannel(693407092056522772).SendMessageAsync(text: m_complete, embed: embed.Build());
                    await Method.DeleteChannelAsync(Context.Guild, (ITextChannel)Context.Channel, Context.Channel.Name);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                try
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "처리 중 오류가 발생했습니다.");
                }
                catch { }
            }
        }

        [ComponentInteraction("ExitCert", ignoreGroupNames: true)]
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

        [ComponentInteraction("CertTimeOut", ignoreGroupNames: true)]
        public async Task TimeOutAsync()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true); // 메시지 수정할 거라 defer

            var selectMenu = new SelectMenuBuilder()
                .WithCustomId("SelectRow")
                .WithPlaceholder("타임아웃 사유를 선택해주세요(최대 1개)")
                .WithMinValues(1)
                .WithMaxValues(1)
                .AddOption("무응답", "채널생성 후 10분 이상 무응답")
                .AddOption("장소틀림", "스샷 장소가 트리시온이 아님")
                .AddOption("시간틀림", "스샷의 시간이 신청시간 보다 지나치게 과거임")
                .AddOption("채팅틀림", "스샷에 인게임채팅을 잘못 입력함")
                .AddOption("잘못누름", "Miss")
                .AddOption("직접입력", "Self");

            var component = new ComponentBuilder()
                .WithSelectMenu(selectMenu)
                .WithButton("인증완료", customId: "Complete", style: ButtonStyle.Success)
                .WithButton("채널종료", customId: "ExitCert", style: ButtonStyle.Danger)
                .WithButton("타임아웃 확정", customId: "TimeOutConfirm", style: ButtonStyle.Primary);

            // ✅ "버튼을 눌렀던 그 메시지"를 수정해야 하니까:
            // Context.Interaction은 SocketMessageComponent로 들어온다.
            if (Context.Interaction is SocketMessageComponent smc)
                await smc.Message.ModifyAsync(m => m.Components = component.Build());
        }

        // values[0] 에 선택된 value가 들어옴
        [ComponentInteraction("SelectRow", ignoreGroupNames: true)]
        public async Task SelectRowAsync(string[] values)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            string reason = values?.FirstOrDefault() ?? "";

            if (reason == "Miss")
            {
                // 셀렉트 숨기기: 컴포넌트를 아예 없애버리면 됨
                if (Context.Interaction is SocketMessageComponent smc)
                    await smc.Message.ModifyAsync(m => m.Components = new ComponentBuilder()
                        .WithButton("인증완료", customId: "Complete", style: ButtonStyle.Success)
                        .WithButton("채널종료", customId: "ExitCert", style: ButtonStyle.Danger)
                        .WithButton("타임아웃 확정", customId: "TimeOutConfirm", style: ButtonStyle.Primary)
                        .Build());

                await RespondAsync("✅ 잘못누름 처리됨(메뉴 숨김)", ephemeral: true);
                return;
            }

            if (reason == "Self")
            {
                await Context.Interaction.RespondWithModalAsync<TimeoutReasonModal>("TimeoutReasonModal");
                return;
            }

            // ✅ 일반 사유(문장 value) → 저장
            CertState.TimeoutReasonByChannel[Context.Channel.Id] = reason;
            await RespondAsync($"✅ 타임아웃 사유 선택됨: `{reason}`", ephemeral: true);
        }

        [ModalInteraction("TimeoutReasonModal", ignoreGroupNames: true)]
        public async Task TimeoutReasonModalAsync(TimeoutReasonModal modal)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            string reason = (modal.Reason ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                await RespondAsync("사유가 비어있습니다.", ephemeral: true);
                return;
            }

            CertState.TimeoutReasonByChannel[Context.Channel.Id] = reason;

            await RespondAsync($"✅ 직접입력 사유 저장됨: `{reason}`", ephemeral: true);
        }

        // 모달 데이터 바인딩용
        public class TimeoutReasonModal : IModal
        {
            public string Title => "타임아웃 사유 직접 입력";

            [InputLabel("사유")]
            [ModalTextInput("reason", TextInputStyle.Paragraph, placeholder: "타임아웃 사유를 입력하세요", maxLength: 200)]
            public string Reason { get; set; }
        }

        [ComponentInteraction("TimeOutConfirm", ignoreGroupNames: true)]
        public async Task TimeOutConfirmAsync()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var chId = Context.Channel.Id;
            CertState.TimeoutReasonByChannel.TryGetValue(chId, out var reason);
            reason ??= "(사유없음)";

            // ✅ 여기서 타임아웃/로그/역할회수/DM 등 네 처리 로직 수행
            // 예: 로그 채널에 reason 전송

            // 마지막에 채널 삭제
            await Method.DeleteChannelAsync(Context.Guild, (ITextChannel)Context.Channel, /*카테고리명*/ Context.Guild.GetCategoryChannel(((ITextChannel)Context.Channel).CategoryId ?? 0)?.Name ?? "");
            await FollowupAsync($"⛔ 타임아웃 처리 완료: `{reason}`", ephemeral: true);
        }

        public static class CertState
        {
            // 채널별 선택된 사유 저장
            public static ConcurrentDictionary<ulong, string> TimeoutReasonByChannel = new();
        }
        #endregion 인증절차

        #region 인증 전체 조회
        [SlashCommand("인증전체조회", "인증된 모든 정보 표시")]
        public async Task GetCertInfoTable()
        {
            if (Context.User is not SocketGuildUser)
            {
                return;
            }

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
        [ComponentInteraction("cert:prev:*", ignoreGroupNames: true)]
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
        [ComponentInteraction("cert:next:*", ignoreGroupNames: true)]
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
        [ComponentInteraction("cert:close:*", ignoreGroupNames: true)]
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
                .AddField("인증일시", row.CertDate + " " + row.CertTime, true)
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
        #endregion 인증 전체 조회

        #region 인증개별조회
        [SlashCommand("인증내역조회", "인증된 정보를 조회합니다. (관리자전용)")]
        public async Task GetCertUserInfoAsync([Summary(description: "디스코드 ID 또는 캐릭터명")] string? 조회대상 = null)
        {
            if (Context.User is not SocketGuildUser gu)
            {
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

        #endregion 인증개별조회

        #region 인증갱신공지
        [SlashCommand("갱신공지", "기간내 거래소 인증데이터 갱신을 위한 버튼 표시")]
        public async Task CertInfoUpdate()
        {
            string Emote = "<:pdiamond:907957436483248159>";
            string m_body = string.Empty;
            m_body += Emote + " 아래의 인증갱신 버튼을 눌러서 정보를 입력하시면 됩니다." + Environment.NewLine;
            m_body += Emote + " 별도의 인증채널이 생기지 않습니다." + Environment.NewLine;
            m_body += Emote + " 거래소 역할이 새로 부여되는 것이 아닙니다." + Environment.NewLine;
            m_body += Emote + " 인증된 데이터를 최신화 하기 위한 목적입니다." + Environment.NewLine + Environment.NewLine;
            m_body += "**[ 유의사항 ]**" + Environment.NewLine;
            m_body += Emote + " 기준레벨보다 낮은 경우 갱신되지 않습니다." + Environment.NewLine;
            m_body += Emote + " 미갱신자는 추후 거래소 역할이 회수될 예정입니다.";

            var embed = new EmbedBuilder()
              .WithTitle("거래소 인증갱신 • 루페온")
              .WithColor(Discord.Color.Green)
              .WithDescription(m_body)
              .WithImageUrl(Method.StoveProfileImagePath)
              .WithFooter("Develop by. 갱프");

            var component = new ComponentBuilder()
              .WithButton(label: "인증정보갱신", customId: "CertInfoUpdate", style: ButtonStyle.Success);

            await Context.Channel.SendMessageAsync(embed: embed.Build(), components: component.Build());
            await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
        }

        public class CertUpModalData : IModal
        {
            public string Title => "인증정보갱신";

            [InputLabel("로스트아크 캐릭터명을 입력해주세요.")]
            [ModalTextInput("NickName", placeholder: "인증받고자하는 캐릭터명", maxLength: 20)]
            public string NickName { get; set; } = "";

            [InputLabel("스토브 프로필 링크를 입력해주세요.")]
            [ModalTextInput("StoveUrl", placeholder: "예) https://profile.onstove.com/ko/123456", maxLength: 50)]
            public string StoveUrl { get; set; } = "";
        }

        [ComponentInteraction("CertInfoUpdate", ignoreGroupNames: true)]
        public async Task CertInfoUpdateAsync()
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
            await Context.Interaction.RespondWithModalAsync<CertUpModalData>("CertInfoUpdateModal");
        }

        [ModalInteraction("CertInfoUpdateModal", ignoreGroupNames: true)]
        public async Task Modal_CertUpModal(CertUpModalData data)
        {
            string m_NickNm = "";
            string m_StoveId = "";

            try
            {
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
                var profile = await ProfileMethod.GetSimpleProfile(m_NickNm);
                // ===============================================

                if (profile == null)
                {
                    await RespondAsync("❌ 캐릭터명을 확인해주세요.", ephemeral: true);
                    return;
                }

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
                if (!Method.TryParseItemLevel(profile.아이템레벨, out var itemLv))
                {
                    await FollowupAsync($"❌ 아이템레벨을 파싱하지 못했습니다: `{profile.아이템레벨}`", ephemeral: true);
                    return;
                }

                if (!Method.TryParseStdLevel(mStdLv, out var stdLv))
                {
                    await FollowupAsync($"❌ 관리자에게 문의해주세요.", ephemeral: true);
                    return;
                }

                // 기준 미달
                if (itemLv < stdLv)
                {
                    string failDesc = $"캐릭명 : {m_NickNm}\n" +
                                      $"아이템 : {profile.아이템레벨}\n" +
                                      $"해당 캐릭터는 인증 기준레벨 미달 입니다.\n" +
                                      $"거래소인증은 {mStdLv} 이상의 캐릭으로만 가능합니다.";

                    var s_embed = new EmbedBuilder()
                        .WithAuthor("🚨 요청실패")
                        .WithDescription(failDesc);

                    await FollowupAsync(embed: s_embed.Build(), ephemeral: true);
                    return;
                }

                DateTime dt = DateTime.UtcNow.AddHours(9);
                string m_CertDate = dt.ToString("yyyy-MM-dd"); // 2026-01-06
                string m_CertTime = dt.ToString("HH:mm");      // 01:23

                var dbRow = await SupabaseClient.GetCertInfoByUserIdAsync(user.Id.ToString());

                if (dbRow != null)
                {
                    var (ok, body) = await SupabaseClient.UpdateCertOnlyAsync(
                        userId: user.Id.ToString(),
                        stoveId: dbRow.StoveId,
                        characters: profile.보유캐릭_목록,
                        certDate: m_CertDate,
                        certTime: m_CertTime
                        );

                    if (!ok)
                    {
                        await ModifyOriginalResponseAsync(m => m.Content = $"❌ 관리자에게 문의해주세요.");
                        return;
                    }

                    // StoveId 비교
                    if (!string.Equals(dbRow.StoveId, m_StoveId, StringComparison.Ordinal))
                    {
                        await ModifyOriginalResponseAsync(m => m.Content = "❌ 저장된 정보와 신청자의 스토브 계정이 다릅니다.");
                        return;
                    }
                }
                else
                {
                    string joindate = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd") ?? "";
                    string jointime = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm") ?? "";

                    var (ok, body) = await SupabaseClient.UpsertCertInfoAsync(
                        userId: user.Id.ToString(),
                        stoveId: m_StoveId,
                        userNm: user.Username,
                        characters: profile.보유캐릭_목록,
                        joinDate: joindate,
                        joinTime: jointime,
                        certDate: m_CertDate,
                        certTime: m_CertTime
                        );

                    if (!ok)
                    {
                        await ModifyOriginalResponseAsync(m => m.Content = $"❌ 관리자에게 문의해주세요.");
                        return;
                    }
                }

                // ✅ 4) DB 저장 성공했으면 역할 부여
                //거래소 삭제
                SocketRole mRole = user.Guild.GetRole(Convert.ToUInt64(1264901726251647086));
                await user.RemoveRoleAsync(mRole);

                //신거래소 추가
                SocketRole mNewRole = user.Guild.GetRole(Convert.ToUInt64(1458795051030286499));
                await user.AddRoleAsync(mNewRole);

                string m_Context = "";
                m_Context += "갱신대상 : " + user.Mention + "``(" + user.Id.ToString() + ")``" + Environment.NewLine + Environment.NewLine;
                m_Context += "갱신캐릭 : ``'" + m_NickNm + "'``" + Environment.NewLine + Environment.NewLine;
                m_Context += "위 정보로 거래소 인증내역이 갱신되었습니다.";

                var ComPeleteEmbed = new EmbedBuilder()
                    .WithAuthor("✅ 갱신완료")
                    .WithDescription(m_Context)
                    .WithColor(Color.Green)
                    .WithThumbnailUrl(user.GetAvatarUrl(ImageFormat.Auto))
                    .WithFooter("Develop by. 갱프　　　　　　　　　갱신일시 : " + m_CertDate + " " + m_CertTime);

                await ModifyOriginalResponseAsync(m => m.Content = "정상적으로 처리되었습니다.");
                await ModifyOriginalResponseAsync(m => m.Embed = ComPeleteEmbed.Build());
            }
            catch (Exception)
            {

            }
        }
        #endregion 인증갱신공지

        #region 인증삭제
        [SlashCommand("인증삭제", "디스코드ID 또는 캐릭터명 (미리보기 후 삭제/취소)")]
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

                var row = await GetByUserIdAsync(input);
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

                rows = await SearchByCharacterAsync(input);
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
                DiscordId = Context.User.Id,
                CreatedUtc = DateTime.UtcNow.AddHours(9),
                Input = input,
                Rows = rows,
                Index = 0,
                IsSingleMode = singleMode
            };

            CertDeleteCache.Map[token] = state;

            var embed = BuildViewEmbed(state);
            var comps = BuildComponents(token, state);

            await RespondAsync(embed: embed, components: comps, ephemeral: true);
        }

        [ComponentInteraction("certdel:*:*", ignoreGroupNames: true)]
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
            if (state.DiscordId != Context.User.Id)
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
                    m.Embed = BuildResultEmbed("⏰ 만료", "요청이 만료되었습니다. 다시 `/인증삭제` 해주세요.", success: false);
                    m.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // Prev/Next: 같은 메시지 수정
            if (action == "prev")
            {
                state.Index = Math.Max(0, state.Index - 1);
                var embed = BuildViewEmbed(state);
                var comps = BuildComponents(token, state);

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
                var embed = BuildViewEmbed(state);
                var comps = BuildComponents(token, state);

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
                    m.Embed = BuildResultEmbed(
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
                        m.Embed = BuildResultEmbed("❌ 실패", "UserId가 비어있어 삭제할 수 없습니다.", success: false);
                        m.Components = new ComponentBuilder().Build();
                    });
                    return;
                }

                await SupabaseClient.DeleteByUserIdAsync(userId);

                // 나머지 row1,row2는 자동 취소 = 캐시 제거로 종료
                CertDeleteCache.Map.TryRemove(token, out _);

                await ModifyOriginalResponseAsync(m =>
                {
                    m.Embed = BuildResultEmbed("✅ 삭제 완료", $"삭제 처리되었습니다.\n삭제된 UserId: `{userId}`", success: true);
                    m.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // 알 수 없는 action
            await FollowupAsync("⚠️ 알 수 없는 동작입니다.", ephemeral: true);
        }

        public Embed BuildViewEmbed(CertDeleteCache.State s)
        {
            var row = s.Rows[s.Index];
            var names = row.Character ?? new List<string>();
            var clean = names.Select(x => (x ?? "").Replace("\r", " ").Replace("\n", " ").Trim())
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .ToList();

            string character = (clean.Count > 0) ? string.Join(", ", clean) : "(no character)";
            if (character.Length > 900) character = character.Substring(0, 900) + "…";

            SocketGuildUser? User = null;

            if (ulong.TryParse(row.UserId, out var uid))
                User = Context.Guild?.GetUser(uid);

            string description = string.Empty;
            description += $"입　력 : **{s.Input}**\n";
            description += $"페이지 : **{s.Index + 1} / {s.Rows.Count}**\n";
            description += $"모　드 : {(s.IsSingleMode ? "디스코드ID 검색" : "캐릭터명 검색")}\n\n";
            description += $"**Character**\n`{character}`\n\n";
            description += $"인증일시 : {row.CertDate} {row.CertTime}";

            var eb = new EmbedBuilder()
                .WithTitle("🧾 인증정보 삭제전 미리보기")
                .WithColor(Color.Orange)
                .AddField("Discord", User?.Mention ?? "(없음)", true)
                .AddField("UserId", row.UserId ?? "-", true)
                .AddField("정보", description, false)
                .WithFooter($"Develop by. 갱프");

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
        #endregion 인증삭제
    }

    [Group("서버가입", "서버가입과 관련된 명령어")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public sealed class SingUpModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("가입공지", "서버가입버튼 표시")]
        public async Task SignUpNoticeAsync()
        {
            if (Context.User is not SocketGuildUser user)
            {
                return;
            }

            string m_body = string.Empty;
            string Emote = "<:pdiamond:907957436483248159>";

            m_body += "**[ 서버가입 ]**" + Environment.NewLine;
            m_body += Emote + " 1. 서버가입 버튼클릭" + Environment.NewLine;
            m_body += Emote + " 2. 캐릭터명입력" + Environment.NewLine;
            m_body += Emote + " 3. 아래 이미지 참고하여 스토브 링크 입력 후 완료" + Environment.NewLine + Environment.NewLine;
            m_body += "**[ 유의사항 ]**" + Environment.NewLine;
            m_body += Emote + " 서버 내 채널이용은 기본인증을 완료해야 이용가능합니다." + Environment.NewLine;
            m_body += Emote + " 매주 수요일 정기점검 시간(~ 10:00)" + Environment.NewLine;
            m_body += Emote + " 해당 시간에는 서버가입이 되지 않습니다." + Environment.NewLine;
            m_body += Emote + " 점검이 끝난 후 공식홈페이지가 접속가능한 경우" + Environment.NewLine;
            m_body += Emote + " 가입절차를 재진행하시면 됩니다.";

            var embed = new EmbedBuilder()
                .WithColor((Discord.Color)System.Drawing.Color.FromArgb(240, 189, 109))
                .WithDescription(m_body)
                //.WithImageUrl("attachment://스토브프로필.png")
                .WithImageUrl(Method.StoveProfileImagePath)
                .WithFooter("Develop by. 갱프")
                .Build();

            var component = new ComponentBuilder()
                .WithButton(label: "서버가입", customId: "SignUp", style: ButtonStyle.Success)
                .Build();

            //await Context.Channel.SendFileAsync(Method.StoveProfileImagePath, embed: embed, components: component);
            await Context.Channel.SendMessageAsync(embed: embed, components: component);
            await RespondAsync("표시완료", ephemeral: true);
        }

        [ComponentInteraction("SignUp", ignoreGroupNames: true)]
        public async Task SignUpAsync()
        {
            await Context.Interaction.RespondWithModalAsync<SingUpModalData>("SignUpModal");
        }

        public class SingUpModalData : IModal
        {
            public string Title => "가입정보입력";

            [InputLabel("로스트아크 캐릭터명을 입력해주세요.")]
            [ModalTextInput("NickName", placeholder: "본캐, 부캐 상관없습니다.", maxLength: 20)]
            public string NickName { get; set; } = "";

            [InputLabel("스토브 프로필 링크를 입력해주세요.")]
            [ModalTextInput("StoveUrl", placeholder: "예) https://profile.onstove.com/ko/123456", maxLength: 50)]
            public string StoveUrl { get; set; } = "";
        }

        [ModalInteraction("SignUpModal", ignoreGroupNames: true)]
        public async Task Modal_CertModal(SingUpModalData data)
        {
            string m_NickNm = "";
            string m_StoveId = "";

            try
            {
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
                await RespondAsync("서버가입에 필요한 데이터를 확인 중입니다.", ephemeral: true);

                // 기준 충족 -> 프로필 조회 (네 기존 함수 그대로)
                var profile = await ProfileMethod.GetSimpleProfile(m_NickNm);
                // ===============================================
                if (profile == null)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ 가입실패, 캐릭터명을 확인해주세요.");
                    return;
                }

                if (Method.TryExtractStoveId(data.StoveUrl, out var stoveId, out var url))
                {
                    m_StoveId = stoveId;
                }
                else
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 스토브 프로필 링크가 올바르지 않습니다.");
                    return;
                }

                //var dbBanRow = await SupabaseClient.GetBanUserInfoAsync(user.Id.ToString(), m_NickNm);

                //if (dbBanRow != null)
                //{
                //    await ModifyOriginalResponseAsync(m => m.Content = "가입이 불가능한 계정입니다.");
                //    await user.KickAsync();
                //    return;
                //}

                DateTime dt = DateTime.UtcNow.AddHours(9);
                string m_joinDate = dt.ToString("yyyy-MM-dd"); // 2026-01-06
                string m_joinTime = dt.ToString("HH:mm");      // 01:23

                if (profile.보유캐릭_목록 == null)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ 가입실패, 캐릭터명을 확인해주세요.");
                    return;
                }

                var dbRow = await SupabaseClient.GetSingUpByUserIdAsync(Context.User.Id.ToString());

                if (dbRow == null)
                {
                    var (ok, body) = await SupabaseClient.UpsertSingUpAsync(
                        userId: user.Id.ToString(),
                        stoveId: m_StoveId,
                        userNm: user.Username,
                        characters: profile.보유캐릭_목록,
                        joinDate: m_joinDate, // 2026-01-06
                        joinTime: m_joinTime // 오전 01:23
                        );

                    if (!ok)
                    {
                        var nosign = user.Guild.GetTextChannel(932836388217450556);
                        await ModifyOriginalResponseAsync(m => m.Content = $"❌ 가입실패, {nosign.Mention} 채널로 이동하여 문의해주세요.");
                        return;
                    }

                    foreach (var role in user.Guild.Roles)
                    {
                        if (role.Name == profile.직업)
                        {
                            await user.AddRoleAsync(role);
                            break;
                        }
                    }
                    await user.AddRoleAsync(1457383863943954512);   // 루페온
                    await user.RemoveRoleAsync(902213602889568316); // 미인증
                    await ModifyOriginalResponseAsync(m => m.Content = "정상적으로 가입처리 되었습니다.");

                    //#region 유저정보
                    ////계정생성일
                    //string creatDate = user.CreatedAt.ToString("yyyy-MM-dd");
                    ////서버가입일
                    //string JoinDate = user.JoinedAt.ToString();
                    //DateTime dt = DateTime.Parse(JoinDate);
                    //JoinDate = dt.ToShortDateString();

                    ////디스코드정보
                    //string s_disCord = string.Empty;
                    //s_disCord = "``유저정보 :``" + user.Mention + " (" + user.Username + ")" + Environment.NewLine;
                    //s_disCord += "``아 이 디 :``" + user.Id + Environment.NewLine;
                    //s_disCord += "``계정생성일 :``" + creatDate + Environment.NewLine;
                    //s_disCord += "``서버가입일 :``" + JoinDate;

                    ////로아정보
                    //string m_lostArk = string.Empty;
                    //m_lostArk = "``레벨 :``" + Method.m_아이템레벨 + Environment.NewLine;
                    //m_lostArk += "``캐릭터 :``" + m_NickNm + Environment.NewLine;
                    //m_lostArk += "``클래스 :``" + Method.m_직업 + Environment.NewLine;
                    //m_lostArk += "``서버 :``" + Method.m_서버;

                    //string m_CharList = string.Empty;
                    //m_CharList = "``보유캐릭 :``" + Method.m_보유캐릭;

                    ////갱신정보
                    //string m_renewal = string.Empty;
                    //m_renewal = "가입일시 : " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString();
                    //#endregion 유저정보

                    //var Embed = new EmbedBuilder();
                    //Embed.WithTitle("서버가입정보");
                    //Embed.WithColor(Discord.Color.DarkTeal);
                    //Embed.AddField("**Discord**", s_disCord, true);
                    //Embed.AddField("**LostArk**", m_lostArk, true);
                    //Embed.AddField("**CharList**", m_CharList, false);
                    //Embed.WithFooter(m_renewal);

                    //await user.Guild.GetTextChannel(903242262677454958).SendMessageAsync(embed: Embed.Build());
                }
            }
            catch (Exception)
            {
            }
        }

        [SlashCommand("가입문의", "가입안되요 채널에 문의버튼생성")]
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

        [ComponentInteraction("SignUpError", ignoreGroupNames: true)]
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

        [ComponentInteraction("ExitSign", ignoreGroupNames: true)]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public sealed class InquiryHelpModule : InteractionModuleBase<SocketInteractionContext>
    {
        private const ulong AdminRoleId = 557635038607573002;          // 관리자 역할
        private const ulong CategoryId = 884010216671309854;    // 문의 채널 카테고리
        private string mStdLv = ""; // 파일에서 읽어온 값(이미 갖고 있는 방식대로 세팅)        
        private string Emote = "<:pdiamond:907957436483248159>";

        [SlashCommand("신고공지", "문의및신고 공지를 표시합니다. (관리자전용)")]
        public async Task NoticeAsync()
        {
            var component = new ComponentBuilder()
                .WithButton(label: "문의하기", customId: "Inquiry", style: ButtonStyle.Primary)
                .WithButton(label: "신고하기", customId: "Help", style: ButtonStyle.Danger);
            //.WithButton(label: "인증갱신", customId: "CertUpdate", style: ButtonStyle.Success);

            string m_body = string.Empty;
            string Emote = "<:pdiamond:907957436483248159>";

            m_body += "**[ 이용 방법 ]**" + Environment.NewLine;
            m_body += Emote + " 문의하기 : 루페온 디스코드와 관련된 내용 문의" + Environment.NewLine;
            m_body += Emote + " 신고하기 : 루페온 디스코드를 통해 일어난 일 신고" + Environment.NewLine + Environment.NewLine;
            //m_body += Emote + " 인증갱신 : 거래소 인증 후 캐릭을 변경하는 경우" + Environment.NewLine + Environment.NewLine;
            m_body += "**[ 유의사항 ]**" + Environment.NewLine;
            m_body += Emote + " **채널생성 후 5분이상 내용작성이 없을 경우 타임아웃 1주일 입니다.**";

            var NewEx = new EmbedBuilder()
                .WithTitle("고객센터 • 루페온")
                .WithColor(Discord.Color.Blue)
                .WithDescription(m_body)
                //.WithImageUrl(Method.StoveProfileImagePath)
                .WithFooter("Develop by. 갱프");

            //await admin.Guild.GetTextChannel(884395336959918100).SendMessageAsync(embed: NewEx.Build(), components: component.Build());
            await Context.Channel.SendMessageAsync(embed: NewEx.Build(), components: component.Build());
            await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
        }

        [ComponentInteraction("Inquiry", ignoreGroupNames: true)]
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
            DateTime dt = DateTime.UtcNow.AddHours(9);

            string m_Description =
                "**[문의 및 건의사항]**\n" +
                $"{Emote}문의 및 건의하실 내용을 해당 채널에 남겨주세요.\n" +
                $"{Emote}범위 : 루페온 디스코드와 관련된 모든내용";
            var 문의건의 = new EmbedBuilder()
               .WithColor(Color.Blue)
               .WithDescription(m_Description)
               .WithFooter($"{m_disCord}({s_userid}) 일시 : {dt.ToString("yyyy-MM-dd HH:mm:ss")}", Context.User.GetAvatarUrl(ImageFormat.Auto));

            var m_Inquiry = new ComponentBuilder()
                .WithButton(label: "종료", customId: "ChDispose", style: ButtonStyle.Danger)
                .WithButton(label: "타임아웃", customId: "TimeOut", style: ButtonStyle.Primary);

            string channelName = $"문의채널_{s_userid}";

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

        [ComponentInteraction("Help", ignoreGroupNames: true)]
        public async Task HelpAsync()
        {
            if (Context.User is not SocketGuildUser gu)
            {
                return;
            }

            var guild = gu.Guild;
            string m_disCord = Context.User.Username;
            ulong s_userid = Context.User.Id;

            DateTime dt = DateTime.UtcNow.AddHours(9);

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
                .WithFooter($"{m_disCord}({s_userid}) 일시 : {dt.ToString("yyyy-MM-dd HH:mm:ss")}", Context.User.GetAvatarUrl(ImageFormat.Auto));

            // ✅ 버튼
            var m_help = new ComponentBuilder()
                .WithButton(label: "종료", customId: "ChDispose", style: ButtonStyle.Danger)
                .WithButton(label: "타임아웃", customId: "TimeOut", style: ButtonStyle.Primary);

            // ✅ 기존 채널 있으면 안내만
            string chName = $"신고채널_{s_userid}";
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

        [ComponentInteraction("CertUpdate", ignoreGroupNames: true)]
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

        [ModalInteraction("CertUpdateModal", ignoreGroupNames: true)]
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
            var profile = await ProfileMethod.GetSimpleProfile(m_NickNm);
            if (profile == null)
            {
                await ModifyOriginalResponseAsync(m => m.Content = $"❌ 캐릭터명을 확인해주세요.");
                return;
            }
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
            if (!Method.TryParseItemLevel(profile.아이템레벨, out var itemLv))
            {
                await FollowupAsync($"❌ 아이템레벨을 파싱하지 못했습니다: `{profile.아이템레벨}`", ephemeral: true);
                return;
            }

            if (!Method.TryParseStdLevel(mStdLv, out var stdLv))
            {
                await FollowupAsync($"❌ 관리자에게 문의해주세요.", ephemeral: true);
                return;
            }

            // 기준 미달
            if (itemLv < stdLv)
            {
                string failDesc = $"캐릭명 : {m_NickNm}\n" +
                                  $"아이템 : {profile.아이템레벨}\n" +
                                  $"해당 캐릭터는 인증 기준레벨 미달 입니다.\n" +
                                  $"거래소인증은 {mStdLv} 이상의 캐릭으로만 가능합니다.";

                var s_embed = new EmbedBuilder()
                    .WithAuthor("🚨 요청실패")
                    .WithDescription(failDesc);

                await FollowupAsync(embed: s_embed.Build(), ephemeral: true);
                return;
            }

            DateTime dt = DateTime.UtcNow.AddHours(9);
            string m_CertDate = dt.ToString("yyyy-MM-dd"); // 2026-01-06
            string m_CertTime = dt.ToString("HH:mm");      // 01:23

            var dbRow = await SupabaseClient.GetCertInfoByUserIdAsync(user.Id.ToString());

            if (dbRow != null)
            {
                var (ok, body) = await SupabaseClient.UpdateCertOnlyAsync(
                    userId: user.Id.ToString(),
                    stoveId: dbRow.StoveId,
                    characters: profile.보유캐릭_목록,
                    certDate: m_CertDate,
                    certTime: m_CertTime
                    );

                if (!ok)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ 관리자에게 문의해주세요.");
                    return;
                }

                // StoveId 비교
                if (!string.Equals(dbRow.StoveId, m_StoveId, StringComparison.Ordinal))
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 저장된 정보와 신청자의 스토브 계정이 다릅니다.");
                    return;
                }
            }
            else
            {
                string joindate = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd") ?? "";
                string jointime = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm") ?? "";

                var (ok, body) = await SupabaseClient.UpsertCertInfoAsync(
                    userId: user.Id.ToString(),
                    stoveId: m_StoveId,
                    userNm: user.Username,
                    characters: profile.보유캐릭_목록,
                    joinDate: joindate,
                    joinTime: jointime,
                    certDate: m_CertDate,
                    certTime: m_CertTime
                    );

                if (!ok)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ 관리자에게 문의해주세요.");
                    return;
                }
            }

            string m_Context = "";
            m_Context += "갱신대상 : " + user.Mention + "``(" + user.Id.ToString() + ")``" + Environment.NewLine + Environment.NewLine;
            m_Context += "갱신캐릭 : ``'" + m_NickNm + "'``" + Environment.NewLine + Environment.NewLine;
            m_Context += "위 정보로 거래소 인증이 완료되었습니다.";

            var ComPeleteEmbed = new EmbedBuilder()
                .WithAuthor("✅ 갱신완료")
                .WithDescription(m_Context)
                .WithColor(Color.Green)
                .WithThumbnailUrl(user.GetAvatarUrl(ImageFormat.Auto))
                .WithFooter("Develop by. 갱프　　　　　　　　　갱신일시 : " + m_CertDate + " " + m_CertTime);

            await ModifyOriginalResponseAsync(m => m.Content = "정상적으로 처리되었습니다.");
            await ModifyOriginalResponseAsync(m => m.Embed = ComPeleteEmbed.Build());
        }

        // ✅ 종료 버튼
        [ComponentInteraction("ChDispose", ignoreGroupNames: true)]
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
    }

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public sealed class AdminModule : InteractionModuleBase<SocketInteractionContext>
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
                .WithFooter($"Develop by. 갱프　　　　　조치일시 : {DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd HH:mm:ss")}");

            var logCh = Context.Guild.GetTextChannel(BanLogChannelId);
            if (logCh != null)
                await logCh.SendMessageAsync(embed: banEmbed.Build());

        }

        [SlashCommand("추방해제", "대상ID 입력, 사유입력")]
        public async Task UnBanUserAsync(
            [Summary(description: "해제대상ID")] string? 해제대상 = null,
            [Summary(description: "해제사유")] string? 해제사유 = null)
        {
            if (Context.User is not SocketGuildUser gu)
            {
                return;
            }

            ulong targetId;
            string displayName;
            string mentionText;
            string? iconUrl = null;

            if (!ulong.TryParse(해제대상!.Trim(), out targetId))
            {
                await RespondAsync("유저ID는 숫자만 입력해주세요.", ephemeral: true);
                return;
            }

            var fetched = await Context.Client.GetUserAsync(targetId);
            if (fetched != null)
            {
                displayName = fetched.Username;
                mentionText = fetched.Mention;
                iconUrl = fetched.GetAvatarUrl(ImageFormat.Auto) ?? fetched.GetDefaultAvatarUrl();
            }
            else
            {
                displayName = targetId.ToString();
                mentionText = targetId.ToString();
            }

            // ✅ 핵심: ID로 밴 (서버에 없어도 100% 가능)
            await Context.Guild.RemoveBanAsync(targetId, options: new RequestOptions { AuditLogReason = 해제사유 });

            // ✅ 커맨드 응답
            await RespondAsync($"{displayName} 차단해제완료\n해제사유 : {해제사유}");

            string description = string.Empty;
            description += $"해제대상: {mentionText} `{targetId}`\n";
            description += $"해제사유: {해제사유}\n";
            description += $"해제일시: {DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd HH:mm:ss")}";

            var banEmbed = new EmbedBuilder()
                .WithColor((Discord.Color)System.Drawing.Color.Green)
                .WithTitle("**추방해제**")
                .WithDescription(description)
                .WithFooter($"Develop by. 갱프");
        }

        //[SlashCommand("역할일괄부여", "메인역할인 '루페온' 역할을 모든 유저에게 일괄로 부여합니다. (미인증제외, 관리자전용)")]
        public async Task SetMainRoleAddByAllUser()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            const ulong excludeRole = 902213602889568316;
            const ulong targetRole = 1457383863943954512;

            // ✅ KST 타임존 (윈도우: Korea Standard Time / 리눅스: Asia/Seoul)
            TimeZoneInfo kst;
            try { kst = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
            catch { kst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); }

            string NowKst() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kst).ToString("yyyy-MM-dd HH:mm:ss");

            List<SocketGuildUser> targetUsers = gu.Guild.Users
                .Where(u => !u.IsBot)
                .Where(u =>
                {
                    var roleIds = u.Roles.Select(r => r.Id).ToHashSet();
                    return !roleIds.Contains(excludeRole)
                        && !roleIds.Contains(targetRole);
                })
                .ToList();
            int total = targetUsers.Count;
            int processed = 0, added = 0, skipped = 0, failed = 0;

            // ✅ 실패 유저 모아두기 (멘션용)
            List<SocketGuildUser> failedUsers = new List<SocketGuildUser>();

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
                    .WithFooter($"시간: {NowKst()}");
            }

            var msg = await Context.Channel.SendMessageAsync(embed: BuildProgressEmbed("시작합니다...", Color.Orange).Build());

            foreach (var user in targetUsers)
            {
                processed++;

                try
                {
                    await user.AddRoleAsync(targetRole);
                    added++;

                    // Rate limit 보호
                    await Task.Delay(500);
                }
                catch
                {
                    failed++;
                    failedUsers.Add(user);
                }

                // 5명마다 진행 로그 갱신
                if (processed % 5 == 0 || processed == total)
                {
                    await msg.ModifyAsync(m =>
                        m.Embed = BuildProgressEmbed($"처리 중... `{processed}/{total}`", Discord.Color.Orange).Build()
                    );
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
                .WithFooter($"완료: {NowKst()}")
                .Build();

            await msg.ModifyAsync(m => m.Embed = done);

            // ✅ 실패 유저 멘션을 특정 채널로 알림
            const ulong notifyChannelId = 1292061651092246580UL;
            var ch = Context.Guild.GetTextChannel(notifyChannelId);

            if (ch != null && failedUsers.Count > 0)
            {
                // 멘션 스팸 방지: 너무 많으면 여러 메시지로 쪼개기 (예: 20명 단위)
                const int chunkSize = 20;

                for (int i = 0; i < failedUsers.Count; i += chunkSize)
                {
                    var chunk = failedUsers.Skip(i).Take(chunkSize);
                    var mentions = string.Join(" ", chunk.Select(u => u.Mention));

                    await ch.SendMessageAsync(
                        $"⚠️ **루페온 역할 부여 실패 유저 목록** (KST {NowKst()})\n{mentions}"
                    );
                }
            }

            // 슬래시커맨드 응답(관리자에게만)
            await FollowupAsync(
                $"완료. 대상:{total}, 성공:{added}, 실패:{failed} (KST {NowKst()})",
                ephemeral: true
            );
        }

        // ✅ 채널 권한에서 제거할 역할들(예: 역할 ID로 관리)
        // HashTable 느낌으로 쓰고 싶으면 Dictionary<string, ulong> 로도 가능
        private static readonly HashSet<ulong> RolesToRemove = new()
        {
            557631665728389153,
            557631664986259472,
            557631664470360099,
            639121866992123974,
            1065618299116863508,
            1387703156833783888,
            557631664365371407,
            557631663102754817,
            855711579290075176,
            557631661525696522,
            557631661966229524,
            557631662284865537,
            571807949513687041,
            789750930811256882,
            1188409166793019513,
            601680900379377664,
            737845189640716319,
            1124738844135264266,
            557631659109908492,
            557628187870232577,
            557631620467916810,
            725431052495224854,
            789750805896495104,
            921699659498524722,
            995318441915461732,
            1317479085328306196,
            1449635262400299051,
            557631663576842241,
            601680858876739634
        };

        // ✅ 역할명(권장: 역할ID로 박아두는게 더 안전)
        private const ulong TargetRoleId = 1457383863943954512;       //루페온

        //[SlashCommand("채널정리", "입력한 채널 직업역할제거, 루페온역할 부여")]
        public async Task SetChannelRoleAsync([Summary("카테고리id", "정리할 카테고리 ID")] string categoryId)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            if (!ulong.TryParse(categoryId, out ulong catId))
            {
                await FollowupAsync("❌ 카테고리 ID는 숫자만 입력해주세요.", ephemeral: true);
                return;
            }

            var category = Context.Guild.GetCategoryChannel(catId);
            if (category == null)
            {
                await FollowupAsync($"❌ 카테고리 ID `{categoryId}` 를 찾을 수 없습니다.", ephemeral: true);
                return;
            }

            var guild = Context.Guild;
            var channels = category.Channels;

            int totalRemoved = 0;
            int okChannels = 0;

            // ⭐ 실패 채널 기록용
            List<string> failedChannels = new();

            foreach (var ch in channels)
            {
                try
                {
                    foreach (var ow in ch.PermissionOverwrites.Where(x => x.TargetType == PermissionTarget.Role))
                    {
                        if (!RolesToRemove.Contains(ow.TargetId))
                            continue;

                        if (ow.TargetId == TargetRoleId)
                            continue;

                        var role = guild.GetRole(ow.TargetId);
                        if (role == null)
                            continue;

                        await ch.RemovePermissionOverwriteAsync(role);
                        totalRemoved++;
                    }

                    okChannels++;
                }
                catch (Exception)
                {
                    // ❗ 실패한 채널 멘션 기록
                    failedChannels.Add($"<#{ch.Id}> ({ch.Name})");
                    // 계속 진행
                }
            }

            // 결과 메시지 구성
            string result =
                $"✅ 카테고리 `{category.Name}` 정리 완료\n" +
                $"- 대상 채널: {channels.Count}개\n" +
                $"- 처리 성공: {okChannels}개\n" +
                $"- 제거된 overwrite: {totalRemoved}개";

            if (failedChannels.Count > 0)
            {
                result +=
                    "\n\n⚠️ 실패한 채널:\n" +
                    string.Join(", ", failedChannels);
            }

            await FollowupAsync(result, ephemeral: true);
        }
    }

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public sealed class RoleSlashModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("역할신청", "직업역할 선택 슬롯 표시")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task RoleSelectAsync()
        {
            if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            ITextChannel textChannel = admin.Guild.GetTextChannel(1000806935634919454);

            var embed = new EmbedBuilder()
                .WithTitle("🎮 직업 역할 선택")
                .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
                                 $"\n\n역할이 받아졌는지 확인 하는 방법" +
                                 $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
                .WithColor(Color.Green)
                .WithFooter("Develop by. 갱프");

            await Context.Channel.SendMessageAsync(embed: embed.Build(), components: RoleMenuUi.BuildMenus());
            await RespondAsync("표시완료", ephemeral: true);
        }

        //[SlashCommand("역할신청", "직업역할 선택 버튼 표시")]
        //[DefaultMemberPermissions(GuildPermission.Administrator)]
        //public async Task RoleButtonAsync()
        //{
        //    if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
        //    {
        //        await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
        //        return;
        //    }

        //    ITextChannel textChannel = admin.Guild.GetTextChannel(1000806935634919454);

        //    #region 직업이모지
        //    Emote? GetEmote(string name) => EmoteCache.Emotes.TryGetValue(name, out var e) ? e : null;

        //    var m_워로드 = GetEmote("emblem_warlord");
        //    var m_버서커 = GetEmote("emblem_berserker");
        //    var m_디스트로이어 = GetEmote("emblem_destroyer");
        //    var m_홀리나이트 = GetEmote("emblem_holyknight");
        //    var m_슬레이어 = GetEmote("emblem_slayer");
        //    var m_발키리 = GetEmote("emblem_holyknight_female");
        //    var m_배틀마스터 = GetEmote("emblem_battlemaster");
        //    var m_인파이터 = GetEmote("emblem_infighter");
        //    var m_기공사 = GetEmote("emblem_soulmaster");
        //    var m_창술사 = GetEmote("emblem_lancemaster");
        //    var m_스트라이커 = GetEmote("emblem_striker");
        //    var m_브레이커 = GetEmote("emblem_infighter_male");
        //    var m_데빌헌터 = GetEmote("emblem_devilhunter");
        //    var m_블래스터 = GetEmote("emblem_blaster");
        //    var m_호크아이 = GetEmote("emblem_hawkeye");
        //    var m_건슬링어 = GetEmote("emblem_gunslinger");
        //    var m_스카우터 = GetEmote("emblem_scouter");
        //    var m_아르카나 = GetEmote("emblem_arcana");
        //    var m_서머너 = GetEmote("emblem_summoner");
        //    var m_바드 = GetEmote("emblem_bard");
        //    var m_소서리스 = GetEmote("emblem_sorceress");
        //    var m_블레이드 = GetEmote("emblem_blade");
        //    var m_데모닉 = GetEmote("emblem_demonic");
        //    var m_리퍼 = GetEmote("emblem_reaper");
        //    var m_소울이터 = GetEmote("emblem_souleater");
        //    var m_도화가 = GetEmote("emblem_artist");
        //    var m_기상술사 = GetEmote("emblem_weather_artist");
        //    var m_환수사 = GetEmote("emblem_alchemist");
        //    var m_가디언나이트 = GetEmote("emblem_dragon_knight");
        //    #endregion 직업이모지

        //    SocketRole? GetRoles(string name) => RoleCache.SocketRoles.TryGetValue(name, out var e) ? e : null;

        //    #region 슈샤이어 | 로헨델
        //    var Embed1 = new EmbedBuilder()
        //        .WithTitle("🎮 직업 역할 선택 • 슈샤이어 | 로헨델")
        //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
        //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
        //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
        //        .WithColor(Color.Green)
        //        .WithFooter("Develop by. 갱프")
        //        .Build();

        //    var Component1 = new ComponentBuilder()
        //        .WithButton(label: "버서커", customId: $"role:{GetRoles("버서커").Id}", style: ButtonStyle.Secondary, emote: m_버서커)
        //        .WithButton(label: "워로드", customId: $"role:{GetRoles("워로드").Id}", style: ButtonStyle.Secondary, emote: m_워로드)
        //        .WithButton(label: "디스트로이어", customId: $"role:{GetRoles("디스트로이어").Id}", style: ButtonStyle.Secondary, emote: m_디스트로이어)
        //        .WithButton(label: "홀리나이트", customId: $"role:{GetRoles("홀리나이트").Id}", style: ButtonStyle.Secondary, emote: m_홀리나이트)
        //        .WithButton(label: "슬레이어", customId: $"role:{GetRoles("슬레이어").Id}", style: ButtonStyle.Secondary, emote: m_슬레이어)
        //        .WithButton(label: "발키리", customId: $"role:{GetRoles("발키리").Id}", style: ButtonStyle.Secondary, emote: m_발키리)
        //        .WithButton(label: "아르카나", customId: $"role:{GetRoles("아르카나").Id}", style: ButtonStyle.Secondary, emote: m_아르카나)
        //        .WithButton(label: "서머너", customId: $"role:{GetRoles("서머너").Id}", style: ButtonStyle.Secondary, emote: m_서머너)
        //        .WithButton(label: "바드", customId: $"role:{GetRoles("바드").Id}", style: ButtonStyle.Secondary, emote: m_바드)
        //        .WithButton(label: "소서리스", customId: $"role:{GetRoles("소서리스").Id}", style: ButtonStyle.Secondary, emote: m_소서리스)
        //        .Build();

        //    await Context.Channel.SendMessageAsync(embed: Embed1, components: Component1);
        //    #endregion 슈샤이어 | 로헨델

        //    #region 애니츠 | 페이튼
        //    var Embed2 = new EmbedBuilder()
        //        .WithTitle("🎮 직업 역할 선택 • 애니츠 | 페이튼")
        //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
        //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
        //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
        //        .WithColor(Color.Green)
        //        .WithFooter("Develop by. 갱프")
        //        .Build();

        //    var Component2 = new ComponentBuilder()
        //        .WithButton(label: "배틀마스터", customId: $"role:{GetRoles("배틀마스터").Id}", style: ButtonStyle.Secondary, emote: m_배틀마스터)
        //        .WithButton(label: "인파이터", customId: $"role:{GetRoles("인파이터").Id}", style: ButtonStyle.Secondary, emote: m_인파이터)
        //        .WithButton(label: "기공사", customId: $"role:{GetRoles("기공사").Id}", style: ButtonStyle.Secondary, emote: m_기공사)
        //        .WithButton(label: "창술사", customId: $"role:{GetRoles("창술사").Id}", style: ButtonStyle.Secondary, emote: m_창술사)
        //        .WithButton(label: "스트라이커", customId: $"role:{GetRoles("스트라이커").Id}", style: ButtonStyle.Secondary, emote: m_스트라이커)
        //        .WithButton(label: "브레이커", customId: $"role:{GetRoles("브레이커").Id}", style: ButtonStyle.Secondary, emote: m_브레이커)
        //        .WithButton(label: "블레이드", customId: $"role:{GetRoles("블레이드").Id}", style: ButtonStyle.Secondary, emote: m_블레이드)
        //        .WithButton(label: "데모닉", customId: $"role:{GetRoles("데모닉").Id}", style: ButtonStyle.Secondary, emote: m_데모닉)
        //        .WithButton(label: "리퍼", customId: $"role:{GetRoles("리퍼").Id}", style: ButtonStyle.Secondary, emote: m_리퍼)
        //        .WithButton(label: "소울이터", customId: $"role:{GetRoles("소울이터").Id}", style: ButtonStyle.Secondary, emote: m_소울이터)
        //        .Build();

        //    await Context.Channel.SendMessageAsync(embed: Embed2, components: Component2);
        //    #endregion 애니츠 | 페이튼

        //    #region 아르데타인 | 스페셜리스트
        //    var Embed3 = new EmbedBuilder()
        //        .WithTitle("🎮 직업 역할 선택 • 아르데타인 | 스페셜리스트")
        //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
        //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
        //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
        //        .WithColor(Color.Green)
        //        .WithFooter("Develop by. 갱프")
        //        .Build();

        //    var Component3 = new ComponentBuilder()
        //        .WithButton(label: "호크아이", customId: $"role:{GetRoles("호크아이").Id}", style: ButtonStyle.Secondary, emote: m_호크아이)
        //        .WithButton(label: "데빌헌터", customId: $"role:{GetRoles("데빌헌터").Id}", style: ButtonStyle.Secondary, emote: m_데빌헌터)
        //        .WithButton(label: "블래스터", customId: $"role:{GetRoles("블래스터").Id}", style: ButtonStyle.Secondary, emote: m_블래스터)
        //        .WithButton(label: "스카우터", customId: $"role:{GetRoles("스카우터").Id}", style: ButtonStyle.Secondary, emote: m_스카우터)
        //        .WithButton(label: "건슬링어", customId: $"role:{GetRoles("건슬링어").Id}", style: ButtonStyle.Secondary, emote: m_건슬링어)
        //        .WithButton(label: "도화가", customId: $"role:{GetRoles("도화가").Id}", style: ButtonStyle.Secondary, emote: m_도화가)
        //        .WithButton(label: "기상술사", customId: $"role:{GetRoles("기상술사").Id}", style: ButtonStyle.Secondary, emote: m_기상술사)
        //        .WithButton(label: "환수사", customId: $"role:{GetRoles("환수사").Id}", style: ButtonStyle.Secondary, emote: m_환수사)
        //        .Build();

        //    await Context.Channel.SendMessageAsync(embed: Embed3, components: Component3);
        //    #endregion 아르데타인 | 스페셜리스트

        //    #region 가디언나이트
        //    var GK = new EmbedBuilder()
        //        .WithTitle("🎮 직업 역할 선택 • 가디언나이트")
        //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
        //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
        //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
        //        .WithColor(Color.Green)
        //        .WithFooter("Develop by. 갱프")
        //        .Build();

        //    var Cp_GK = new ComponentBuilder()
        //        .WithButton(label: "가디언나이트", customId: $"role:{GetRoles("가디언나이트").Id}", style: ButtonStyle.Secondary, emote: m_가디언나이트)
        //        .Build();

        //    await Context.Channel.SendMessageAsync(embed: GK, components: Cp_GK);
        //    #endregion 가디언나이트

        //    await RespondAsync("표시완료", ephemeral: true);
        //}

        [SlashCommand("역할확인", "본인이 가지고 있는 역할들을 확인 할 수 있는 버튼표시")]
        public async Task RoleCheck()
        {
            if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            string mValue = string.Empty;
            string Emote = "<:pdiamond:907957436483248159>";

            mValue = Emote + "아래의 역할확인 버튼을 클릭" + Environment.NewLine
                   + Emote + "본인이 가지고 있는 역할들을 확인 할 수 있습니다.";

            var Embed = new EmbedBuilder()
                .WithAuthor("[역할확인]")
                .WithColor(Discord.Color.LightOrange)
                .WithDescription(mValue)
                .WithFooter("Develop by. 갱프")
                .Build(); ;

            var component = new ComponentBuilder()
                .WithButton(label: "역할확인", customId: "ChkRoles", style: ButtonStyle.Success)
                .Build();

            await Context.Channel.SendMessageAsync(embed: Embed, components: component);
            await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
        }

        [ComponentInteraction("ChkRoles")] // 버튼 customId 예시
        public async Task ShowMyRolesAsync()
        {
            if (Context.User is not SocketGuildUser gu)
            {
                await RespondAsync("길드 유저만 가능합니다.", ephemeral: true);
                return;
            }

            // ✅ 예전 코드와 동일한 리스트들
            var 슈샤이어 = new List<string>();
            var 로헨델 = new List<string>();
            var 애니츠 = new List<string>();
            var 아르데타인 = new List<string>();
            var 페이튼 = new List<string>();
            var 스페셜리스트 = new List<string>();
            var 가디언나이트 = new List<string>();

            var 거래역할 = new List<string>();
            var 관리역할 = new List<string>();
            var 그외역할 = new List<string>();

            foreach (var role in gu.Roles)
            {
                if (role.IsEveryone) continue;
                if (IgnoreRoleIds.Contains(role.Id)) continue;

                // ✅ 직업 분류
                if (Job_Shushaire.Contains(role.Id))
                    슈샤이어.Add(role.Mention);
                else if (Job_Rohendel.Contains(role.Id))
                    로헨델.Add(role.Mention);
                else if (Job_Anihc.Contains(role.Id))
                    애니츠.Add(role.Mention);
                else if (Job_Arthetine.Contains(role.Id))
                    아르데타인.Add(role.Mention);
                else if (Job_Faten.Contains(role.Id))
                    페이튼.Add(role.Mention);
                else if (Job_Specialist.Contains(role.Id))
                    스페셜리스트.Add(role.Mention);
                else if (Job_DragonKnight.Contains(role.Id))
                    가디언나이트.Add(role.Mention);

                // ✅ 거래/관리/그외
                else if (TradeRoleIds.Contains(role.Id) || string.Equals(role.Name, "거래소", StringComparison.OrdinalIgnoreCase))
                    거래역할.Add(role.Mention);
                else if (AdminRoleIds.Contains(role.Id))
                    관리역할.Add(role.Mention);
                else
                    그외역할.Add(role.Mention);
            }

            // ✅ 예전 코드와 “출력 규칙 동일”하게 문자열 만들기
            string mJob = BuildLikeLegacy(슈샤이어)
                        + BuildLikeLegacy(로헨델)
                        + BuildLikeLegacy(애니츠)
                        + BuildLikeLegacy(아르데타인)
                        + BuildLikeLegacy(페이튼)
                        + BuildLikeLegacy(스페셜리스트)
                        + BuildLikeLegacy(가디언나이트);

            string mRole = BuildLikeLegacy(거래역할);
            string mEtc = BuildLikeLegacy(그외역할);
            string mAdmin = BuildLikeLegacy(관리역할);

            string mValue = "";

            if (!string.IsNullOrEmpty(mJob))
                mValue = "직업역할" + Environment.NewLine + TrimLegacy(mJob);

            if (!string.IsNullOrEmpty(mRole))
                mValue += Environment.NewLine + Environment.NewLine + "거래역할" + Environment.NewLine + TrimLegacy(mRole);

            if (!string.IsNullOrEmpty(mEtc))
                mValue += Environment.NewLine + Environment.NewLine + "그외역할" + Environment.NewLine + TrimLegacy(mEtc);

            if (!string.IsNullOrEmpty(mAdmin))
                mValue += Environment.NewLine + Environment.NewLine + "관리역할" + Environment.NewLine + TrimLegacy(mAdmin);

            var embed = new EmbedBuilder()
                .WithAuthor("보유 중인 역할")
                .WithDescription(mValue)
                .WithColor(Color.Purple)
                .WithFooter($"Develop by. 갱프　　　　　확인일시: {DateTime.Now:yyyy-MM-dd HH:mm}")
                .Build();

            await RespondAsync(embed: embed, ephemeral: true);
        }

        [ComponentInteraction("role:*")]
        public async Task HandleRoleButton(string roleIdText)
        {
            if (Context.User is not SocketGuildUser user)
            {
                await RespondAsync("서버에서만 사용 가능합니다.", ephemeral: true);
                return;
            }

            if (!ulong.TryParse(roleIdText, out var roleId))
            {
                await RespondAsync("역할 정보가 올바르지 않습니다.", ephemeral: true);
                return;
            }

            var role = user.Guild.GetRole(roleId);
            if (role == null)
            {
                await RespondAsync("해당 역할을 찾을 수 없습니다.", ephemeral: true);
                return;
            }

            var hasRole = user.Roles.Any(r => r.Id == roleId);

            if (hasRole)
            {
                // 보호 역할 자체는 제거 못하게 하고 싶으면 이것도 추가 가능
                if (ExcludedRoleIds.Contains(roleId))
                {
                    await RespondAsync("❌ 이 역할은 해제할 수 없습니다.", ephemeral: true);
                    return;
                }

                int remain = CountRemovableRoles(user, roleId);
                if (remain == 0)
                {
                    await RespondAsync("❌ 최소 1개의 직업 역할은 유지해야 합니다.", ephemeral: true);
                    return;
                }

                await user.RemoveRoleAsync(role);
                await RespondAsync($"❌ `{role.Name}` 역할이 제거되었습니다.", ephemeral: true);
                return;
            }

            await user.AddRoleAsync(role);
            await RespondAsync($"✅ `{role.Name}` 역할이 부여되었습니다.", ephemeral: true);
        }

        [ComponentInteraction("SelectRow:*")]
        public async Task SelectRowAsync(string values)
        {
            await DeferAsync(ephemeral: true); // ✅ 필수

            if (Context.User is not SocketGuildUser user)
            {
                await FollowupAsync("❌ 길드 유저만 사용 가능합니다.", ephemeral: true);
                return;
            }

            // ✅ 선택값 꺼내기 (SelectMenu는 SocketMessageComponent로 들어옴)
            if (Context.Interaction is not SocketMessageComponent smc)
            {
                await FollowupAsync("❌ 컴포넌트 상호작용이 아닙니다.", ephemeral: true);
                return;
            }

            var picked = smc.Data.Values.FirstOrDefault(); // MaxValues(1)이면 1개만 들어옴
            if (string.IsNullOrWhiteSpace(picked))
            {
                await FollowupAsync("❌ 선택값이 없습니다.", ephemeral: true);
                return;
            }

            // ✅ 역할 이름 = 직업명 으로 바로 찾기
            var role = Context.Guild.Roles.FirstOrDefault(r => r.Name.Equals(picked, StringComparison.OrdinalIgnoreCase));

            // ✅ 있으면 제거 / 없으면 부여
            if (user.Roles.Any(r => r.Id == role.Id))
            {
                await user.RemoveRoleAsync(role);
                await FollowupAsync($"❌ `{role.Name}` 역할이 제거되었습니다.", ephemeral: true);
            }
            else
            {
                await user.AddRoleAsync(role);
                await FollowupAsync($"✅ `{role.Name}` 역할이 부여되었습니다.", ephemeral: true);
            }
        }

        // ✅ 삭제 제한 계산에서 제외할 역할들 (예: 인증/필수/운영진 등)
        // 여기에 네가 지정한 역할 ID를 넣어.
        private static readonly HashSet<ulong> ExcludedRoleIds = new()
        {
            653491548482174996,  // 메인관리자
            557635038607573002,  // 관리자
            667614334998020096,  // 봇
            688802446943715404,  // 작대기1
            688803133153214536,  // 작대기2
            1264901726251647086, // 거래소
            58000335490252801,   // 거래인증
            595607967030837285,  // 판매인증
            602169127926366239,  // 작대기3
            600948355501260800,  // 니트로
            1190024494144831589, // 공란
            893431274964922380,  // 하트
            900235242219118592,  // 별표
            999954837301116988,  // 치타
            1407670667670716497, // 노랑
            1370337289213050930, // OrangeYellow
            900240165598031932,  // Emerald
            1370336719941144676, // SkyBlue
            900236308356669440,  // Purple
            914463919567945759,  // RoseGold
            1370336310119890984, // Silver
            1299736324890431518, // 임시역할
            1457383863943954512, // 루페온
        };

        private static int CountRemovableRoles(SocketGuildUser user, ulong roleIdToRemove)
        {
            // @everyone(=guild.Id)는 항상 있으니 제외
            // ExcludedRoleIds는 제외
            // 지금 삭제하려는 roleIdToRemove도 제외하고 나머지 역할이 몇 개인지 센다
            return user.Roles.Count(r =>
                r.Id != user.Guild.Id &&              // @everyone 제외
                !ExcludedRoleIds.Contains(r.Id) &&    // 보호 역할 제외
                r.Id != roleIdToRemove                // 지금 삭제하려는 역할 제외
            );
        }

        private static readonly HashSet<ulong> IgnoreRoleIds = new()
        {
            //513799663086862336,
            688802446943715404,
            688803133153214536,
            602169127926366239
        };

        // ✅ 직업/거래/관리 ID를 그대로 switch 대신 HashSet으로 분류
        private static readonly HashSet<ulong> Job_Shushaire = new()
        {
            557631665728389153, // 버서커
            557631664986259472, // 디트
            557631664470360099, // 워로드
            639121866992123974, // 홀리나이트
            1065618299116863508,// 슬레이어
            1387703156833783888,// 발키리
        };

        private static readonly HashSet<ulong> Job_Rohendel = new()
        {
            557631664365371407, // 바드
            557631663102754817, // 서머너
            557631663576842241, // 아르카나
            855711579290075176, // 소서리스
        };

        private static readonly HashSet<ulong> Job_Anihc = new()
        {
            557631661525696522, // 배마
            557631661966229524, // 인파
            557631662284865537, // 기공
            571807949513687041, // 창술
            789750930811256882, // 스트
            1188409166793019513,// 브커
        };

        private static readonly HashSet<ulong> Job_Specialist = new()
        {
            921699659498524722, // 도화가
            995318441915461732, // 기상술사
            1317479085328306196,// 환술사
        };

        private static readonly HashSet<ulong> Job_Faten = new()
        {
            601680900379377664, // 블레이드
            601680858876739634, // 데모닉
            737845189640716319, // 리퍼
            1124738844135264266,// 소울이터
        };

        private static readonly HashSet<ulong> Job_Arthetine = new()
        {
            789750805896495104, // 데빌헌터
            557628187870232577, // 블래스터
            557631620467916810, // 호크
            725431052495224854, // 스카
            557631659109908492, // 건슬
        };

        private static readonly HashSet<ulong> Job_DragonKnight = new()
        {
            1449635262400299051, // 가디언나이트
        };

        private static readonly HashSet<ulong> TradeRoleIds = new()
        {
            1056749315818782761,
            580003354902528011,
            595607967030837285
        };

        private static readonly HashSet<ulong> AdminRoleIds = new()
        {
            653491548482174996,
            557635038607573002,
            900235242219118592,
            667614334998020096,
            999954837301116988
        };

        private static string BuildLikeLegacy(List<string> list)
        {
            if (list == null || list.Count == 0) return "";

            var sb = new StringBuilder();
            for (int idx = 1; idx <= list.Count; idx++)
            {
                if (idx == list.Count)
                    sb.Append(list[idx - 1]).Append(", ").Append(Environment.NewLine);
                else
                    sb.Append(list[idx - 1]).Append(", ");
            }
            return sb.ToString();
        }

        // ✅ 예전: Substring(0, Length-2) 와 동일 효과
        // 마지막에 붙은 ", \n" 또는 ", " 를 제거
        private static string TrimLegacy(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // 예전코드가 Length-2라서 \r\n 환경에 따라 애매했음.
            // 여기선 안전하게 끝의 ", " 와 줄바꿈을 제거
            return s.TrimEnd('\r', '\n', ' ', ',');
        }

        /// <summary>
        /// 메뉴 생성 코드를 공용으로 빼둔 곳 (핵심)
        /// </summary>
        public static class RoleMenuUi
        {
            public static MessageComponent BuildMenus()
            {
                Emote? GetEmote(string name) => EmoteCache.Emotes.TryGetValue(name, out var e) ? e : null;

                var m_워로드 = GetEmote("emblem_warlord");
                var m_버서커 = GetEmote("emblem_berserker");
                var m_디스트로이어 = GetEmote("emblem_destroyer");
                var m_홀리나이트 = GetEmote("emblem_holyknight");
                var m_슬레이어 = GetEmote("emblem_slayer");
                var m_발키리 = GetEmote("emblem_holyknight_female");
                var m_배틀마스터 = GetEmote("emblem_battlemaster");
                var m_인파이터 = GetEmote("emblem_infighter");
                var m_기공사 = GetEmote("emblem_soulmaster");
                var m_창술사 = GetEmote("emblem_lancemaster");
                var m_스트라이커 = GetEmote("emblem_striker");
                var m_브레이커 = GetEmote("emblem_infighter_male");
                var m_데빌헌터 = GetEmote("emblem_devilhunter");
                var m_블래스터 = GetEmote("emblem_blaster");
                var m_호크아이 = GetEmote("emblem_hawkeye");
                var m_건슬링어 = GetEmote("emblem_gunslinger");
                var m_스카우터 = GetEmote("emblem_scouter");
                var m_아르카나 = GetEmote("emblem_arcana");
                var m_서머너 = GetEmote("emblem_summoner");
                var m_바드 = GetEmote("emblem_bard");
                var m_소서리스 = GetEmote("emblem_sorceress");
                var m_블레이드 = GetEmote("emblem_blade");
                var m_데모닉 = GetEmote("emblem_demonic");
                var m_리퍼 = GetEmote("emblem_reaper");
                var m_소울이터 = GetEmote("emblem_souleater");
                var m_도화가 = GetEmote("emblem_artist");
                var m_기상술사 = GetEmote("emblem_weather_artist");
                var m_환수사 = GetEmote("emblem_alchemist");
                var m_가디언나이트 = GetEmote("emblem_dragon_knight");

                var selectMenu = new SelectMenuBuilder()
                    .AddOption(emote: m_버서커, label: "버서커", value: "버서커")
                    .AddOption(emote: m_디스트로이어, label: "디스트로이어", value: "디스트로이어")
                    .AddOption(emote: m_워로드, label: "워로드", value: "워로드")
                    .AddOption(emote: m_홀리나이트, label: "홀리나이트", value: "홀리나이트")
                    .AddOption(emote: m_슬레이어, label: "슬레이어", value: "슬레이어")
                    .AddOption(emote: m_발키리, label: "발키리", value: "발키리")
                    .AddOption(emote: m_아르카나, label: "아르카나", value: "아르카나")
                    .AddOption(emote: m_서머너, label: "서머너", value: "서머너")
                    .AddOption(emote: m_바드, label: "바드", value: "바드")
                    .AddOption(emote: m_소서리스, label: "소서리스", value: "소서리스")
                    .AddOption(emote: m_배틀마스터, label: "배틀마스터", value: "배틀마스터")
                    .AddOption(emote: m_인파이터, label: "인파이터", value: "인파이터")
                    .AddOption(emote: m_기공사, label: "기공사", value: "기공사")
                    .AddOption(emote: m_창술사, label: "창술사", value: "창술사")
                    .AddOption(emote: m_스트라이커, label: "스트라이커", value: "스트라이커")
                    .AddOption(emote: m_브레이커, label: "브레이커", value: "브레이커")
                    .WithCustomId("SelectRow:1")
                    .WithMinValues(1)
                    .WithMaxValues(1)
                    .WithPlaceholder("원하는 직업을 선택하여 역할을 받을 수 있습니다.");

                var selectMenu2 = new SelectMenuBuilder()
                    .AddOption(emote: m_블레이드, label: "블레이드", value: "블레이드")
                    .AddOption(emote: m_데모닉, label: "데모닉", value: "데모닉")
                    .AddOption(emote: m_리퍼, label: "리퍼", value: "리퍼")
                    .AddOption(emote: m_소울이터, label: "소울이터", value: "소울이터")
                    .AddOption(emote: m_호크아이, label: "호크아이", value: "호크아이")
                    .AddOption(emote: m_데빌헌터, label: "데빌헌터", value: "데빌헌터")
                    .AddOption(emote: m_블래스터, label: "블래스터", value: "블래스터")
                    .AddOption(emote: m_스카우터, label: "스카우터", value: "스카우터")
                    .AddOption(emote: m_건슬링어, label: "건슬링어", value: "건슬링어")
                    .AddOption(emote: m_도화가, label: "도화가", value: "도화가")
                    .AddOption(emote: m_기상술사, label: "기상술사", value: "기상술사")
                    .AddOption(emote: m_환수사, label: "환수사", value: "환수사")
                    .AddOption(emote: m_가디언나이트, label: "가디언나이트", value: "가디언나이트")
                    .WithCustomId("SelectRow:2")
                    .WithMinValues(1)
                    .WithMaxValues(1)
                    .WithPlaceholder("원하는 직업을 선택하여 역할을 받을 수 있습니다.");

                return new ComponentBuilder()
                    .WithSelectMenu(selectMenu)
                    .WithSelectMenu(selectMenu2)
                    .Build();
            }
        }
    }

    public sealed class ProfileSerachModule : InteractionModuleBase<SocketInteractionContext>
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

