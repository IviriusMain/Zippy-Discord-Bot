import datetime
from discord import app_commands
from discord.ext import commands
import discord
import aiohttp
from discord import app_commands, Interaction, Member, Embed, Color
from discord.ui import View, Select, Modal, TextInput
import datetime
import re
from datetime import timedelta

TESTER_ROLE_ID = 1228628053807202364
IVIRIUS_TEAM_ROLE_ID = 1137503779487486004

class TesterApplicationModal(discord.ui.Modal, title="Tester Application"):
    country = discord.ui.TextInput(
        label="What is your country of residence?",
        required=True,
        max_length=100
    )

    age = discord.ui.TextInput(
        label="Are you at least 13 years of age?",
        required=True,
        placeholder="Yes / No",
        max_length=5
    )

    nda = discord.ui.TextInput(
        label="Do you understand leaking = permanent ban?",
        style=discord.TextStyle.paragraph,
        required=True
    )

    def __init__(self, applicant: discord.Member):
        super().__init__()
        self.applicant = applicant

    async def on_submit(self, interaction: discord.Interaction):
        embed = discord.Embed(
            title="🧪 Tester Application",
            color=discord.Color.blurple()
        )
        embed.add_field(name="Applicant", value=self.applicant.mention, inline=False)
        embed.add_field(name="Country", value=self.country.value, inline=True)
        embed.add_field(name="13+ Years Old", value=self.age.value, inline=True)
        embed.add_field(
            name="Acknowledged NDA",
            value=self.nda.value,
            inline=False
        )

        view = ApplicationReviewView(self.applicant)

        await interaction.response.send_message(
            embed=embed,
            view=view
        )

class ApplicationReviewView(discord.ui.View):
    def __init__(self, applicant: discord.Member):
        super().__init__(timeout=None)
        self.applicant = applicant

    def has_permission(self, interaction: discord.Interaction) -> bool:
        role = interaction.guild.get_role(IVIRIUS_TEAM_ROLE_ID)
        return role in interaction.user.roles or interaction.user.guild_permissions.administrator

    @discord.ui.button(label="Approve", style=discord.ButtonStyle.success)
    async def approve(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.has_permission(interaction):
            await interaction.response.send_message(
                "❌ You are not authorized to approve applications.",
                ephemeral=True
            )
            return

        role = interaction.guild.get_role(TESTER_ROLE_ID)
        if role:
            await self.applicant.add_roles(role, reason="Tester application approved")

        await interaction.response.edit_message(
            content=f"✅ Approved by {interaction.user.mention}",
            view=None
        )

    @discord.ui.button(label="Deny", style=discord.ButtonStyle.danger)
    async def deny(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.has_permission(interaction):
            await interaction.response.send_message(
                "❌ You are not authorized to deny applications.",
                ephemeral=True
            )
            return

        await interaction.response.edit_message(
            content=f"❌ Denied by {interaction.user.mention}",
            view=None
        )

def parse_timespan(timespan: str) -> int | None:
    """Parse a timespan string like '1d2h30m45s' into total minutes."""
    pattern = r"(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?"
    match = re.fullmatch(pattern, timespan.lower())
    if not match:
        return None

    days = int(match.group(1)) if match.group(1) else 0
    hours = int(match.group(2)) if match.group(2) else 0
    minutes = int(match.group(3)) if match.group(3) else 0
    seconds = int(match.group(4)) if match.group(4) else 0

    total_seconds = days*86400 + hours*3600 + minutes*60 + seconds
    if total_seconds == 0:
        return None
    total_minutes = (total_seconds + 59) // 60  # round up seconds to full minute
    return total_minutes

async def send_ban_dm(user: discord.Member, guild: discord.Guild, banned_by: discord.Member, reason: str):
    try:
        dm_embed = discord.Embed(
            title="You have been banned",
            description=(
                f"You have been banned from **{guild.name}**.\n"
                f"**Banned by:** {banned_by}\n"
                f"**Reason:** {reason}"
            ),
            color=discord.Color.red(),
            timestamp=discord.utils.utcnow(),
        )
        await user.send(embed=dm_embed)
    except Exception as e:
        print(f"[Warning] Could not send ban DM to {user}: {e}")

async def send_kick_dm(user: discord.Member, guild: discord.Guild, banned_by: discord.Member, reason: str):
    try:
        dm_embed = discord.Embed(
            title="You have been kicked",
            description=(
                f"You have been kicked from **{guild.name}**.\n"
                f"**Kicked by:** {banned_by}\n"
                f"**Reason:** {reason}"
            ),
            color=discord.Color.red(),
            timestamp=discord.utils.utcnow(),
        )
        await user.send(embed=dm_embed)
    except Exception as e:
        print(f"[Warning] Could not send kick DM to {user}: {e}")

RULES = [
    "Follow Discord ToS",
    "Be respectful to everyone",
    "No spamming",
    "Keep it friendly and SFW",
    "Use channels appropriately",
    "No unhinged behavior",
    "Testers must follow an additional set of rules",
    "No wild claims",
    "Provide constructive feedback",
]

class BanReasonModal(Modal, title="Optional Ban Note"):
    note = TextInput(
        label="Additional note (optional)",
        style=discord.TextStyle.paragraph,
        required=False,
        max_length=300,
        placeholder="Leave any extra info or context here..."
    )

    def __init__(self, user: Member, rules_selected: list[str], interaction: Interaction):
        super().__init__()
        self.user = user
        self.rules_selected = rules_selected
        self.interaction = interaction

    async def on_submit(self, interaction: Interaction):
        # Build embed
        embed = Embed(
            title="User Banned",
            color=Color.brand_red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
            description=f"🔨 {self.user.mention} was banned for breaking the rules."
        )

        embed.add_field(
            name="Rules Broken",
            value="\n".join(f"- {rule}" for rule in self.rules_selected) or "None specified",
            inline=False
        )

        if self.note.value:
            embed.add_field(name="Moderator Note", value=self.note.value, inline=False)

        embed.add_field(name="User ID", value=str(self.user.id), inline=False)
        embed.add_field(name="Banned By", value=interaction.user.mention, inline=False)

        try:
            embed.set_thumbnail(url=self.user.avatar.url)
        except Exception:
            embed.set_thumbnail(url=self.user.default_avatar.url)

        embed.set_footer(text=f"{self.user} banned")

        # Ban user
        try:
            # Send DM before banning
            await send_ban_dm(self.user, interaction.guild, interaction.user, f"Rules: {', '.join(self.rules_selected)} | Note: {self.note.value or 'None'}")
            
            await self.user.ban(reason=f"Banned by {interaction.user} | Rules: {', '.join(self.rules_selected)} | Note: {self.note.value or 'None'}")
        except Exception as e:
            await interaction.response.send_message(f"Failed to ban user: {e}", ephemeral=True)
            return

        await interaction.response.send_message(embed=embed)


class RuleSelect(Select):
    def __init__(self, user: Member, interaction: Interaction):
        super().__init__(
            placeholder="Select rule(s) broken (multiple allowed)",
            min_values=1,
            max_values=len(RULES),
            options=[discord.SelectOption(label=rule) for rule in RULES]
        )
        self.user = user
        self.interaction = interaction

    async def callback(self, interaction: Interaction):
        # Show modal for optional note
        modal = BanReasonModal(self.user, self.values, self.interaction)
        await interaction.response.send_modal(modal)


class BanView(View):
    def __init__(self, user: Member, interaction: Interaction):
        super().__init__(timeout=60)
        self.add_item(RuleSelect(user, interaction))

class moderation(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    async def unban_autocomplete(
        self, interaction: discord.Interaction, current: str
    ) -> list[app_commands.Choice[str]]:
        banned_users = await interaction.guild.bans()
        
        # Filter banned users by current input (case-insensitive)
        filtered = [
            ban_entry for ban_entry in banned_users
            if current.lower() in ban_entry.user.name.lower()
        ]

        # Limit choices to max 25 (Discord limit)
        choices = [
            app_commands.Choice(
                name=f"{ban.user.name}#{ban.user.discriminator}",
                value=str(ban.user.id)
            )
            for ban in filtered[:25]
        ]

        return choices

    @app_commands.command(name="ban", description="Ban a user from the server")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(user="The user to ban")
    async def ban(self, interaction: Interaction, user: Member):
        """Start ban process with rules selection"""

        if user == interaction.user:
            await interaction.response.send_message("You cannot ban yourself.", ephemeral=True)
            return
        if user == self.bot.user:
            await interaction.response.send_message("I cannot ban myself.", ephemeral=True)
            return
        if not interaction.guild.me.guild_permissions.ban_members:
            await interaction.response.send_message("I don't have permission to ban members.", ephemeral=True)
            return

        view = BanView(user, interaction)
        await interaction.response.send_message(
            f"Select the rules {user.mention} broke:", view=view, ephemeral=True
        )

    quick_ban_reasons = [
        app_commands.Choice(name="Compromised account", value="Compromised account"),
        app_commands.Choice(name="Troll", value="Troll"),
        app_commands.Choice(name="Raid", value="Raid"),
    ]

    @app_commands.command(name="apply-tester", description="Apply for tester access")
    async def apply_tester(self, interaction: discord.Interaction):
        modal = TesterApplicationModal(interaction.user)
        await interaction.response.send_modal(modal)

    @app_commands.command(name="quickban", description="Quickly ban a user with a preset reason")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="User to ban",
        reason="Reason for banning",
        note="Optional additional note"
    )
    @app_commands.choices(reason=quick_ban_reasons)
    async def quickban(
        self,
        interaction: discord.Interaction,
        user: discord.Member,
        reason: app_commands.Choice[str],
        note: str | None = None
    ):
        full_reason = reason.value
        if note:
            full_reason += f" | Note: {note}"

        # Send DM before banning
        await send_ban_dm(user, interaction.guild, interaction.user, reason)
        
        await interaction.guild.ban(user, reason=full_reason)

        embed = discord.Embed(
            title="🔨 User Banned",
            description=f"<:admin:1442253091264008324> {user.mention} was banned for **{reason.value}**.",
            color=discord.Color.brand_red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc)
        )
        embed.add_field(name="User ID", value=str(user.id), inline=True)
        embed.add_field(name="Banned by", value=interaction.user.mention, inline=True)
        if note:
            embed.add_field(name="Note", value=note, inline=False)

        try:
            embed.set_thumbnail(url=user.avatar.url)
        except Exception:
            embed.set_thumbnail(url=user.default_avatar.url)

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="unban", description="Unbans a user from the server by user ID")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user_id="The ID of the user to unban",
        reason="Optional reason for unbanning"
    )
    async def unban(
        self,
        interaction: discord.Interaction,
        user_id: str,
        *,
        reason: str | None = None
    ):
        try:
            user_obj = await self.bot.fetch_user(int(user_id))
            await interaction.guild.unban(user_obj, reason=reason)

            embed = discord.Embed(
                title="User Unbanned",
                description=(
                    f"🔓 {user_obj.mention} was unbanned from the server.\n\n"
                    f"{f'Reason: {reason}' if reason else 'Welcome back! Please follow the rules.'}"
                ),
                color=discord.Color.green(),
                timestamp=datetime.datetime.now(datetime.timezone.utc)
            )
            embed.add_field(name="User ID", value=f"`{user_obj.id}`", inline=True)
            embed.add_field(name="Unbanned by", value=interaction.user.mention, inline=True)

            try:
                embed.set_thumbnail(url=user_obj.avatar.url)
            except Exception:
                embed.set_thumbnail(url=user_obj.default_avatar.url)

            embed.set_footer(text=f"{user_obj} unbanned")

            await interaction.response.send_message(embed=embed)

        except discord.NotFound:
            await interaction.response.send_message(
                f"User with ID {user_id} is not banned or does not exist.",
                ephemeral=True
            )
        except Exception as e:
            await interaction.response.send_message(
                f"Failed to unban user: {e}",
                ephemeral=True
            )

    @app_commands.command(name="kick", description="Kicks a user from the server")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="The user to kick",
        reason="The reason for kicking the user"
    )
    async def kick(
        self,
        interaction: discord.Interaction,
        user: discord.Member,
        *,
        reason: str | None = None,
    ):
        try:
            # Send DM before banning
            await send_kick_dm(user, interaction.guild, interaction.user, reason)
            
            await interaction.guild.kick(user, reason=reason)

            embed = discord.Embed(
                title="User Kicked",
                description=(
                    f"👢 {user.mention} was kicked from the server.\n\n"
                    f"{f'Reason: {reason}' if reason else 'No reason provided.'}"
                ),
                color=discord.Color.orange(),
                timestamp=datetime.datetime.now(datetime.timezone.utc),
            )
            embed.add_field(name="User ID", value=f"`{user.id}`", inline=True)
            embed.add_field(name="Kicked by", value=interaction.user.mention, inline=True)

            try:
                embed.set_thumbnail(url=user.avatar.url)
            except Exception:
                embed.set_thumbnail(url=user.default_avatar.url)

            embed.set_footer(text=f"{user} kicked")

            await interaction.response.send_message(embed=embed)
        except Exception as e:
            await interaction.response.send_message(
                f"Failed to kick {user.mention}: {e}", ephemeral=True
            )

    @app_commands.command(
        name="purge",
        description="Purges messages after a specific message ID"
    )
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        message_id="The message ID after which to purge messages",
        channel="The channel to purge messages from (defaults to current channel)"
    )
    async def purge(
        self,
        interaction: discord.Interaction,
        message_id: int,
        channel: discord.TextChannel = None,
    ):
        if not interaction.user.guild_permissions.manage_messages:
            await interaction.response.send_message(
                embed=discord.Embed(
                    description="You don't have permission to do that!",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )
            return

        if channel is None:
            channel = interaction.channel

        try:
            # Fetch the message with the given ID to get its creation time
            base_message = await channel.fetch_message(message_id)
        except discord.NotFound:
            await interaction.response.send_message(
                embed=discord.Embed(
                    description="Message ID not found in this channel.",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )
            return
        except discord.Forbidden:
            await interaction.response.send_message(
                embed=discord.Embed(
                    description="I do not have permission to read message history.",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )
            return

        def check(msg):
            # Delete messages that were sent after the base_message and are not pinned
            return msg.created_at > base_message.created_at and not msg.pinned

        # Purge messages after the message with the given ID (limit to 1000 by default)
        deleted = await channel.purge(limit=1000, check=check)

        embed = discord.Embed(
            title="Purge",
            description=f"Purged {len(deleted)} messages after ID `{message_id}`",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.add_field(name="Channel", value=channel.mention, inline=False)
        embed.set_footer(text=f"Purged messages after {message_id}")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="slowmode", description="Sets a slowmode for a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        time="The time in seconds for the slowmode (0 to disable)",
        channel="The channel to set the slowmode in",
    )
    async def slowmode(
        self,
        interaction: discord.Interaction,
        time: int,
        channel: discord.TextChannel = None,
    ):
        if time < 0 or time > 21600:
            await interaction.response.send_message(
                embed=discord.Embed(
                    description="Slowmode time must be between 0 and 21600 seconds (6 hours).",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )
            return

        if channel is None:
            channel = interaction.channel

        await interaction.response.defer()

        await channel.edit(slowmode_delay=time)

        if time == 0:
            description = f"Slowmode has been removed in {channel.mention}."
            title = "Slowmode Removed"
            color = discord.Color.green()
        else:
            description = f"Set slowmode to `{time} seconds` in {channel.mention}."
            title = "Slowmode Set"
            color = discord.Color.green()

        embed = discord.Embed(
            title=title,
            description=description,
            color=color,
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Slowmode updated")

        await interaction.followup.send(embed=embed)

    @app_commands.command(name="add-role", description="Adds a role to a user")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="The user to add the role to",
        role="The role to add to the user"
    )
    async def add_role(
        self,
        interaction: discord.Interaction,
        user: discord.Member,
        role: discord.Role,
    ):
        try:
            await user.add_roles(role)
            embed = discord.Embed(
                title="Role Added",
                description=f"Added the role {role.mention} to {user.mention}.",
                color=discord.Color.green(),
                timestamp=datetime.datetime.now(datetime.timezone.utc),
            )
            try:
                embed.set_thumbnail(url=user.avatar.url)
            except Exception:
                embed.set_thumbnail(url=user.default_avatar.url)
            embed.set_footer(text="Role added")
            await interaction.response.send_message(embed=embed)
        except Exception as e:
            await interaction.response.send_message(
                embed=discord.Embed(
                    title="Error Adding Role",
                    description=f"Failed to add the role due to: {e}",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )


    @app_commands.command(name="remove-role", description="Removes a role from a user")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        user="The user to remove the role from",
        role="The role to remove from the user"
    )
    async def remove_role(
        self,
        interaction: discord.Interaction,
        user: discord.Member,
        role: discord.Role,
    ):
        if role not in user.roles:
            embed = discord.Embed(
                title="Role Not Found",
                description=f"{user.mention} doesn't have the role {role.mention}.",
                color=discord.Color.red(),
            )
            await interaction.response.send_message(embed=embed, ephemeral=True)
            return

        try:
            await user.remove_roles(role)
            embed = discord.Embed(
                title="Role Removed",
                description=f"Removed the role {role.mention} from {user.mention}.",
                color=discord.Color.orange(),
                timestamp=datetime.datetime.now(datetime.timezone.utc),
            )
            try:
                embed.set_thumbnail(url=user.avatar.url)
            except Exception:
                embed.set_thumbnail(url=user.default_avatar.url)
            embed.set_footer(text="Role removed")
            await interaction.response.send_message(embed=embed)
        except Exception as e:
            await interaction.response.send_message(
                embed=discord.Embed(
                    title="Error Removing Role",
                    description=f"Failed to remove the role due to: {e}",
                    color=discord.Color.red(),
                ),
                ephemeral=True,
            )

    @app_commands.command(name="create-channel", description="Creates a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(
        name="The name of the channel",
        category="The category to create the channel in",
        private="Whether the channel should be private or not",
    )
    async def create_channel(
        self,
        interaction: discord.Interaction,
        name: str,
        category: discord.CategoryChannel | None = None,
        private: bool = False,
    ):
        overwrites = None
        if private:
            overwrites = {
                interaction.guild.default_role: discord.PermissionOverwrite(read_messages=False)
            }

        channel = await interaction.guild.create_text_channel(
            name=name,
            category=category,
            overwrites=overwrites,
        )

        embed = discord.Embed(
            title="Channel Created",
            description=f"Created the channel {channel.mention}",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Channel created")

        if category:
            embed.add_field(name="Category", value=category.mention, inline=False)

        # Ephemeral if private, else public response
        await interaction.response.send_message(embed=embed, ephemeral=private)


    @app_commands.command(name="delete-channel", description="Deletes a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(channel="The channel to delete")
    async def delete_channel(
        self,
        interaction: discord.Interaction,
        channel: discord.TextChannel,
    ):
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
        hoist="Whether the role should be displayed separately",
        mentionable="Whether the role should be mentionable",
    )
    async def create_role(
        self,
        interaction: discord.Interaction,
        name: str,
        hoist: bool = False,
        mentionable: bool = False,
    ):
        role = await interaction.guild.create_role(
            name=name,
            hoist=hoist,
            mentionable=mentionable
        )
        embed = discord.Embed(
            title="Role Created",
            description=f"Created the role {role.mention}",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Role created")

        await interaction.response.send_message(embed=embed)


    @app_commands.command(name="delete-role", description="Deletes a role")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(role="The role to delete")
    async def delete_role(
        self,
        interaction: discord.Interaction,
        role: discord.Role,
    ):
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
        name="The name of the category",
        emoji="The emoji to use for the category (optional)",
    )
    async def create_category(
        self,
        interaction: discord.Interaction,
        name: str,
        emoji: str | None = None,
    ):
        category_name = f"{emoji} {name}" if emoji else name
        category = await interaction.guild.create_category(name=category_name)
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
    async def delete_category(
        self,
        interaction: discord.Interaction,
        category: discord.CategoryChannel,
    ):
        await category.delete()
        embed = discord.Embed(
            title="Category Deleted",
            description=f"Deleted the category {category.name}",
            color=discord.Color.red(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Category deleted")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="archive-channel", description="Archives a channel")
    @app_commands.guild_only()
    @app_commands.default_permissions(administrator=True)
    @app_commands.describe(channel="The channel to archive", lock="Lock the channel to prevent sending messages")
    async def archive_channel(
        self,
        interaction: discord.Interaction,
        channel: discord.TextChannel,
        lock: bool = False,
    ):
        # Find archive category or create it if missing
        archive_category = next(
            (cat for cat in interaction.guild.categories if "archive" in cat.name.lower()), 
            None
        )
        if not archive_category:
            archive_category = await interaction.guild.create_category("Archived")

        # Move channel to archive category
        await channel.edit(category=archive_category)

        # Optionally lock channel (deny sending messages for @everyone)
        if lock:
            await channel.set_permissions(
                interaction.guild.default_role,
                send_messages=False
            )

        embed = discord.Embed(
            title="Channel Archived",
            description=f"Archived the channel {channel.mention}",
            color=discord.Color.green(),
            timestamp=datetime.datetime.now(datetime.timezone.utc),
        )
        embed.set_footer(text="Channel archived")

        await interaction.response.send_message(embed=embed)

    @app_commands.command(name="mute", description="Mute a user by applying a timeout")
    @app_commands.guild_only()
    @app_commands.default_permissions(moderate_members=True)
    @app_commands.describe(
        user="The user to mute",
        duration="Duration for the mute (e.g., 1d2h30m)",
        reason="Reason for muting the user"
    )
    async def mute(
        self,
        interaction: discord.Interaction,
        user: discord.Member,
        duration: str,
        *,
        reason: str | None = None,
    ):
        total_minutes = parse_timespan(duration)
        if total_minutes is None or total_minutes < 1 or total_minutes > 40320:
            await interaction.response.send_message(
                "Invalid duration format or duration must be between 1 minute and 28 days.", ephemeral=True
            )
            return

        if user.timed_out_until and user.timed_out_until > discord.utils.utcnow():
            await interaction.response.send_message(
                f"{user.mention} is already muted.", ephemeral=True
            )
            return

        try:
            timeout_until = discord.utils.utcnow() + timedelta(minutes=total_minutes)
            await user.edit(timed_out_until=timeout_until, reason=reason)
            
            embed = discord.Embed(
                title="User Muted",
                description=(
                    f"🔇 {user.mention} was muted for {duration}.\n"
                    f"Reason: {reason or 'No reason provided.'}"
                ),
                color=discord.Color.orange(),
                timestamp=discord.utils.utcnow()
            )
            embed.set_thumbnail(url=user.avatar.url if user.avatar else user.default_avatar.url)
            embed.set_footer(text=f"Muted by {interaction.user}", icon_url=interaction.user.avatar.url)

            await interaction.response.send_message(embed=embed)
        except Exception as e:
            await interaction.response.send_message(
                f"Failed to mute {user.mention}: {e}", ephemeral=True
            )

    @app_commands.command(name="unmute", description="Removes timeout from a user")
    @app_commands.guild_only()
    @app_commands.default_permissions(moderate_members=True)
    @app_commands.describe(user="The user to unmute")
    async def unmute(self, interaction: discord.Interaction, user: discord.Member):
        try:
            await user.edit(timed_out_until=None)
            embed = discord.Embed(
                title="User Unmuted",
                description=f"🔊 {user.mention} was unmuted.",
                color=discord.Color.green(),
                timestamp=discord.utils.utcnow()
            )
            embed.set_thumbnail(url=user.avatar.url if user.avatar else user.default_avatar.url)
            embed.set_footer(text=f"Unmuted by {interaction.user}", icon_url=interaction.user.avatar.url)
            await interaction.response.send_message(embed=embed)
        except Exception as e:
            await interaction.response.send_message(f"Failed to unmute {user.mention}: {e}", ephemeral=True)

async def setup(bot):
    await bot.add_cog(moderation(bot))
    print("Loaded moderation_cog.py")
