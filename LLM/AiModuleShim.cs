using Discord;
using Discord.WebSocket;
using Discord.Interactions;

namespace Ivirius.Zippy
{
    // Provides initialized module instances with a fake context for AI-triggered calls
    public class AiModuleShim
    {
        public BanModule BanModule { get; }
        public KickModule KickModule { get; }
        public MuteModule MuteModule { get; }
        public WarnModule WarnModule { get; }
        public LockModule LockModule { get; }
        public SlowmodeModule SlowmodeModule { get; }
        public UserInfoModule UserInfoModule { get; }

        public AiModuleShim(DiscordSocketClient client, IGuild guild)
        {
            var context = new AiFakeContext(client, guild);

            BanModule = new BanModule();
            ((IInteractionModuleBase)BanModule).SetContext(context);

            KickModule = new KickModule();
            ((IInteractionModuleBase)KickModule).SetContext(context);

            MuteModule = new MuteModule();
            ((IInteractionModuleBase)MuteModule).SetContext(context);

            WarnModule = new WarnModule();
            ((IInteractionModuleBase)WarnModule).SetContext(context);

            LockModule = new LockModule();
            ((IInteractionModuleBase)LockModule).SetContext(context);

            SlowmodeModule = new SlowmodeModule();
            ((IInteractionModuleBase)SlowmodeModule).SetContext(context);

            UserInfoModule = new UserInfoModule();
            ((IInteractionModuleBase)UserInfoModule).SetContext(context);
        }
    }

    // Minimal fake context that satisfies InteractionModuleBase without a real interaction
    public class AiFakeContext : IInteractionContext
    {
        public IDiscordClient Client { get; }
        public IGuild Guild { get; }
        public IMessageChannel Channel { get; }
        public IUser User { get; }
        public IDiscordInteraction Interaction => throw new NotSupportedException("No real interaction in AI context.");

        public AiFakeContext(DiscordSocketClient client, IGuild guild)
        {
            Client = client;
            Guild = guild;
            Channel = null!; // set per-call if needed
            User = client.CurrentUser;
        }
    }
}
