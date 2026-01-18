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

        // ★ 추가: 이동 제외 채널 ID 목록 (여기만 관리)
        private static readonly HashSet<ulong> NO_MOVE_CHANNEL_IDS = new()
        {
            1380476273339666463,
            1317364238754386011,
            882578988260794428,
            653484646260277248
        };

        public async Task MainAsync()
        {
            client.Log += Log;
            client.Ready += Ready;

            // 봇 토큰
            BotToken = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "LupeonBot_Token.txt")).Trim();

            // ✅ 로아 JWT 토큰
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

            // ✅ 이벤트 연결
            client.InteractionCreated += HandleInteraction;
            client.MessageReceived += OnMessageReceivedAsync;
            client.UserJoined += UserJoined;
            client.UserLeft += UserLeft;

            _services = ConfigureServices();

            await client.LoginAsync(TokenType.Bot, BotToken);
            await client.StartAsync();
            await client.SetGameAsync(string.Empty, type: ActivityType.Playing);
            await Task.Delay(-1);
        }

        private Task UserLeft(SocketGuild arg1, SocketUser arg2)
        {
            _userLeftQueue.Enqueue(new UserLeftJob(arg1.Id, arg2.Id));
            return Task.CompletedTask;
        }

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
            if (_registered) return;
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
                await lupeonSvc.AddModuleAsync(t, _services);

            await lupeonSvc.RegisterCommandsToGuildAsync(guildId, deleteMissing: true);

            var maintSvc = new MaintenanceNoticeService(client);
            maintSvc.Start();

            var eventSvc = new EventNoticeService(client);
            eventSvc.Start();

            if (client.GetGuild(guildId) == null)
                return;

            InitStickyIfNeeded();

            foreach (var guild in client.Guilds)
            {
                switch (guild.Id)
                {
                    case 513799663086862336:
                        RoleCache.SocketRoles.Clear();
                        foreach (var role in guild.Roles)
                            RoleCache.SocketRoles[role.Name] = role;

                        var voiceChannel = guild.GetVoiceChannel(1457106002553081958);
                        await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
                        break;

                    case 624936203229069344:
                        EmoteCache.Emotes.Clear();
                        foreach (var emote in guild.Emotes)
                            EmoteCache.Emotes[emote.Name] = emote;
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
                if (!interaction.HasResponded)
                    await interaction.RespondAsync("처리 중 오류가 발생했습니다.", ephemeral: true);
            }
        }

        const ulong WATCH_CATEGORY_ID = 595596190666588185;
        const ulong TARGET_CATEGORY_ID = 1435983876857008138;

        private async Task OnMessageReceivedAsync(SocketMessage message)
        {
            if (message is not SocketUserMessage msg) return;
            if (msg.Author.IsBot) return;
            if (msg.Channel is not SocketTextChannel channel) return;

            // ★ 추가: 특정 채널은 이동 안 함
            if (NO_MOVE_CHANNEL_IDS.Contains(channel.Id)) return;

            if (channel.CategoryId != WATCH_CATEGORY_ID) return;

            var targetCategory = await GetOrCreateAvailableCategoryAsync(
                channel.Guild,
                TARGET_CATEGORY_ID,
                "자동생성"
            );

            await channel.ModifyAsync(x => x.CategoryId = targetCategory.Id);
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
                .BuildServiceProvider();
        }

        private void InitStickyIfNeeded()
        {
            if (_stickyInitialized) return;
            _stickyInitialized = true;

            ulong TARGET_GUILD_ID = 513799663086862336;
            _sticky ??= new StickyRefreshService(client, TARGET_GUILD_ID);

            string mAutoMsg =
                $"<#1058371903762468934>을 확인 후 반드시 지켜주세요.\n\n" +
                $"- 거래시 판매자가 골드 및 아이템을 보유 중인지 확인 후 거래하시기 바랍니다.\n\n" +
                $"- 거래도중 의심이 든다면 <#884395336959918100>로 신고해주시기 바랍니다.\n\n" +
                $"- 판매글은 3줄 이하로 작성해주세요.\n\n" +
                $"- 거래소 갱신이 진행 중입니다. 미갱신자는 확인 후 갱신하시기 바랍니다.";

            _sticky.UpsertChannel(
                channelId: 661860451323215873UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mAutoMsg)
                    .WithColor(Color.Blue)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );

            _sticky.UpsertChannel(
                channelId: 693357562044874802UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mAutoMsg)
                    .WithColor(Color.Blue)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );

             #region 보석교환
             string mJemMsg =
                 $"ㆍ보석 변환 글 작성 시 아래의 5가지를 반드시 포함해야합니다.\n\n" +
                 $"ㆍ빈줄 포함 10줄 이하로 글을 작성해주세요.\n" +
                 $"ㆍ본캐 레벨 / 원정대 레벨\n" +
                 $"ㆍ담보 유무\n" +
                 $"ㆍ보석 변환 가능한 티어 / 레벨\n" +
                 $"ㆍ본캐 레벨 / 원정대 레벨\n" +
                 $"ㆍ보석 변환 비용\n\n" +
                 $"ㆍ보석 변환 글 재작성 시 이전 글을 반드시 삭제하고 올려주세요.";
            
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

            if (baseCategory.Channels.Count < 50)
                return baseCategory;

            var siblings = guild.CategoryChannels
                .Where(c => c.Name.StartsWith(baseCategory.Name))
                .OrderBy(c => c.Position)
                .ToList();

            foreach (var cat in siblings)
            {
                if (cat.Channels.Count < 50)
                    return cat;
            }

            return await CreateNextCategoryAsync(guild, baseCategory, autoCategoryPrefix);
        }

        private async Task<ICategoryChannel> CreateNextCategoryAsync(SocketGuild guild, SocketCategoryChannel baseCategory, string prefix)
        {
            int index = 1;
            string newName;
            do
            {
                index++;
                newName = $"{baseCategory.Name}-{prefix}-{index}";
            }
            while (guild.CategoryChannels.Any(c => c.Name == newName));

            var newCategory = await guild.CreateCategoryChannelAsync(newName);

            foreach (var overwrite in baseCategory.PermissionOverwrites)
            {
                if (overwrite.TargetType == PermissionTarget.Role)
                {
                    await newCategory.AddPermissionOverwriteAsync(
                        guild.GetRole(overwrite.TargetId),
                        overwrite.Permissions
                    );
                }
                else
                {
                    var user = guild.GetUser(overwrite.TargetId);
                    if (user != null)
                        await newCategory.AddPermissionOverwriteAsync(user, overwrite.Permissions);
                }
            }

            await newCategory.ModifyAsync(x => x.Position = baseCategory.Position + 1);
            return newCategory;
        }

        private async Task HandleUserLeftAsync(UserLeftJob job, CancellationToken ct)
        {
            const ulong GUILD_ID = 513799663086862336;
            const ulong VOICE_ID = 1457106002553081958;

            if (job.GuildId != GUILD_ID) return;

            var guild = client.GetGuild(GUILD_ID);
            if (guild == null) return;

            var voiceChannel = guild.GetVoiceChannel(VOICE_ID);
            if (voiceChannel != null)
            {
                try
                {
                    await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
                }
                catch { }
            }

            try
            {
                var userId = job.UserId.ToString();
                var dbRow = await SupabaseClient.GetSingUpByUserIdAsync(userId);
                if (dbRow != null)
                    await SupabaseClient.DeleteSignUpByUserIdAsync(userId);
            }
            catch { }
        }

        public Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}

