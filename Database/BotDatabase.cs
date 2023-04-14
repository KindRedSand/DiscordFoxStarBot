using Microsoft.EntityFrameworkCore;

namespace DiscordStarBot.Database
{
    public class BotDatabase : DbContext
    {
        public DbSet<ConfigModel> Config { get; set; }
        public DbSet<MessageModel> Messages { get; set; }


        private bool IsContextSet { get; }
        /// <summary>
        /// Create a context and connect to local DB
        /// </summary>
        public BotDatabase()
        {
            IsContextSet = false;
        }

        /// <summary>
        /// Create a context configured to connect to a specific DB
        /// Probably you should adjust migrations to match your DB provider before using it
        /// </summary>
        public BotDatabase(DbContextOptions<BotDatabase> options) : base(options)
        {
            IsContextSet = true;
        }


        readonly string ConnectionString = string.Concat("Data Source=base.db");
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            if(!IsContextSet)
                optionsBuilder.UseSqlite(ConnectionString);
        }
    }
}
