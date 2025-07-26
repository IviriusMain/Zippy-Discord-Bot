import datetime
from discord import app_commands
from discord.ext import commands
import discord
import aiohttp


class moderation(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    async def unban_autocomplete(
        self, interaction: discord.Interaction, current: str
    ) -> list[app_commands.Choice[str]]:
        banned_users = await interaction.guild.bans()
        print(banned_users)
        return [
            app_commands.Choice(
                name=f"{ban_entry.user.name}", value=str(ban_entry.user.id)
            )
            for ban_entry in banned_users
        ]

    @app_commands.command(name="ban", description="Bans a user from the server")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="The user to ban", reason="The reason for banning the user"
    )
    async def ban(
        self,
        interaction: discord.Interaction,
        user: discord.Member,
        *,
        reason: str | None = None,
    ):
        await interaction.guild.ban(user, reason=reason)

        embed = discord.Embed(
            title="User Banned",
            description=f"{user.mention} was banned from the server",
            color=discord.Color.brand_red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )

        embed.add_field(name=f"User Id `{user.id}`", value="** **", inline=False)
        embed.add_field(
            name=f"Banned By {interaction.user.mention}", value="** **", inline=False
        )
        embed.add_field(name="Reason", value=reason, inline=False)

        try:
            embed.set_thumbnail(url=user.avatar.url)
        except Exception:
            embed.set_thumbnail(url=user.default_avatar.url)

        embed.set_footer(text=f"{user.name} Banned")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="unban", description="Unbans a user from the server")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.autocomplete(user=unban_autocomplete)
    async def unban(self, interaction, user: str):
        user = await self.bot.fetch_user(user)
        await interaction.guild.unban(user)

        embed = discord.Embed(
            title="User Unbanned",
            description=f"{user.mention} was unbanned from the server",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )

        embed.add_field(name=f"User Id   `{user.id}`", value="** **", inline=False)
        embed.add_field(
            name=f"Unbanned By {interaction.user.mention}", value="** **", inline=False
        )

        try:
            embed.set_thumbnail(url=user.avatar.url)
        except Exception:
            embed.set_thumbnail(url=user.default_avatar.url)

        embed.set_footer(text=f"{user.name} Unbanned")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="kick", description="Kicks a user from the server")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="The user to kick", reason="The reason for kicking the user"
    )
    async def kick(
        self,
        interaction: discord.Interaction,
        user: discord.Member,
        *,
        reason: str | None = None,
    ):
        await interaction.guild.kick(user, reason=reason)

        embed = discord.Embed(
            title="User Kicked",
            description=f"{user.mention} was kicked from the server",
            color=discord.Color.orange(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )

        embed.add_field(name=f"User Id `{user.id}`", value="** **", inline=False)
        embed.add_field(
            name=f"Kicked By {interaction.user.mention}", value="** **", inline=False
        )
        embed.add_field(name="Reason", value=reason, inline=False)

        try:
            embed.set_thumbnail(url=user.avatar.url)
        except Exception:
            embed.set_thumbnail(url=user.default_avatar.url)

        embed.set_footer(text=f"{user.name} Kicked")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(
        name="purge", description="Purges a certain amount of messages"
    )
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        amount="The amount of messages to purge",
        channel="The channel to purge the messages from",
    )
    async def purge(
        self,
        interaction: discord.Interaction,
        amount: int,
        channel: discord.TextChannel = None,
    ):
        if not interaction.user.guild_permissions.manage_messages:
            await interaction.response.send_message(
                embed=discord.Embed(
                    description="You don't have the permissions to do that!",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )
            return

        if channel is None:
            channel = interaction.channel

        def check(msg):
            return not msg.pinned

        await channel.purge(limit=amount, check=check)

        embed = discord.Embed(
            title="Purge",
            description=f"Purged {amount} messages",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.add_field(name="Channel", value=channel.mention, inline=False)
        embed.set_footer(text=f"Purged {amount} messages")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="slowmode", description="Sets a slowmode for a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        time="The time in seconds for the slowmode",
        channel="The channel to set the slowmode in",
    )
    async def slowmode(
        self, interaction, time: int, channel: discord.TextChannel = None
    ):
        if time > 21600:
            await interaction.response.send_message(
                embed=discord.Embed(
                    description="Slow Mode cannot be more than 6 hours.",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )
            return

        if time < 0:
            await interaction.response.send_message(
                embed=discord.Embed(
                    description="Slow Mode cannot be less than 0 seconds.",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )
            return

        await interaction.response.defer()

        if channel is None:
            channel = interaction.channel
        await channel.edit(slowmode_delay=time)
        embed = discord.Embed(
            title="Slowmode Set",
            description=f"Set the slowmode to `{time} seconds`",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Slowmode set")
        embed.add_field(name="Channel", value=channel.mention, inline=False)

        await interaction.followup.send(embed=embed)

    @app_commands.command(name="add-role", description="Adds a role to a user")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="The user to add the role to", role="The role to add to the user"
    )
    async def add_role(self, interaction, user: discord.Member, role: discord.Role):
        await user.add_roles(role)
        embed = discord.Embed(
            title="Role Added",
            description=f"Added the role {role.mention} to {user.mention}",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        try:
            embed.set_thumbnail(url=user.avatar.url)
        except Exception:
            embed.set_thumbnail(url=user.default_avatar.url)
        embed.set_footer(text="Role added")
        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="remove-role", description="Removes a role from a user")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="The user to remove the role from", role="The role to remove from the user"
    )
    async def removerole(self, interaction, user: discord.Member, role: discord.Role):
        if role in user.roles:
            await user.remove_roles(role)
            embed = discord.Embed(
                title="Role Removed",
                description=f"Removed the role {role.mention} from {user.mention}",
                color=discord.Color.red(),
                timestamp=datetime.datetime.now(datetime.timezone.utc),
            )
            try:
                embed.set_thumbnail(url=user.avatar.url)
            except Exception:
                embed.set_thumbnail(url=user.default_avatar.url)
            embed.set_footer(text="Role removed")

            await interaction.response.send_message(embed=embed)
        else:
            embed = discord.Embed(
                title="Role Not Found",
                description=f"{user.mention} doesn't have the role {role.mention}",
                color=discord.Color.red(),
            )

            await interaction.response.send_message(embed=embed, ephemeral=True)

    @app_commands.command(name="create-channel", description="Creates a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        name="The name of the channel",
        category="The category to create the channel in",
        private="Whether the channel should be private or not",
    )
    async def createchannel(
        self,
        interaction,
        name: str,
        category: discord.CategoryChannel = None,
        private: bool = False,
    ):
        if not private:
            await interaction.guild.create_text_channel(name=name, category=category)
        else:
            await interaction.guild.create_text_channel(
                name=name,
                category=category,
                overwrites={
                    interaction.guild.default_role: discord.PermissionOverwrite(
                        read_messages=False
                    )
                },
            )

        embed = discord.Embed(
            title="Channel Created",
            description=f"Created the channel `{name}`",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Channel created")

        if category:
            embed.add_field(name="Category", value=category.mention, inline=False)

        await interaction.response.send_message(
            embed=embed
        ) if not private else await interaction.response.send_message(
            embed=embed, ephemeral=True
        )

    @app_commands.command(name="delete-channel", description="Deletes a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(channel="The channel to delete")
    async def deletechannel(self, interaction, channel: discord.TextChannel):
        await channel.delete()
        embed = discord.Embed(
            title="Channel Deleted",
            description=f"Deleted the channel `{channel.name}`",
            color=discord.Color.red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Channel deleted")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="create-role", description="Creates a role")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        name="The name of the role",
        mentionable="Whether the role should be mentionable",
    )
    async def createrole(
        self, interaction, name: str, hoist: bool = False, mentionable: bool = False
    ):
        role = await interaction.guild.create_role(
            name=name, hoist=hoist, mentionable=mentionable
        )
        embed = discord.Embed(
            title="Role Created",
            description=f"Created the role {role.mention}",
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Role created")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="delete-role", description="Deletes a role")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(role="The role to delete")
    async def deleterole(self, interaction, role: discord.Role):
        await role.delete()
        embed = discord.Embed(
            title="Role Deleted",
            description=f"Deleted the role {role.mention}",
            color=discord.Color.red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Role deleted")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="create-category", description="Creates a category")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        name="The name of the category", emoji="The emoji to use for the category"
    )
    async def createcategory(self, interaction, emoji: str, name: str):
        name = f"{emoji} {name}" if emoji else name
        category = await interaction.guild.create_category(name=name)
        embed = discord.Embed(
            title="Category Created",
            description=f"Created the category {category.name}",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Category created")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="delete-category", description="Deletes a category")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(category="The category to delete")
    async def deletecategory(self, interaction, category: discord.CategoryChannel):
        await category.delete()
        embed = discord.Embed(
            title="Category Deleted",
            description=f"Deleted the category {category.name}",
            color=discord.Color.red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Category deleted")

        await interaction.response.send_message(embed=embed)

    async def get_image(self, url: str) -> discord.Asset:
        async with aiohttp.ClientSession() as session:
            async with session.get(url) as response:
                return await response.read()

    @app_commands.command(name="create-emoji", description="Creates an emoji")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(name="The name of the emoji", url="The url of the emoji")
    async def createemoji(self, interaction, name: str, url: str):
        image = await self.get_image(url)
        emoji = await interaction.guild.create_custom_emoji(name=name, image=image)

        embed = discord.Embed(
            title="Emoji Created",
            description=f"Created the emoji <:{name}:{emoji.id}>",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )

        embed.add_field(name="Name", value=name, inline=False)
        embed.add_field(name="Url", value=url, inline=False)
        embed.set_thumbnail(url=url)

        embed.set_footer(text="Emoji created")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="edit-channel", description="Edits a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        channel="The channel to edit",
        name="The new name of the channel",
        category="The new category of the channel",
        private="Whether the channel should be private or not",
        slowmode="The new slowmode of the channel",
    )
    async def editchannel(
        self,
        interaction,
        channel: discord.TextChannel,
        name: str,
        category: discord.CategoryChannel = None,
        private: bool = False,
        slowmode: int = None,
    ):
        if not category:
            category = channel.category
        await channel.edit(
            name=name,
            category=category,
            overwrites={
                interaction.guild.default_role: discord.PermissionOverwrite(
                    read_messages=False
                )
            }
            if private
            else None,
            slowmode_delay=slowmode,
        )
        embed = discord.Embed(
            title="Channel Edited",
            description=f"Edited the channel {channel.name}",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Channel edited")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="archive-channel", description="Archives a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(channel="Moves the selected channel to the archive category")
    async def archivechannel(
        self, interaction, channel: discord.TextChannel, lock: bool = False
    ):
        archive_category = next(
            (
                category
                for category in interaction.guild.categories
                if "archive" in category.name.lower()
            ),
            None,
        )
        if not archive_category:
            archive_category = await interaction.guild.create_category("Archived")

        await channel.edit(category=archive_category)
        if lock:
            await channel.set_permissions(
                interaction.guild.default_role, send_messages=False
            )

        embed = discord.Embed(
            title="Channel Archived",
            description=f"Archived the channel `{channel.name}`",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Channel archived")

        await interaction.response.send_message(embed=embed)


async def setup(bot):
    await bot.add_cog(moderation(bot))
    print("Loaded moderation_cog.py")
