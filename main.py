from dotenv import load_dotenv
import os
import datetime
import discord
from discord.ext import commands
from api import keep_alive
import statistics
import time
import sys
from constants import TOKEN

load_dotenv()

start_time = None
latencies = []


class zippyBot(commands.Bot):
    def __init__(self):
        super().__init__(
            command_prefix=">> ", intents=discord.Intents.all(), help_command=None
        )
        self.synced = False

    async def on_ready(self):
        await load()

        global start_time
        start_time = datetime.datetime.now(datetime.timezone.utc)

        await self.wait_until_ready()
        await self.change_presence(
            activity=discord.CustomActivity(
                name="Custom Status",
                state="Watching Ivirius Text Editor Plus"
            ),
            status=discord.Status.online
        )
        if not self.synced:
            await self.tree.sync()
            self.synced = True

        print(f"Logged in as {self.user.name} (ID: {self.user.id})")
        print(f"Connected to {len(self.guilds)} guilds")

bot = zippyBot()


async def load():
    for filename in os.listdir("./cogs"):
        if filename.endswith(".py"):
            await bot.load_extension(f"cogs.{filename[:-3]}")


@bot.event
async def on_message(message):
    if message.author == bot.user:
        return

    await bot.process_commands(message)

@bot.event
async def on_member_join(member):
    if member.guild.id != 1137161703000375336:
        return

    embed = discord.Embed(
        title="Welcome to <:Ivirius:1208396508941127701> Ivirius™ Community!",
        description=(
            "### Fluent design, brought to you everywhere.\n"
            "Grow together, develop for the people.\n\n"
            "**Useful Links:**\n"
            "[Website](https://ivirius.com/)  |  "
            "[Rebound](https://ivirius.com/rebound/)  |  "
            "[Ivirius Text Editor](https://ivirius.com/ivirius-text-editor/)  |  "
            "[Ivirius Text Editor Plus](https://ivirius.com/ivirius-text-editor-plus/)\n\n"
            "<:ZippyWhen:1400922840727031888>"
        ),
        color=discord.Color.brand_red(),
        timestamp=discord.utils.utcnow()
    )
    try:
        await member.send(embed=embed)
    except Exception as e:
        print(f"[Warning] Failed to send welcome DM to {member}: {e}")


@bot.event
async def on_member_remove(member):
    if member.guild.id != 1137161703000375336:
        return

    join_date = member.joined_at or "Unknown"
    total_time = datetime.datetime.now(datetime.timezone.utc) - join_date if join_date != "Unknown" else "Unknown"

    embed = discord.Embed(
        title="Goodbye!",
        description=f"**{member.name}** ({member.nick or 'No Nickname'}) has left the server.",
        color=discord.Color.red(),
        timestamp=discord.utils.utcnow()
    )

    embed.add_field(name="Join Date", value=join_date.strftime("%Y-%m-%d %H:%M:%S") if join_date != "Unknown" else "Unknown", inline=False)
    embed.add_field(name="Total Time in Server", value=str(total_time) if total_time != "Unknown" else "Unknown", inline=False)
    roles = [role.mention for role in member.roles if role.name != "@everyone"]
    embed.add_field(name="Roles", value=", ".join(roles) if roles else "None", inline=False)

    channel = bot.get_channel(1386050116238049280)
    if channel:
        await channel.send(embed=embed)
    else:
        print("[Warning] Goodbye channel not found.")

@bot.event
async def on_member_ban(guild, user):
    ban_reason = None
    banned_by = None

    async for entry in guild.audit_logs(action=discord.AuditLogAction.ban, limit=1):
        if entry.target.id == user.id:
            ban_reason = entry.reason or "No reason provided"
            banned_by = entry.user
            break

    # Public announcement
    response = (
        f"{user.mention} has been **banned**!\n"
        f"Banned by: {banned_by.mention if banned_by else 'Unknown'}\n"
        f"Reason: {ban_reason}"
    )
    channel = bot.get_channel(1386050116238049280)
    if channel:
        await channel.send(response)
    else:
        print("[Warning] Ban announcement channel not found.")

@bot.event
async def on_member_unban(guild: discord.Guild, user: discord.User):
    unbanned_by = None

    try:
        async for entry in guild.audit_logs(action=discord.AuditLogAction.unban, limit=5):
            if entry.target.id == user.id:
                unbanned_by = entry.user
                break
    except Exception as e:
        print(f"[Warning] Failed to fetch audit logs: {e}")

    channel = bot.get_channel(1386050116238049280)
    if not channel:
        print(f"[Warning] Unban announcement channel not found in guild {guild.name} ({guild.id})")
        return

    description = f"{user.mention} had their ban **revoked**!"
    if unbanned_by:
        description += f"\nRevoked by: {unbanned_by.mention}"
    else:
        description += "\nRevoked by: Unknown"

    embed = discord.Embed(
        title="Ban Revoked",
        description=description,
        color=discord.Color.green(),
        timestamp=discord.utils.utcnow()
    )
    embed.set_footer(text=f"User ID: {user.id}")

    try:
        await channel.send(embed=embed)
    except Exception as e:
        print(f"[Error] Failed to send unban announcement: {e}")


@bot.event
async def on_member_kick(guild, user):
    kick_reason = None
    kicked_by = None

    async for entry in guild.audit_logs(action=discord.AuditLogAction.kick, limit=1):
        if entry.target.id == user.id:
            kick_reason = entry.reason or "No reason provided"
            kicked_by = entry.user
            break

    response = (
        f"{user.mention} has been **kicked**!\n"
        f"Kicked by: {kicked_by.mention if kicked_by else 'Unknown'}\n"
        f"Reason: {kick_reason}"
    )
    channel = bot.get_channel(1386050116238049280)
    if channel:
        await channel.send(response)
    else:
        print("[Warning] Kick announcement channel not found.")


@bot.event
async def on_command_error(ctx, error):
    if isinstance(error, commands.CommandNotFound):
        return
    elif isinstance(error, commands.MissingPermissions):
        await ctx.send("❌ You don't have the required permissions to run this command!")
    elif isinstance(error, commands.MissingRequiredArgument):
        await ctx.send("⚠️ You are missing a required argument!")
    elif isinstance(error, commands.BadArgument):
        await ctx.send("⚠️ You have provided a bad argument!")
    elif isinstance(error, commands.CommandOnCooldown):
        await ctx.send(
            f"⏳ This command is on cooldown! Please try again in {error.retry_after:.2f} seconds."
        )
    elif isinstance(error, commands.NotOwner):
        await ctx.send("⛔ You are not the owner of this bot!")
    elif isinstance(error, commands.MissingRole):
        await ctx.send("❌ You are missing a required role!")
    elif isinstance(error, commands.BotMissingPermissions):
        await ctx.send("❌ I don't have the required permissions to run this command!")
    else:
        # Unexpected error, log for debugging
        print(f"[Error] {ctx.command} raised an error: {error}")
        await ctx.send("⚠️ An unexpected error occurred. Please try again later.")

@bot.command()
@commands.is_owner()
async def sync(ctx):
    await bot.tree.sync()
    synced = await bot.tree.sync()
    if len(synced) > 0:
        await ctx.send(f"Successfully Synced {len(synced)} Commands ✔️")
    else:
        await ctx.send("No Slash Commands to Sync :/")


@bot.event
async def on_command_completion(ctx):
    end = time.perf_counter()
    start = ctx.start
    latency = (end - start) * 1000
    latencies.append(latency)
    if len(latencies) > 10:
        latencies.pop(0)


@bot.before_invoke
async def before_invoke(ctx):
    start = time.perf_counter()
    ctx.start = start


@bot.command()
async def ping(ctx):
    try:
        embed = discord.Embed(
            title="Pong! 🏓",
            color=discord.Color.brand_red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        message = await ctx.send(embed=embed)

        end = time.perf_counter()
        latency = (end - ctx.start) * 1000

        embed.add_field(name="Bot Latency", value=f"{bot.latency * 1000:.2f} ms", inline=False)
        embed.add_field(name="Message Latency", value=f"{latency:.2f} ms", inline=False)

        if latencies:
            average_ping = statistics.mean(latencies)
            embed.add_field(name="Average Message Latency", value=f"{average_ping:.2f} ms", inline=False)

        # Calculate uptime
        current_time = datetime.datetime.now(datetime.timezone.utc)
        delta = current_time - start_time
        hours, remainder = divmod(int(delta.total_seconds()), 3600)
        minutes, seconds = divmod(remainder, 60)

        embed.add_field(
            name="Uptime",
            value=f"{hours}h {minutes}m {seconds}s",
            inline=False,
        )

        embed.set_footer(
            text=f"Requested by: {ctx.author.display_name}",
            icon_url=ctx.author.display_avatar.url,
        )

        await message.edit(embed=embed)

    except Exception as e:
        print(f"Ping command error: {e}", file=sys.stdout)


if __name__ == "__main__":
    if os.environ.get("CLOUD", "True") == "True":
        keep_alive()
    bot.run(token=TOKEN)
