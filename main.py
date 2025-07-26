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
        title="Welcome to the server!",
        description=f"Welcome to the server {member.name}! Please read the rules and have fun!",
        color=discord.Color.brand_red(),
    )
    await member.send(embed=embed)


@bot.event
async def on_member_remove(member):
    if member.guild.id != 1137161703000375336:
        return

    user = member.name
    nickname = member.nick
    join_date = member.joined_at
    total_time = datetime.datetime.now() - join_date

    join_date_formatted = join_date.strftime("%Y-%m-%d %H:%M:%S")
    total_time_formatted = str(total_time)

    embed = discord.Embed(
        title="Goodbye!",
        description=f"**{user}** ({nickname}) has left the server.",
        color=discord.Color.red(),
    )

    embed.add_field(name="Join Date", value=join_date_formatted, inline=False)
    embed.add_field(
        name="Total Time in Server", value=total_time_formatted, inline=False
    )

    roles = [role.mention for role in member.roles]
    embed.add_field(name="Roles", value=", ".join(roles), inline=False)

    channel = bot.get_channel(1386050116238049280)
    await channel.send(embed=embed)


@bot.event
async def on_member_ban(guild, user):
    ban_reason = None
    banned_by = None

    async for entry in guild.audit_logs(action=discord.AuditLogAction.ban, limit=1):
        if entry.target == user:
            ban_reason = entry.reason
            banned_by = entry.user
            break

    response = (
        f"{user.mention} has been **banned**!\n"
        f"Banned by: {banned_by.mention}\n"
        f"Reason: {ban_reason}"
    )
    target_channel = bot.get_channel(1188420266234228809)
    await target_channel.send(response)


@bot.event
async def on_member_unban(guild, user):
    unbanned_by = None

    async for entry in guild.audit_logs(action=discord.AuditLogAction.unban, limit=1):
        if entry.target == user:
            unbanned_by = entry.user
            break

    response = (
        f"{user.mention} had their ban **revoked**!\n"
        f"Revoked by: {unbanned_by.mention}\n"
    )

    target_channel = bot.get_channel(1188420266234228809)
    await target_channel.send(response)


@bot.event
async def on_member_kick(guild, user):
    kick_reason = None
    kicked_by = None

    async for entry in guild.audit_logs(action=discord.AuditLogAction.kick, limit=1):
        if entry.target == user:
            kick_reason = entry.reason
            kicked_by = entry.user
            break

    response = (
        f"{user.mention} has been **kicked**!\n"
        f"Kicked by: {kicked_by.mention}\n"
        f"Reason: {kick_reason}"
    )
    target_channel = bot.get_channel(1188420266234228809)
    await target_channel.send(response)


@bot.event
async def on_command_error(ctx, error):
    if isinstance(error, commands.CommandNotFound):
        return
    elif isinstance(error, commands.MissingPermissions):
        await ctx.send("You don't have the required permissions to run this command!")
    elif isinstance(error, commands.MissingRequiredArgument):
        await ctx.send("You are missing a required argument!")
    elif isinstance(error, commands.BadArgument):
        await ctx.send("You have provided a bad argument!")
    elif isinstance(error, commands.CommandOnCooldown):
        await ctx.send(
            f"This command is on cooldown! Please try again in {error.retry_after:.2f} seconds."
        )
    elif isinstance(error, commands.NotOwner):
        await ctx.send("You are not the owner of this bot!")
    elif isinstance(error, commands.MissingRole):
        await ctx.send("You are missing a required role!")
    elif isinstance(error, commands.BotMissingPermissions):
        await ctx.send("I don't have the required permissions to run this command!")


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
        embed = discord.Embed(title="Pong!", color=discord.Color.brand_red())
        message = await ctx.send(embed=embed)

        end = time.perf_counter()
        latency = (end - ctx.start) * 1000

        embed.add_field(
            name="Bot Latency", value=f"{bot.latency * 1000:.2f} ms", inline=False
        )
        embed.add_field(name="Message Latency", value=f"{latency:.2f} ms", inline=False)

        # Calculate the average ping of the bot in the last 10 minutes
        if latencies:
            average_ping = statistics.mean(latencies)
            embed.add_field(
                name="Average Message Latency",
                value=f"{average_ping:.2f} ms",
                inline=False,
            )

        global start_time
        current_time = datetime.datetime.now(datetime.timezone.utc)
        delta = current_time - start_time

        hours, remainder = divmod(int(delta.total_seconds()), 3600)
        minutes, seconds = divmod(remainder, 60)

        embed.add_field(
            name="Uptime",
            value=f"{hours} hours {minutes} minutes {seconds} seconds",
            inline=False,
        )
        embed.set_footer(
            text="Information requested by: {}".format(ctx.author.name),
            icon_url=ctx.author.avatar.url,
        )
        embed.set_thumbnail(
            url="https://uploads.poxipage.com/7q5iw7dwl5jc3zdjaergjhpat27tws8bkr9fgy45_938843265627717703-webp"
        )

        await message.edit(embed=embed)

    except Exception as e:
        print(e, file=sys.stdout)


if __name__ == "__main__":
    if os.environ.get("CLOUD", "True") == "True":
        keep_alive()
    bot.run(token=TOKEN)
