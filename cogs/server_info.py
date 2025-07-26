from discord import app_commands
from discord.ext import commands
import discord


class server_info(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    @app_commands.command(name="release", description="Announce a new release of an app.")
    @app_commands.describe(app="Name of the app", version="Version number", link="Download link", ping="Role to ping for this release")
    async def release(
        self, interaction: discord.Interaction, app: str, version: str, link: str, ping: discord.Role
    ):
        embed = discord.Embed(
            title=f"{app} - New Release",
            description=f"**Version {version}**\n[Download here]({link})",
            color=discord.Color.blue()
        )
        await interaction.response.send_message(content=f"<@&{ping.id}>", embed=embed)

    @app_commands.command(
        name="member-count", description="Get the number of members in the server"
    )
    @app_commands.guild_only()
    async def member_count(self, interaction: discord.Interaction):
        human_members = len(
            [member for member in interaction.guild.members if not member.bot]
        )

        embed = discord.Embed(
            title="Member Count",
            description=f"Total Members: {len(interaction.guild.members)}",
            color=discord.Color.green(),
        )

        embed.add_field(name="Humans", value=f"```{human_members}```", inline=True)
        embed.add_field(
            name="Bots",
            value=f"```{len(interaction.guild.members) - human_members}```",
            inline=True,
        )

        embed.set_footer(
            text=f"Requested by {interaction.user.display_name}",
            icon_url=interaction.user.avatar.url,
        )

        await interaction.response.send_message(embed=embed)

    @app_commands.command(
        name="links", description="Get Links to various Ivirius Products"
    )
    @app_commands.guild_only()
    async def links(self, interaction: discord.Interaction):
        embed = discord.Embed(
            title="Ivirius Links",
            description="Here are some links to Ivirius Community products",
            color=discord.Color(int("4287f5", 16)),
        )

        embed.add_field(
            name="Website <:Ivirius:1208396508941127701>",
            value="https://ivirius.com/",
            inline=False,
        )
        embed.add_field(
            name="Rebound <:Rebound:1340813542902993008>",
            value="https://ivirius.com/rebound/",
            inline=False,
        )
        embed.add_field(
            name="Ivirius Text Editor <:TextEditor:1320122327144468480>",
            value="https://ivirius.com/ivirius-text-editor/",
            inline=False,
        )
        embed.add_field(
            name="Ivirius Text Editor Plus <:TextEditorPlus:1320122197750055003>",
            value="https://ivirius.com/ivirius-text-editor-plus/",
            inline=False,
        )

        embed.set_footer(
            text=f"Requested by {interaction.user.display_name}",
            icon_url=interaction.user.avatar.url
            if interaction.user.avatar
            else interaction.user.default_avatar.url,
        )

        await interaction.response.send_message(embed=embed)


async def setup(bot):
    await bot.add_cog(server_info(bot))
    print("Loaded server_info.py")
