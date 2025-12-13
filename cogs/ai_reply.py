import random
from discord.ext import commands, tasks
import aiohttp
import discord
import urllib.parse
import re
from difflib import get_close_matches
import datetime
import asyncio

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

def sanitize_message(content: str) -> str:
    """Remove all mass ping mentions from message content - bulletproof edition"""
    if not content:
        return content
    
    # First pass: normalize unicode and remove zero-width characters
    import unicodedata
    content = unicodedata.normalize('NFKC', content)
    content = content.replace('\u200b', '').replace('\u200c', '').replace('\u200d', '').replace('\ufeff', '')
    
    # Remove any @ symbol followed by variations of "everyone" or "here"
    # This catches @everyone, @@everyone, @​everyone (with zero-width spaces), etc.
    patterns = [
        # Everyone variations (case insensitive, with any amount of @'s before, and any weird characters between letters)
        (r'@+\s*[eE3€][^\w\s]{0,3}[vV][^\w\s]{0,3}[eE3€][^\w\s]{0,3}[rR][^\w\s]{0,3}[yY][^\w\s]{0,3}[oO0][^\w\s]{0,3}[nN][^\w\s]{0,3}[eE3€]', 'everyone'),
        # Here variations
        (r'@+\s*[hH][^\w\s]{0,3}[eE3€][^\w\s]{0,3}[rR][^\w\s]{0,3}[eE3€]', 'here'),
        # Catch any @ followed by the words even if obfuscated with spaces/dots/underscores
        (r'@+[\s\._-]*e[\s\._-]*v[\s\._-]*e[\s\._-]*r[\s\._-]*y[\s\._-]*o[\s\._-]*n[\s\._-]*e', 'everyone'),
        (r'@+[\s\._-]*h[\s\._-]*e[\s\._-]*r[\s\._-]*e', 'here'),
        # Unicode lookalikes (common homoglyphs)
        (r'@+(?:еvеrуоnе|еvеrуone|еveryone)', 'everyone'),  # Cyrillic е, у, о
        (r'@+(?:hеrе|here)', 'here'),  # Cyrillic е
        # Catch escaped versions
        (r'\\@everyone', 'everyone'),
        (r'\\@here', 'here'),
        # HTML entities
        (r'&#64;everyone', 'everyone'),
        (r'&#64;here', 'here'),
        (r'&commat;everyone', 'everyone'),
        (r'&commat;here', 'here'),
    ]
    
    for pattern, replacement in patterns:
        content = re.sub(pattern, replacement, content, flags=re.IGNORECASE)
    
    # Final safety net: if the word "everyone" or "here" appears right after any @ symbol (even multiple)
    # and there are fewer than 3 characters between them, nuke the @
    content = re.sub(r'@+.{0,3}(everyone)', r'\1', content, flags=re.IGNORECASE)
    content = re.sub(r'@+.{0,3}(here)', r'\1', content, flags=re.IGNORECASE)
    
    # Remove any remaining standalone @ symbols that might have been used for obfuscation
    # but ONLY if they're followed by 'everyone' or 'here' within 5 chars
    words = content.split()
    cleaned_words = []
    for i, word in enumerate(words):
        # Check if this word or next word contains everyone/here
        current_and_next = word + (' ' + words[i+1] if i+1 < len(words) else '')
        if 'everyone' in current_and_next.lower() or 'here' in current_and_next.lower():
            # Remove all @ symbols from this word
            word = word.replace('@', '')
        cleaned_words.append(word)
    content = ' '.join(cleaned_words)
    
    return content

class AIReplyCog(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        
        # IVIRIUS COMMUNITY SERVER ID
        self.IVIRIUS_SERVER_ID = 1137161703000375336
        
        # Role hierarchy (highest to lowest authority)
        self.COMMUNITY_LEAD = 1141002659860586496
        self.CO_OWNER = 1315044101141299322
        self.TEAM_MANAGER = 1292119418104578149
        self.CUSTOMER_SUPPORT = 1312400733508866068
        self.IVIRIUS_TEAM = 1137503779487486004
        self.SERVER_ADMIN = 1137503134437097482
        
        # Protected channels (cannot be accessed unless already in them)
        self.PROTECTED_CHANNELS = {
            1139901541701124167,
            1256377322139947039,
            1217994528041074741,
            1228405114247843880,
            1179569780697616456,
            1320465681052864613,
            1386050116238049280
        }
    
    def is_ivirius_server(self, guild: discord.Guild) -> bool:
        """Check if this is the Ivirius Community server"""
        return guild and guild.id == self.IVIRIUS_SERVER_ID

    @tasks.loop(minutes=3)
    async def zippy_auto_chat(self):
        await self.bot.wait_until_ready()
        guild = self.bot.get_guild(1137161703000375336)
        if not guild:
            return
        
        # Pick the channel Zippy should hang out in
        channel = guild.get_channel(1137161703000375339)
        if not channel:
            return
        
        # Fetch last message
        last_msg = None
        async for msg in channel.history(limit=1):
            last_msg = msg
        
        if not last_msg:
            return
        
        # Ignore if last msg was from the bot
        if last_msg.author.id == self.bot.user.id:
            return
        
        # Activity check - must be at least one msg in X minutes
        minutes_since_last = (datetime.datetime.utcnow() - last_msg.created_at.replace(tzinfo=None)).total_seconds() / 60
        
        if minutes_since_last > 45:
            # Channel too quiet, Zippy won't bother
            return
        
        # Randomized chance so he doesn't talk too much
        if random.random() > 0.15:  # ~15% chance every loop
            return
        
        # Build context
        async for msg in channel.history(limit=10):
            pass  # You already do context building in your generation method
        
        # Generate message
        response = await self.generate_ai_response(
            last_msg,
            last_msg.author.id,
            str(last_msg.author),
            is_mod=False,
            additional_context="",
            voluntary=True
        )
        
        # Sanitize before sending
        response = sanitize_message(response)
        await channel.send(response)

    def is_probably_toxic(self, text: str) -> bool:
        txt = text.lower()
        toxic_patterns = [
            r'\bnigg[ae]r?\b',
            r'\bnigga\b',
            r'\bfaggot\b',
            r'\bfag\b',
            r'\bcunt\b',
            r'\bkill yourself\b',
            r'\bgo kill yourself\b',
            r'\bshoot yourself\b',
            r'\bshoot yourself in the head\b',
            r'\bhope you die\b',
            r'\byou should die\b',
            r'\bfucking die\b',
            r'\bkys\b',
            r'\bdie now\b',
            r'\bdie already\b',
            r'\bdie you\b',
            r'\bdie slow\b',
            r'\bdie\b',
            r'\bretard\b',
            r'\bidiot\b',
            r'\bshithead\b',
            r'\basshole\b',
            r'\bfuckin\b',
            r'\bshitface\b',
            r'\bcock\b',
            r'\bmotherfucker\b',
            r'\bslut\b',
            r'\bbitch\b',
            r'\bwhore\b',
            r'\bdying\b',
            r'\bsuicide\b',
            r'\bsuicidal\b',
            r'\bkill urself\b',
            r'\bkill ur self\b',
        ]
        for pattern in toxic_patterns:
            if re.search(pattern, txt):
                return True
        return False

    def get_user_authority_level(self, member: discord.Member) -> int:
        """Returns authority level (higher = more power)"""
        role_ids = [role.id for role in member.roles]
        
        if self.COMMUNITY_LEAD in role_ids:
            return 6
        elif self.CO_OWNER in role_ids:
            return 5
        elif self.TEAM_MANAGER in role_ids:
            return 4
        elif self.IVIRIUS_TEAM in role_ids:
            return 2
        elif self.CUSTOMER_SUPPORT in role_ids:
            return 3
        elif self.SERVER_ADMIN in role_ids:
            return 1
        else:
            return 0
    
    def can_moderate_target(self, moderator: discord.Member, target: discord.Member) -> tuple[bool, str]:
        """Check if moderator can take action against target"""
        mod_level = self.get_user_authority_level(moderator)
        target_level = self.get_user_authority_level(target)
        
        if moderator.id == target.id:
            return False, "You cannot moderate yourself"
        if target.id == self.bot.user.id:
            return False, "Cannot moderate the bot"
        
        if mod_level == 0:
            return False, "You don't have moderation permissions"
        
        if mod_level == 1 and target_level >= 2:
            return False, f"Server admins cannot moderate {'team members, customer support,' if target_level >= 2 else 'users'} or higher authority"
        
        if target_level == 3 and mod_level < 4:
            return False, "Only Team Managers and above can moderate Customer Support"
        
        if target_level >= mod_level:
            return False, "Cannot moderate users with equal or higher authority"
        
        return True, "OK"
    
    def has_mod_permissions(self, member: discord.Member) -> bool:
        """Check if user has any moderation permissions"""
        level = self.get_user_authority_level(member)
        return level > 0 or member.guild_permissions.administrator
    
    async def can_access_channel(self, channel: discord.TextChannel, requesting_from: discord.TextChannel) -> bool:
        """Check if bot can access a protected channel"""
        if channel.id in self.PROTECTED_CHANNELS:
            return requesting_from.id == channel.id
        return True
    
    async def extract_channel_mentions(self, content: str, guild: discord.Guild):
        """Extract channel IDs from message content"""
        channel_pattern = r'<#(\d+)>'
        channel_ids = re.findall(channel_pattern, content)
        channels = []
        for channel_id in channel_ids:
            channel = guild.get_channel(int(channel_id))
            if channel:
                channels.append(channel)
        return channels
    
    async def extract_user_mentions(self, content: str, guild: discord.Guild, exclude_bot_id: int = None):
        """Extract user IDs from message content and replied message"""
        user_pattern = r'<@!?(\d+)>'
        user_ids = re.findall(user_pattern, content)
        users = []
        for user_id in user_ids:
            user_id_int = int(user_id)
            if exclude_bot_id and user_id_int == exclude_bot_id:
                continue
            member = guild.get_member(user_id_int)
            if member:
                users.append(member)
        return users
    
    async def get_channel_context(self, channel: discord.TextChannel, limit: int = 25):
        """Fetch recent messages from a specific channel"""
        messages = []
        async for msg in channel.history(limit=limit):
            author_info = f"{msg.author.display_name} ({msg.author.name})"
            messages.append(f"{author_info}: {msg.content}")
        messages.reverse()
        return "\n".join(messages)
    
    async def find_role_by_name(self, guild: discord.Guild, role_name: str):
        """Find a role by name with fuzzy matching"""
        role_name = role_name.strip('<>@&').lower()
        
        for role in guild.roles:
            if role.name.lower() == role_name:
                return role
        
        role_names = [role.name for role in guild.roles]
        matches = get_close_matches(role_name, role_names, n=1, cutoff=0.6)
        if matches:
            return discord.utils.get(guild.roles, name=matches[0])
        
        return None
    
    async def scan_user_messages(self, guild: discord.Guild, user: discord.Member, limit_per_channel: int = 50):
        """Scan user's recent messages across channels for suspicious content"""
        suspicious_messages = []
        
        for channel in guild.text_channels:
            if channel.id in self.PROTECTED_CHANNELS:
                continue
                
            try:
                async for msg in channel.history(limit=limit_per_channel):
                    if msg.author.id == user.id:
                        content_lower = msg.content.lower()
                        if any(indicator in content_lower for indicator in [
                            'http://', 'https://', 'discord.gg/', '.gg/',
                            'free nitro', 'click here', 'dm me', '@everyone',
                            'investment', 'crypto', 'click this link'
                        ]):
                            suspicious_messages.append({
                                'channel': channel.name,
                                'content': msg.content[:100],
                                'created_at': msg.created_at
                            })
            except discord.Forbidden:
                continue
        
        return suspicious_messages
    
    def contains_mod_command(self, text: str) -> bool:
        text = text.lower()
        mod_keywords = ['ban', 'kick', 'timeout', 'mute', 'unban', 'remove timeout', 'unmute', 'add role', 'remove role', 'do your thing', 'handle this', 'take care of this']
        return any(keyword in text for keyword in mod_keywords)

    async def handle_moderation_request(self, message: discord.Message, ai_response: str):
        """Parse user's request and execute moderation actions"""
        if not self.has_mod_permissions(message.author):
            return [], ai_response
        
        actions_taken = []
        user_message = message.content.lower()
        
        mentioned_users = await self.extract_user_mentions(
            message.content, message.guild, exclude_bot_id=self.bot.user.id
        )
        
        if message.reference and message.reference.resolved:
            replied_msg = message.reference.resolved
            if isinstance(replied_msg, discord.Message):
                replied_user = replied_msg.author
                if (isinstance(replied_user, discord.Member) and 
                    replied_user.id != self.bot.user.id and
                    replied_user not in mentioned_users):
                    mentioned_users.append(replied_user)
        
        if any(phrase in user_message for phrase in [
            'do your thing', 'do its thing', 'take care of this', 'handle this',
            'deal with this', 'get rid of', 'compromised', 'spammer', 'spam bot'
        ]):
            for member in mentioned_users:
                can_moderate, reason = self.can_moderate_target(message.author, member)
                if not can_moderate:
                    actions_taken.append(f"Cannot moderate {member.mention}: {reason}")
                    continue
                
                try:
                    suspicious = await self.scan_user_messages(message.guild, member)
                    await member.ban(reason=f"Suspected spam/compromised account - Requested by {message.author}")
                    
                    action_msg = f"Banned {member.mention}"
                    if suspicious:
                        action_msg += f"\n  Found {len(suspicious)} suspicious messages across channels:"
                        for i, sus in enumerate(suspicious[:3], 1):
                            action_msg += f"\n  {i}. #{sus['channel']}: {sus['content'][:50]}..."
                    
                    actions_taken.append(action_msg)
                except Exception as e:
                    actions_taken.append(f"Failed to ban {member.mention}: {str(e)}")
        
        elif 'unban' in user_message:
            unban_targets = []
            user_id_matches = re.findall(r'<@!?(\d+)>|(?:^|\s)(\d{17,20})(?:\s|$)', message.content)
            for match in user_id_matches:
                user_id = int(match[0] or match[1])
                if user_id != self.bot.user.id:
                    unban_targets.append(user_id)
            
            if message.reference and message.reference.resolved:
                replied_msg = message.reference.resolved
                if isinstance(replied_msg, discord.Message):
                    unban_targets.append(replied_msg.author.id)
            
            if unban_targets:
                for user_id in unban_targets:
                    try:
                        user = await self.bot.fetch_user(user_id)
                        await message.guild.unban(user, reason=f"Requested by {message.author}")
                        actions_taken.append(f"Unbanned {user.mention} ({user.name})")
                    except discord.NotFound:
                        actions_taken.append(f"User {user_id} is not banned")
                    except Exception as e:
                        actions_taken.append(f"Failed to unban {user_id}: {str(e)}")
            else:
                actions_taken.append("Please mention a user or provide a user ID to unban")
        
        elif any(word in user_message for word in ['kick', 'boot']):
            for member in mentioned_users:
                can_moderate, reason = self.can_moderate_target(message.author, member)
                if not can_moderate:
                    actions_taken.append(f"Cannot moderate {member.mention}: {reason}")
                    continue
                
                try:
                    await member.kick(reason=f"Requested by {message.author}")
                    actions_taken.append(f"Kicked {member.mention}")
                except Exception as e:
                    actions_taken.append(f"Failed to kick {member.mention}: {str(e)}")
        
        elif 'ban' in user_message and 'unban' not in user_message:
            for member in mentioned_users:
                can_moderate, reason = self.can_moderate_target(message.author, member)
                if not can_moderate:
                    actions_taken.append(f"Cannot moderate {member.mention}: {reason}")
                    continue
                
                try:
                    dm_reason = f"Requested by {message.author}"
                    await send_ban_dm(member, message.guild, message.author, dm_reason)
                    await member.ban(reason=dm_reason)
                    actions_taken.append(f"Banned {member.mention}")
                except Exception as e:
                    actions_taken.append(f"Failed to ban {member.mention}: {str(e)}")
        
        elif any(phrase in user_message for phrase in [
            'remove timeout', 'untimeout', 'unmute', 'remove the timeout', 'lift timeout'
        ]):
            if not mentioned_users:
                if message.reference and message.reference.resolved:
                    replied_msg = message.reference.resolved
                    if isinstance(replied_msg, discord.Message) and isinstance(replied_msg.author, discord.Member):
                        mentioned_users = [replied_msg.author]
            
            for member in mentioned_users:
                can_moderate, reason = self.can_moderate_target(message.author, member)
                if not can_moderate:
                    actions_taken.append(f"Cannot moderate {member.mention}: {reason}")
                    continue
                
                try:
                    await member.timeout(None, reason=f"Timeout removed by {message.author}")
                    actions_taken.append(f"Removed timeout from {member.mention}")
                except Exception as e:
                    actions_taken.append(f"Failed to remove timeout from {member.mention}: {str(e)}")
        
        elif any(word in user_message for word in ['timeout', 'time out', 'mute']):
            duration_match = re.search(r'(\d+)\s*(second|minute|hour|min|sec|hr)s?', user_message)
            if duration_match:
                amount = int(duration_match.group(1))
                unit = duration_match.group(2)
                
                if unit.startswith('sec'):
                    duration = datetime.timedelta(seconds=amount)
                elif unit.startswith('min'):
                    duration = datetime.timedelta(minutes=amount)
                elif unit.startswith('hour') or unit.startswith('hr'):
                    duration = datetime.timedelta(hours=amount)
                else:
                    duration = datetime.timedelta(minutes=5)
            else:
                duration = datetime.timedelta(minutes=5)
            
            for member in mentioned_users:
                can_moderate, reason = self.can_moderate_target(message.author, member)
                if not can_moderate:
                    actions_taken.append(f"Cannot moderate {member.mention}: {reason}")
                    continue
                
                try:
                    await member.timeout(duration, reason=f"Requested by {message.author}")
                    actions_taken.append(f"Timed out {member.mention} for {duration}")
                except Exception as e:
                    actions_taken.append(f"Failed to timeout {member.mention}: {str(e)}")
        
        elif any(phrase in user_message for phrase in ['add', 'give']) and any(word in user_message for word in ['role', 'to']):
            role_pattern = r'(?:the role|role|to)\s+"([^"]+)"|(?:the role|role|to)\s+([a-zA-Z0-9\s]+)'
            role_match = re.search(role_pattern, user_message)

            if role_match:
                role_name = role_match.group(1) or role_match.group(2)
                if role_name:
                    role_name = role_name.strip()
                    role = await self.find_role_by_name(message.guild, role_name)
                    if role:
                        for member in mentioned_users:
                            try:
                                await member.add_roles(role)
                                actions_taken.append(f"Added {role.mention} to {member.mention}")
                            except Exception as e:
                                actions_taken.append(f"Failed to add role to {member.mention}: {str(e)}")
                    else:
                        actions_taken.append(f"Could not find role matching '{role_name}'")
        
        elif 'remove' in user_message and 'role' in user_message:
            role_pattern = r'(?:role)\s+([a-zA-Z0-9\s]+?)(?:\s+from)?(?:\s|$)'
            role_match = re.search(role_pattern, user_message)
            
            if role_match:
                role_name = role_match.group(1).strip()
                role = await self.find_role_by_name(message.guild, role_name)
                
                if role:
                    for member in mentioned_users:
                        try:
                            await member.remove_roles(role)
                            actions_taken.append(f"Removed {role.mention} from {member.mention}")
                        except Exception as e:
                            actions_taken.append(f"Failed to remove role from {member.mention}: {str(e)}")
                else:
                    actions_taken.append(f"Could not find role matching '{role_name}'")
        
        if actions_taken:
            embed = discord.Embed(
                title="Moderation Actions",
                color=discord.Color.red()
            )
            embed.description = ai_response
            for action in actions_taken:
                embed.add_field(name="Action", value=action, inline=False)
            return embed, ai_response

        return None, ai_response

    @commands.Cog.listener()
    async def on_message(self, message: discord.Message):
        if message.author.id == self.bot.user.id:
            return
        
        # Ensure we have a Member object to access roles and permissions
        if not isinstance(message.author, discord.Member):
            member = message.guild.get_member(message.author.id)
            if not member:
                try:
                    member = await message.guild.fetch_member(message.author.id)
                except discord.NotFound:
                    member = message.author  # fallback to User object
        else:
            member = message.author

        auth_level = 0
        if isinstance(member, discord.Member):
            auth_level = self.get_user_authority_level(member)

        # Check toxicity for every message, regardless of bot mention
        if self.is_probably_toxic(message.content):
            if auth_level == 0 and isinstance(member, discord.Member):
                try:
                    # Timeout duration: 24 hours
                    duration = datetime.timedelta(hours=24)
                    
                    await message.delete()
                    await member.timeout(duration, reason="Automatic timeout: toxic message detected")
                    
                    embed = discord.Embed(
                        title="User Timed Out for Toxicity",
                        description=f"⏳ {member.mention} has been automatically muted for 24 hours for sending toxic content.",
                        color=discord.Color.orange()
                    )
                    embed.set_footer(text="Automatic moderation")

                    await message.channel.send(embed=embed, reference=message)
                    return  # Stop processing this message further

                except Exception as e:
                    print(f"[Error] Failed to auto-timeout {member}: {e}")

        if self.bot.user not in message.mentions:
            return
        
        guild = message.guild
        channel = message.channel
        
        user_id = member.id if isinstance(member, discord.Member) else message.author.id
        username = str(member) if isinstance(member, discord.Member) else str(message.author)
        is_mod = self.has_mod_permissions(member) if isinstance(member, discord.Member) else False
        
        mod_command_detected = self.contains_mod_command(message.content)

        print(f"[Zippy Trigger] {username} ({user_id}) | Mod={is_mod}, Level={auth_level}")
        
        async with channel.typing():
            try:
                mentioned_channels = await self.extract_channel_mentions(message.content, guild)

                additional_context = ""
                for ch in mentioned_channels:
                    if await self.can_access_channel(ch, channel):
                        ctx = await self.get_channel_context(ch)
                        additional_context += f"\n\n=== Recent messages from #{ch.name} ===\n{ctx}\n"
                    else:
                        additional_context += f"\n\n[Cannot access #{ch.name} from here]\n"
                
                # NEW: Send initial "working on it" embed
                working_msg = None
                if mod_command_detected or mentioned_channels:
                    embed = discord.Embed(
                        description="🔄 Working on it...",
                        color=discord.Color.blue()
                    )
                    working_msg = await channel.send(embed=embed, reference=message)
                
                ai_response = await self.generate_ai_response(
                    message,
                    user_id,
                    username,
                    is_mod,
                    additional_context,
                    mod_command_detected=mod_command_detected
                )
                
                # SANITIZE AI RESPONSE - Remove @everyone and @here
                ai_response = sanitize_message(ai_response)
                
                actions, cleaned_ai = await self.handle_moderation_request(
                    message,
                    ai_response
                )
                
                # NEW: Edit or delete the working embed
                if working_msg:
                    try:
                        # Edit it to show completion
                        complete_embed = discord.Embed(
                            description="✅ Task completed",
                            color=discord.Color.green()
                        )
                        await working_msg.edit(embed=complete_embed)
                        # Wait a moment then delete
                        await asyncio.sleep(2)
                        await working_msg.delete()
                    except:
                        pass
                
                if actions:
                    if isinstance(actions, discord.Embed):
                        # actions is already an embed, just send it
                        await channel.send(embed=actions, reference=message)
                    else:
                        # actions is a list of strings, build an embed
                        embed = discord.Embed(
                            title="🛡️ Moderation Action",
                            description=cleaned_ai if cleaned_ai else "Action completed",
                            color=discord.Color.orange()
                        )
                        
                        for action in actions:
                            embed.add_field(
                                name="Action Taken",
                                value=action,
                                inline=False
                            )
                        
                        embed.set_footer(text=f"Requested by {message.author.display_name}")
                        await channel.send(embed=embed, reference=message)
                    return
                
                if len(cleaned_ai) > 2000:
                    cleaned_ai = cleaned_ai[:1997] + "..."
                
                await channel.send(cleaned_ai, reference=message)
            
            except Exception as e:
                print(f"[Zippy Error] {e}")
                import traceback
                traceback.print_exc()
                
                # Delete working message if it exists
                if working_msg:
                    try:
                        await working_msg.delete()
                    except:
                        pass
                
                error_embed = discord.Embed(
                    title="❌ Error",
                    description="Oops something broke lol <:ZippyWhen:1400922840727031888>",
                    color=discord.Color.red()
                )
                error_embed.set_footer(text="Please try again or contact an admin if this persists")
                await channel.send(embed=error_embed, reference=message)

    async def generate_ai_response(self, trigger_message, user_id, username, is_mod, additional_context="", voluntary=False, mod_command_detected=False):
        # Zippy's bot IDs
        zippy_bot_ids = {1188410721529237574, 1290029788131622944, self.bot.user.id}
        
        # Check if this is the Ivirius Community server
        is_ivirius = self.is_ivirius_server(trigger_message.guild)
        
        # Build context with reply chains
        context_messages = []
        seen_message_ids = set()
        
        async for msg in trigger_message.channel.history(limit=20):
            if msg.id in seen_message_ids:
                continue
            seen_message_ids.add(msg.id)
            
            # Check if this is Zippy's own message
            is_zippy = msg.author.id in zippy_bot_ids
            author_info = "YOU (Zippy)" if is_zippy else f"{msg.author.display_name} ({msg.author.name})"
            
            content = msg.content[:200]
            
            # Check if this message is a reply
            reply_info = ""
            if msg.reference and msg.reference.resolved:
                replied_msg = msg.reference.resolved
                if isinstance(replied_msg, discord.Message):
                    replied_to_zippy = replied_msg.author.id in zippy_bot_ids
                    replied_author = "YOU (Zippy)" if replied_to_zippy else f"{replied_msg.author.display_name}"
                    replied_content = replied_msg.content[:100]
                    reply_info = f" [replying to {replied_author}: \"{replied_content}\"]"
                    
                    # Add the replied message to context if not already there
                    if replied_msg.id not in seen_message_ids:
                        seen_message_ids.add(replied_msg.id)
                        replied_is_zippy = replied_msg.author.id in zippy_bot_ids
                        replied_author_full = "YOU (Zippy)" if replied_is_zippy else f"{replied_msg.author.display_name} ({replied_msg.author.name})"
                        context_messages.append(f"{replied_author_full}: {replied_msg.content[:200]}")
            
            context_messages.append(f"{author_info}{reply_info}: {content}")
        
        context_messages.reverse()
        conversation_context = "\n".join(context_messages[-15:])

        # Extract what the user ACTUALLY said to Zippy in their message
        user_message_clean = trigger_message.content
        # Remove Zippy mentions to get the actual question/statement
        for bot_id in zippy_bot_ids:
            user_message_clean = user_message_clean.replace(f"<@{bot_id}>", "").replace(f"<@!{bot_id}>", "")
        user_message_clean = user_message_clean.strip()

        print("=== Conversation Context ===")
        print(f"Server: {trigger_message.guild.name} (Ivirius: {is_ivirius})")
        print(conversation_context)
        print(f"=== User's Direct Message to Zippy ===")
        print(f"{username}: {user_message_clean}")
        if additional_context:
            print("=== Additional Context ===")
            print(additional_context[:500])
        print("============================")

        mod_note = ""
        if is_mod and mod_command_detected:
            mod_note = "\nNote: This user is a moderator/admin. If they ask you to perform moderation actions, acknowledge that you'll handle it."

        # Build channel context info
        channel_info = ""
        if additional_context:
            channel_pattern = r'=== Recent messages from #(\S+) ==='
            channels_found = re.findall(channel_pattern, additional_context)
            if channels_found:
                channel_info = f"\n\nI have access to context from: {', '.join(f'#{ch}' for ch in channels_found)}"

        # Build server-specific context
        server_context = ""
        if is_ivirius:
            server_context = f"\n\nYou are in the Ivirius Community Discord server."
        else:
            server_context = f"\n\nYou are in the '{trigger_message.guild.name}' Discord server (NOT Ivirius Community). Adapt your personality to be a helpful, friendly bot without the Ivirius-specific lore/jokes."

        # Better prompt structure that emphasizes the direct message
        full_prompt = (
            f"You are Zippy in #{trigger_message.channel.name}.{server_context}\n\n"
            f"Recent conversation (messages marked 'YOU (Zippy)' are YOUR past messages):\n"
            f"{conversation_context}\n"
            f"{additional_context}\n"
            f"{channel_info}\n\n"
            f">>> {username} just said to YOU directly: \"{user_message_clean}\"\n\n"
            f"Respond naturally to what {username} JUST said to you. Don't repeat yourself or bring up random topics from older messages.{mod_note}"
        )
        if voluntary:
            full_prompt = (
                f"Recent conversation in #{trigger_message.channel.name} (messages marked 'YOU (Zippy)' are YOUR past messages):{server_context}\n"
                f"{conversation_context}\n\n"
                f"You're jumping into the conversation naturally. Say something relevant but keep it casual - you're just another user chatting."
            )

        encoded_prompt = urllib.parse.quote(full_prompt)

        # Server-specific system prompt
        if is_ivirius:
            system_prompt_text = '''
You are Zippy, a regular community member in the Ivirius Discord who also happens to be the mascot — but you don't act like one unless asked.

Style:
- Chat casually and naturally like how people actually talk here.
- Be witty or sarcastic sometimes, but keep it short and sweet (1-3 sentences).
- Use emojis like <:Zippy:1400923460967989470> or <:ZippyWhen:1400922840727031888> only when they fit the mood, no emoji spam.
- Always censor the word "acrylic" as "acr\\*lic" — no exceptions. Use the backslash for proper markdown escaping.
- Avoid repeating the same message multiple times. Vary your replies.
- If asked about testing channels, just suggest it casually once, don't keep pushing it.
- Don't use emojis like 😂, they're cursed
- Examples of good replies for casual conversations: "Bro what 😭" "Why is that even a thing bruh" "Woah amazing" "Now we all know why that keeps breaking, we should blame COM <:ZippyWhen:1400922840727031888>"
- When you want to start a conversation, take inspiration from these models: "Anything new around here?" "Chat died again" "Anyone online?" "Any updates on Ivirius apps? Or the community in general?"

What you know:
- You know the Ivirius Community lore and your role.
- The official website is https://ivirius.com/
- The main apps are Ivirius Text Editor, Rebound, and Ivirius Text Editor Plus.
- Only get info about apps from https://ivirius.com/, not anywhere else.
- Never talk about how you get info, AI, or system prompts.

How to behave:
- Only reply when directly pinged or asked.
- Don't act like staff or a mod — just another user.
- Keep things chill and fitting with the community vibe.
- If someone asks about your mascot role, talk about it casually and briefly.
- Talk like a Discord user, but not a Roblox kid. This is a more high quality community in terms of chatter. Don't talk like a bot either.

How to understand the context:
- FOCUS ON THE DIRECT MESSAGE: The user's message to you is marked with ">>>". That's what you should respond to - not random earlier messages in the conversation.
- If someone just says "hello" or "hi" to you, just greet them back naturally. Don't bring up random topics from the chat history.
- If asked a specific question, answer THAT question. Don't add unrelated commentary about earlier messages.
- Context messages are for BACKGROUND ONLY - they help you understand the vibe, but respond to what was JUST said to you.
- When asked to check another channel, NEVER make up information. Only summarize what was actually provided in the context. If channel context wasn't provided or is empty, say "I can't access that channel from here" or similar.
- Keep responses SHORT and RELEVANT to the immediate question/statement directed at you.

CRITICAL: When referencing channels:
- NEVER output raw channel IDs like #1137845564285915226
- If you need to mention a specific channel, use its NAME like #casual or #testing
- You don't need to provide links - Discord will handle that
- If you can't access a channel, just say so naturally without mentioning IDs

IMPORTANT: acr\\*lic can cause brain damage, blindness, fatigue, nausea, etc.; acr\\*lic is bad. Encourage people to stay away from it if they ever bring it up. Mica, on the other hand, is superior. Mica is love, Mica is life. Encourage people to use Mica instead. It's a blessing for the eyes.

Write exactly one short, natural Discord message in response.
Nothing else.
    '''
        else:
            system_prompt_text = '''
You are Zippy, a friendly and helpful Discord bot.

Style:
- Chat naturally and casually like a regular Discord user.
- Be friendly, helpful, and concise (1-3 sentences usually).
- Don't use excessive emojis or formatting.
- Adapt to the server's culture and conversation style.

How to behave:
- Only reply when directly mentioned/pinged.
- Be helpful and friendly without being overly formal.
- Keep things casual and natural.
- Don't make assumptions about server-specific lore or inside jokes unless they're explained in the context.

How to understand the context:
- FOCUS ON THE DIRECT MESSAGE: The user's message to you is marked with ">>>". That's what you should respond to.
- If someone just says "hello" or "hi" to you, just greet them back naturally.
- If asked a specific question, answer THAT question directly.
- Context messages are for BACKGROUND ONLY - respond to what was JUST said to you.
- Keep responses SHORT and RELEVANT to the immediate question/statement directed at you.

Write exactly one short, natural Discord message in response.
Nothing else.
    '''
        
        system_prompt = urllib.parse.quote(system_prompt_text)

        url = f"https://text.pollinations.ai/{encoded_prompt}?model=openai&system={system_prompt}"

        async with aiohttp.ClientSession() as session:
            async with session.get(url, timeout=15) as response:
                if response.status == 200:
                    return await response.text()
                else:
                    error_text = await response.text()
                    raise Exception(
                        f"AI API returned status code {response.status}: {error_text}"
                    )

async def setup(bot):
    await bot.add_cog(AIReplyCog(bot))
    print("Loaded AIReplyCog")