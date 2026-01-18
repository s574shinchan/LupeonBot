using Discord;
using Discord.API;
using Discord.Commands;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using LupeonBot.Client;
using LupeonBot.Module;
using LupeonBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Supabase.Gotrue;
using Supabase.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;


namespace DiscordBot
{
    public class Program
    {
        private static readonly DiscordSocketConfig config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMembers,
            AlwaysDownloadUsers = true,
            LogLevel = LogSeverity.Verbose
        };

        public static DiscordSocketClient client = new DiscordSocketClient(config);

        public static void Main() => new Program().MainAsync().GetAwaiter().GetResult();

        // ✅ 슬래시 커맨드 서비스
        InteractionService? publicSvc;
        InteractionService? lupeonSvc;
        private static IServiceProvider? _services;
        private StickyRefreshService? _sticky;
        private bool _stickyInitialized;

        public static string BotToken = string.Empty;
        public static string LostArkJwt = string.Empty; // ✅ 로아 Open API JWT

        private bool _registered;
        private static UserLeftQueue _userLeftQueue;

        public async Task MainAsync()
        {
            client.Log += Log;
            client.Ready += Ready;

            // 봇 토큰
            BotToken = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "LupeonBot_Token.txt")).Trim();

            // ✅ 로아 JWT 토큰 (추가)
            LostArkJwt = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "LostArkToken.txt")).Trim();

            SupabaseClient.Init(
                File.ReadAllText("SupabaseUrl.txt").Trim(),
                File.ReadAllText("SupabaseServiceRole.txt").Trim()
            );

            // ✅ InteractionService 생성
            publicSvc = new InteractionService(client.Rest);
            lupeonSvc = new InteractionService(client.Rest);

            _userLeftQueue = new UserLeftQueue(HandleUserLeftAsync, ex => Console.WriteLine(ex));
            _userLeftQueue.Start();

            // ✅ Interaction 처리 이벤트 연결
            client.InteractionCreated += HandleInteraction;
            client.MessageReceived += OnMessageReceivedAsync;

            client.UserJoined += UserJoined;
            client.UserLeft += UserLeft;

            _services = ConfigureServices();

            await client.LoginAsync(TokenType.Bot, BotToken);
            await client.StartAsync();
            await client.SetGameAsync(string.Empty, type: ActivityType.Playing);
            await Task.Delay(-1); // 프로그램 종료시까지 태스크 유지
        }

        private Task UserLeft(SocketGuild arg1, SocketUser arg2)
        {
            // ✅ 이벤트에서는 큐에만 넣고 즉시 반환 (절대 await로 무거운거 하지마)
            _userLeftQueue.Enqueue(new UserLeftJob(arg1.Id, arg2.Id));
            return Task.CompletedTask;
        }

        //private async Task UserJoined(SocketGuildUser arg)
        //{
        //    var guild = client.GetGuild(513799663086862336);

        //    var m_UnSignUp = guild.GetRole(902213602889568316);
        //    await arg.AddRoleAsync(m_UnSignUp);

        //    var voiceChannel = guild.GetVoiceChannel(1457106002553081958);
        //    await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
        //}
        private Task UserJoined(SocketGuildUser arg)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var guild = client.GetGuild(513799663086862336);

                    var m_UnSignUp = guild.GetRole(902213602889568316);
                    await arg.AddRoleAsync(m_UnSignUp);

                    var voiceChannel = guild.GetVoiceChannel(1457106002553081958);
                    await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UserJoined] {ex}");
                }
            });

            return Task.CompletedTask;
        }

        public async Task Ready()
        {
            if (_registered) return; // ✅ Ready 중복 방지
            _registered = true;

            await publicSvc.AddModuleAsync<ProfileSerachModule>(_services);
            await publicSvc.RegisterCommandsGloballyAsync();

            ulong guildId = 513799663086862336;
            var asm = Assembly.GetEntryAssembly()!;
            var moduleTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract)
                .Where(t => typeof(InteractionModuleBase<SocketInteractionContext>).IsAssignableFrom(t))
                .Where(t => t != typeof(ProfileSerachModule));

            foreach (var t in moduleTypes)
            {
                await lupeonSvc.AddModuleAsync(t, _services);
            }

            await lupeonSvc.RegisterCommandsToGuildAsync(guildId, deleteMissing: true);

            var maintSvc = new MaintenanceNoticeService(client);
            maintSvc.Start();

            var eventSvc = new EventNoticeService(client);
            eventSvc.Start();


            // ✅ 그 길드에 봇이 들어가 있을 때만
            if (client.GetGuild(guildId) == null)
                return;

            InitStickyIfNeeded();

            //ulong[] fullGuilds = { 513799663086862336, 222222222222222222 }; // 전부 보일 서버들            
            //foreach (var gid in fullGuilds)
            //    await fullSvc.RegisterCommandsToGuildAsync(gid);
            //await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services: null);
            //var modules = _interactions.Modules.Select(m => m.Name);

            //ulong guildId = 513799663086862336;
            //await _interactions.RegisterCommandsToGuildAsync(guildId, deleteMissing: true);

            foreach (var guild in client.Guilds)
            {
                switch (guild.Id)
                {
                    case 513799663086862336:
                        RoleCache.SocketRoles.Clear();

                        foreach (var role in guild.Roles)
                        {
                            RoleCache.SocketRoles[role.Name] = role;
                        }

                        var voiceChannel = guild.GetVoiceChannel(1457106002553081958);
                        await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
                        break;
                    case 624936203229069344:
                        EmoteCache.Emotes.Clear();

                        foreach (var emote in guild.Emotes)
                        {
                            EmoteCache.Emotes[emote.Name] = emote;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                var ctx = new SocketInteractionContext(client, interaction);
                var r1 = await lupeonSvc.ExecuteCommandAsync(ctx, _services);
                if (!r1.IsSuccess && r1.Error == InteractionCommandError.UnknownCommand)
                    await publicSvc.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                try
                {
                    if (!interaction.HasResponded)
                        await interaction.RespondAsync("처리 중 오류가 발생했습니다.", ephemeral: true);
                }
                catch { }
            }
        }

        const ulong WATCH_CATEGORY_ID = 595596190666588185; // 감시할 카테고리
        const ulong TARGET_CATEGORY_ID = 1435983876857008138; // 기본 이동 카테고리

        private async Task OnMessageReceivedAsync(SocketMessage message)
        {
            if (message is not SocketUserMessage msg) return;
            if (msg.Author.IsBot) return;

            if (msg.Channel is not SocketTextChannel channel) return;
            if (channel.CategoryId != WATCH_CATEGORY_ID) return;

            var guild = channel.Guild;

            // 이동 대상 카테고리 결정
            var targetCategory = await GetOrCreateAvailableCategoryAsync(
                guild,
                TARGET_CATEGORY_ID,
                "자동생성"
            );

            // 채널 이동
            await channel.ModifyAsync(x =>
            {
                x.CategoryId = targetCategory.Id;
            });
        }

        public static class EmoteCache
        {
            public static Dictionary<string, Emote> Emotes { get; } = new();
        }

        public static class RoleCache
        {
            public static Dictionary<string, SocketRole> SocketRoles { get; } = new();
        }

        private static IServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton(client)
                .AddSingleton<InteractionService>()
                .AddSingleton<HttpClient>()
                // 기타 싱글톤
                .BuildServiceProvider();
        }

        private void InitStickyIfNeeded()
        {
            if (_stickyInitialized) return;
            _stickyInitialized = true;

            ulong TARGET_GUILD_ID = 513799663086862336;
            _sticky ??= new StickyRefreshService(client, TARGET_GUILD_ID);

            #region 아이템팝니다, 골드팝니다.
            string mAutoMsg =
                $"<#1058371903762468934>을 확인 후 반드시 지켜주세요.\n\n" +
                $"- 거래시 판매자가 골드 및 아이템을 보유 중인지 확인 후 거래하시기 바랍니다.\n\n" +
                $"- 거래도중 의심이 든다면 <#884395336959918100>로 신고해주시기 바랍니다.\n\n" +
                $"- 판매글은 3줄 이하로 작성해주세요.\n\n" +
                $"- 거래소 갱신이 진행 중입니다. 미갱신자는 확인 후 갱신하시기 바랍니다.";

            // 아이템팝니다.
            _sticky.UpsertChannel(
                channelId: 661860451323215873UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mAutoMsg)
                    .WithColor(Color.Blue)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );

            // 골드팝니다.
            _sticky.UpsertChannel(
                channelId: 693357562044874802UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mAutoMsg)
                    .WithColor(Color.Blue)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );
            #endregion

            #region 보석교환
            string mJemMsg =
                $"- 빈줄 포함 10줄 이하로 글을 작성해주세요.\n" +
                $"- 보석 변환 글 작성 시 아래의 5가지를 반드시 포함해야합니다.\n\n" +
                $"- 본캐 레벨 / 원정대 레벨\n" +
                $"- 담보 유무\n" +
                $"- 보석 변환 가능한 티어 / 레벨\n" +
                $"- 본캐 레벨 / 원정대 레벨\n" +
                $"- 보석 변환 비용\n\n" +
                $"- 보석 변환 글 재작성 시 이전 글을 반드시 삭제하고 올려주세요.\n\n" +
                $"- 거래소 갱신이 진행 중입니다. 미갱신자는 확인 후 갱신하시기 바랍니다.";

            _sticky.UpsertChannel(
                channelId: 837673368945557535UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mJemMsg)
                    .WithColor(Color.Orange)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );
            #endregion

            _sticky.Start();
        }
        private async Task<ICategoryChannel> GetOrCreateAvailableCategoryAsync(SocketGuild guild, ulong baseCategoryId, string autoCategoryPrefix)
        {
            var baseCategory = guild.GetCategoryChannel(baseCategoryId);
            if (baseCategory == null)
                throw new Exception("기본 카테고리를 찾을 수 없습니다.");

            // 현재 카테고리 채널 수
            if (baseCategory.Channels.Count < 50)
                return baseCategory;

            // 같은 Prefix의 카테고리들 검색
            var siblings = guild.CategoryChannels
                .Where(c => c.Name.StartsWith(baseCategory.Name))
                .OrderBy(c => c.Position)
                .ToList();

            foreach (var cat in siblings)
            {
                if (cat.Channels.Count < 50)
                    return cat;
            }

            // 전부 꽉 찼으면 새 카테고리 생성
            return await CreateNextCategoryAsync(guild, baseCategory, autoCategoryPrefix);
        }

        private async Task<ICategoryChannel> CreateNextCategoryAsync(SocketGuild guild, SocketCategoryChannel baseCategory,string prefix)
        {
            // 새 카테고리 이름 (예: 거래-자동생성-2)
            int index = 1;
            string newName;
            do
            {
                index++;
                newName = $"{baseCategory.Name}-{prefix}-{index}";
            }
            while (guild.CategoryChannels.Any(c => c.Name == newName));

            var newCategory = await guild.CreateCategoryChannelAsync(newName);

            // 🔹 권한 동기화
            foreach (var overwrite in baseCategory.PermissionOverwrites)
            {
                if (overwrite.TargetType == PermissionTarget.Role)
                {
                    await newCategory.AddPermissionOverwriteAsync(
                        guild.GetRole(overwrite.TargetId),
                        overwrite.Permissions
                    );
                }
                else if (overwrite.TargetType == PermissionTarget.User)
                {
                    await newCategory.AddPermissionOverwriteAsync(
                        guild.GetUser(overwrite.TargetId),
                        overwrite.Permissions
                    );
                }
            }

            // 🔹 위치를 기존 카테고리 바로 아래로
            await newCategory.ModifyAsync(x =>
            {
                x.Position = baseCategory.Position + 1;
            });

            return newCategory;
        }

        private async Task HandleUserLeftAsync(UserLeftJob job, CancellationToken ct)
        {
            // 니 코드에서 하드코딩하던 값들
            const ulong GUILD_ID = 513799663086862336;
            const ulong VOICE_ID = 1457106002553081958;

            // 들어온 길드가 타겟 길드가 아니면 스킵 (안전)
            if (job.GuildId != GUILD_ID) return;

            var guild = client.GetGuild(GUILD_ID);
            if (guild == null) return;

            // ✅ 1) 보이스 채널 이름 갱신 (느릴 수 있으니 여기서만)
            var voiceChannel = guild.GetVoiceChannel(VOICE_ID);
            if (voiceChannel != null)
            {
                // 예외/레이트리밋으로 죽지 않게 try/catch
                try
                {
                    await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UserLeft] voice modify failed: {ex.Message}");
                }
            }

            // ✅ 2) Supabase 처리 (느릴 수 있으니 여기서만)
            try
            {
                var userId = job.UserId.ToString();
                var dbRow = await SupabaseClient.GetSingUpByUserIdAsync(userId);
                if (dbRow != null)
                    await SupabaseClient.DeleteSignUpByUserIdAsync(userId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserLeft] supabase failed: {ex.Message}");
            }
        }


        public Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
