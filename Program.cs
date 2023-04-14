
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

using DiscordStarBot.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Reflection;

var oatoken = "";
const string Star_Code = @"⭐";

if (!File.Exists(".secret") || 
    string.IsNullOrEmpty(oatoken = File.ReadAllText(".secret").Trim()))
{

    Console.WriteLine("Please, fill .secret file before launching this bot!");
    File.Open(".secret", FileMode.OpenOrCreate).Close();
    Console.ReadKey();
    return;
}



var db = new BotDatabase();
await db.Database.EnsureCreatedAsync();
//await db.Database.MigrateAsync();

var client = new DiscordSocketClient(new DiscordSocketConfig()
{
    GatewayIntents = GatewayIntents.GuildEmojis | GatewayIntents.GuildMessageReactions | GatewayIntents.GuildMessages |
                     GatewayIntents.MessageContent | GatewayIntents.DirectMessages | GatewayIntents.DirectMessageReactions,
});

var commands = new CommandService(new CommandServiceConfig() { });
var interaction = new InteractionService(client);
interaction.Log += Log;


var cts = new CancellationTokenSource();

AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
{
    Console.WriteLine("Exiting...");
    await client.StopAsync();
    //Release main thread
    cts.Cancel();
};

client.ReactionAdded += OnReaction;
client.ReactionRemoved += OnReaction;
client.Log += Log;

var services = new ServiceCollection()
    .AddSingleton(client)
    .AddSingleton(interaction)
    .AddSingleton(commands)
    .AddDbContext<BotDatabase>((x) =>
    {
        x.UseSqlite("Data Source=base.db");
    })
    .BuildServiceProvider();


await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);
await interaction.AddModulesAsync(Assembly.GetEntryAssembly(), services);

client.InteractionCreated += async (x) =>
{
    var context = new SocketInteractionContext(client, x);

    // Execute the incoming command.
    var result = await interaction.ExecuteCommandAsync(context, services);
};
client.Ready += async () =>
{
    var mod = await interaction.RegisterCommandsGloballyAsync(true);
};


await client.LoginAsync(TokenType.Bot, oatoken);
await client.StartAsync();

//Just wait until exit
cts.Token.WaitHandle.WaitOne();


async Task OnReaction(Cacheable<IUserMessage, ulong> c, Cacheable<IMessageChannel, ulong> c1, SocketReaction reaction)
{
    if (reaction.Emote.Name != Star_Code)
        return;
    //Don't count author reactions

    if(c1.Value is not SocketGuildChannel ch)
        return;

    var msg = (RestUserMessage)(await reaction.Channel.GetMessageAsync(c.Id));

    if (msg.Author.Id == client.CurrentUser.Id)
        return;

    if(msg.Author == reaction.User.Value )
        return;


    // In long long past, first argument get always noncached, so i'll continue to ignore it 
    var rCount = msg.Reactions.FirstOrDefault(x => x.Key.Name == Star_Code).Value.ReactionCount;
    var cfg = await GetConfig(ch.Guild.Id);
    var msgEntry = await GetRestMessageEntry(msg.Id, msg);

    if(cfg.ChannelID is null)
        return;

    if (msgEntry.LastCount == rCount)
        return;

    msgEntry.LastCount = rCount;

    var emb = new EmbedBuilder().WithAuthor(msg.Author).WithColor(Color.LightOrange)
        .WithDescription(msg.Content).WithTitle($"{rCount} {Star_Code} {msg.GetJumpUrl()}");

    if (!string.IsNullOrEmpty(msgEntry.AttachmentUrl) && (msgEntry.AttachmentUrl.EndsWith(".png") || msgEntry.AttachmentUrl.EndsWith(".jpg")))
        emb = emb.WithImageUrl(msgEntry.AttachmentUrl);
    else if (msg.Content.StartsWith("https://") && (msg.Content.EndsWith(".png") || msg.Content.EndsWith(".jpg")))
    {
        try
        {
            //Weird way to validate this, but atm i already have headache
            var uri = new Uri(msg.Content);
            emb = emb.WithImageUrl(msg.Content);
        }
        catch (Exception)
        {
            // ignored
        }
    }

    //Here are no way we can have ChannelID as null, but Roslyn still complain
    var starboardCh = ((IMessageChannel) client.GetChannel(cfg.ChannelID ?? 0));

    //We reached threshold and we don't have printed msg
    if (msgEntry.StarboardMessage is null && rCount >= cfg.ReactionsThreshold)
    {

        var r = await starboardCh.SendMessageAsync(embed: emb.Build());
        msgEntry.StarboardMessage = r.Id;
        await db.SaveChangesAsync();
        return;
    }

    try
    {
        if (rCount <= Math.Max(cfg.ReactionsThreshold - 4, 0))
        {
            await starboardCh.DeleteMessageAsync(msgEntry.StarboardMessage ?? 0);
            msgEntry.StarboardMessage = null;
            //Force update cache
            db.Messages.Update(msgEntry);
            db.SaveChanges();
            return;
        }

        var rest = (RestUserMessage)await starboardCh.GetMessageAsync(msgEntry.StarboardMessage ?? 0);
        await rest.ModifyAsync(x => x.Embed = emb.Build());
    }
    catch (Exception e)
    {
        Console.WriteLine( new LogMessage(LogSeverity.Error, "Reactions", "It appears what or starboard channel are changed, or original message get deleted." +
                                                                          " This entry will be purged from the database", e));
        db.Messages.Remove(msgEntry);
        await db.SaveChangesAsync();
        return;
    }

}

async Task<ConfigModel> GetConfig(ulong guildId)
{
    var config = await db.Config.FirstOrDefaultAsync( x=> x.Id == guildId);
    if (config != null) return config;

    config = new ConfigModel()
    {
        Id = guildId,
        ReactionsThreshold = 1,
    };
    await db.AddAsync(config);
    return config ;
}

async Task<MessageModel> GetMessageEntry(ulong msgId, SocketUserMessage msg)
{
    var msgEntry = await db.Messages.FirstOrDefaultAsync(x => x.Id == msgId);
    if (msgEntry != null) return msgEntry;

    msgEntry = new MessageModel()
    {
        Id = msgId,
        UserID = msg.Author.Id,
        Content = msg.Content,
        AttachmentUrl = msg.Attachments?.FirstOrDefault()?.ProxyUrl,
        GuildID = ((SocketGuildChannel) (msg.Channel)).Guild.Id,
        LastCount = 0,
        StarboardMessage = null,
    };
    await db.AddAsync(msgEntry);
    return msgEntry;
}

async Task<MessageModel> GetRestMessageEntry(ulong msgId, RestUserMessage msg)
{
    var msgEntry = await db.Messages.FirstOrDefaultAsync(x => x.Id == msgId);
    if (msgEntry != null) return msgEntry;

    msgEntry = new MessageModel()
    {
        Id = msgId,
        UserID = msg.Author.Id,
        Content = msg.Content,
        AttachmentUrl = msg.Attachments?.FirstOrDefault()?.ProxyUrl,
        GuildID = ((SocketGuildChannel)(msg.Channel)).Guild.Id,
        LastCount = 0,
        StarboardMessage = null,
    };
    await db.AddAsync(msgEntry);
    return msgEntry;
}


Task Log(LogMessage msg)
{
    return Task.Run(() => Console.WriteLine(msg.ToString()));
}