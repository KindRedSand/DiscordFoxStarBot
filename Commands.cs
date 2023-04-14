using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using DiscordStarBot.Database;

using Microsoft.EntityFrameworkCore;

namespace DiscordStarBot
{
    public class Commands : InteractionModuleBase<SocketInteractionContext>
    {
        const string Star_Code = @"⭐";

        private readonly BotDatabase _db;
        private readonly DiscordSocketClient _client;

        public Commands(BotDatabase db, DiscordSocketClient client)
        {
            _db = db;
            _client = client;
        }

        internal const string Set_Channel = "star-channel";
        internal const string Set_Threshold = "star-threshold";

        [EnabledInDm(false)]
        [SlashCommand("star-channel", "Set specific channel as starboard", runMode: RunMode.Async)]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SetChannel(IChannel channel)
        {
            var cfg = await GetConfig(Context.Guild.Id);
            cfg.ChannelID = channel.Id;
            await _db.SaveChangesAsync();

            switch (Context.Interaction.UserLocale)
            {
                case "ru":
                    await RespondAsync($"Канал {channel.Name} будет ипользоваться как доска почета", ephemeral: true);
                    break;
                default:
                    await RespondAsync($"You set channel {channel.Name} as starboard channel", ephemeral: true);
                    break;
            }
        }

        [EnabledInDm(false)]
        [SlashCommand("star-count", "Set specific channel as starboard", runMode: RunMode.Async)]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SetCount([MinValue(1)]int count)
        {
            var cfg = await GetConfig(Context.Guild.Id);
            cfg.ReactionsThreshold = count;
            await _db.SaveChangesAsync();

            switch (Context.Interaction.UserLocale)
            {
                case "ru":
                    await RespondAsync($"Сообщения будут добавлятся на доску почета по достежению {count} {Star_Code}", ephemeral: true);
                    break;
                default:
                    await RespondAsync($"Now messages will be sent to starboard once they recieve {count} {Star_Code}", ephemeral: true);
                    break;
            }
        }

        async Task<ConfigModel> GetConfig(ulong guildId)
        {
            var config = await _db.Config.FirstOrDefaultAsync(x => x.Id == guildId);
            if (config != null) return config;

            config = new ConfigModel()
            {
                Id = guildId,
                ReactionsThreshold = 1,
            };
            await _db.AddAsync(config);
            return config;
        }
    }
}
