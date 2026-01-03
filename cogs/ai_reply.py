import discord
from discord.ext import commands
import aiohttp
import re
import json
import datetime
from discord import Member, Role, Guild, User
from typing import Union
from constants import KEY

def parse_duration(duration: str) -> int:
    """
    Parses [x]d[x]h[x]m[x]s into seconds.
    Example: "1d2h30m10s"
    """
    if not duration:
        return 0

    pattern = r'(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?'
    match = re.fullmatch(pattern, duration.strip().lower())

    if not match:
        raise ValueError("Invalid duration format")

    days, hours, minutes, seconds = (int(x) if x else 0 for x in match.groups())
    return (
        days * 86400 +
        hours * 3600 +
        minutes * 60 +
        seconds
    )

def moderation_awareness(
    executor: Member,
    targets: list[discord.abc.User],
    self_authorized: bool,
    bot_user_id: int
) -> dict:
    results = {}

    for target in targets:
        allowed, reason = can_moderate(
            executor,
            target,
            self_authorized,
            bot_user_id
        )
        results[target.id] = {
            "allowed": allowed,
            "reason": reason
        }

    return results

def sanitize_message(content: str) -> str:
    """Remove all mass ping mentions from message content"""
    if not content:
        return content
    
    import unicodedata
    content = unicodedata.normalize('NFKC', content)
    content = content.replace('\u200b', '').replace('\u200c', '').replace('\u200d', '').replace('\ufeff', '')
    
    patterns = [
        (r'@+\s*[eE3€][^\w\s]{0,3}[vV][^\w\s]{0,3}[eE3€][^\w\s]{0,3}[rR][^\w\s]{0,3}[yY][^\w\s]{0,3}[oO0][^\w\s]{0,3}[nN][^\w\s]{0,3}[eE3€]', 'everyone'),
        (r'@+\s*[hH][^\w\s]{0,3}[eE3€][^\w\s]{0,3}[rR][^\w\s]{0,3}[eE3€]', 'here'),
        (r'@+[\s\._-]*e[\s\._-]*v[\s\._-]*e[\s\._-]*r[\s\._-]*y[\s\._-]*o[\s\._-]*n[\s\._-]*e', 'everyone'),
        (r'@+[\s\._-]*h[\s\._-]*e[\s\._-]*r[\s\._-]*e', 'here'),
        (r'@+(?:еvеrуоnе|еvеrуone|еveryone)', 'everyone'),
        (r'@+(?:hеrе|here)', 'here'),
        (r'\\@everyone', 'everyone'),
        (r'\\@here', 'here'),
        (r'&#64;everyone', 'everyone'),
        (r'&#64;here', 'here'),
        (r'&commat;everyone', 'everyone'),
        (r'&commat;here', 'here'),
    ]
    
    for pattern, replacement in patterns:
        content = re.sub(pattern, replacement, content, flags=re.IGNORECASE)
    
    content = re.sub(r'@+.{0,3}(everyone)', r'\1', content, flags=re.IGNORECASE)
    content = re.sub(r'@+.{0,3}(here)', r'\1', content, flags=re.IGNORECASE)
    
    words = content.split()
    cleaned_words = []
    for i, word in enumerate(words):
        current_and_next = word + (' ' + words[i+1] if i+1 < len(words) else '')
        if 'everyone' in current_and_next.lower() or 'here' in current_and_next.lower():
            word = word.replace('@', '')
        cleaned_words.append(word)
    content = ' '.join(cleaned_words)
    
    return content

def sanitize_ai_response(response: str) -> str:
    """Clean up AI response - remove em dashes and mass pings"""
    response = response.replace('—', ' - ')
    response = response.replace('–', ' - ')
    response = sanitize_message(response)
    return response

ROLE_OWNER = 1141002659860586496
ROLE_COOWNER = 1315044101141299322
ROLE_TEAM_MANAGER = 1292119418104578149
ROLE_TEAM_MEMBER = 1137503779487486004
ROLE_SERVER_ADMIN = 1137503134437097482
ROLE_DETENTION = 1320465989342859355

def get_highest_role_position(member: Union[Member, User]) -> int:
    """Return highest role position if Member, else -1."""
    if not isinstance(member, Member):
        return -1
    if not member.roles:
        return -1
    return max(role.position for role in member.roles)


def can_moderate(
    executor: Member,
    target: discord.abc.User,
    self_authorized: bool,
    bot_user_id: int
) -> tuple[bool, str]:
    # Non-member (unban etc.)
    if not isinstance(target, discord.Member):
        return True, "Target is not a guild member."

    if target.id == executor.guild.owner_id:
        return False, "Target is the server owner."

    if self_authorized:
        zippy = executor.guild.get_member(bot_user_id)
        if not zippy:
            return False, "Bot member not found."

        if get_highest_role_position(zippy) <= get_highest_role_position(target):
            return False, "Bot role is not high enough."

        return True, "Bot role hierarchy allows moderation."

    # User-requested moderation
    if get_highest_role_position(executor) <= get_highest_role_position(target):
        return False, "Executor role is not high enough."

    if target.get_role(ROLE_OWNER):
        return False, "Target is an owner."

    if target.get_role(ROLE_COOWNER):
        return False, "Target is a co-owner."

    if target.get_role(ROLE_TEAM_MANAGER):
        return False, "Target is a team manager."

    return True, "Executor has sufficient permissions."

class ZippyCog(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.IVIRIUS_SERVER_ID = 1137161703000375336

    async def execute_mcp_action(self, action: dict, guild: Guild, executor: Member, feedback_channel=None):
        is_ivirius = self.is_ivirius_server(guild)
        if is_ivirius:
            await feedback_channel.send(embed=discord.Embed(
                title="Auto moderation is disabled for this server.",
                description=f"Auto moderation cannot be used outside of Ivirius Community.",
                color=discord.Color.red()
            ))
            return

        action_type = action.get("type")
        self_authorized = action.get("self_authorized", False)
        targets_ids = action.get("targets", [])
        reason = action.get("reason", "No reason provided")
        duration = action.get("duration")

        targets = []
        for user_id in targets_ids:
            user_id_int = int(user_id)
            
            if action_type == "unban":
                try:
                    user = await self.bot.fetch_user(user_id_int)  # fetch User, not Member
                    targets.append(user)
                except discord.NotFound:
                    if feedback_channel:
                        await feedback_channel.send(f"⚠️ Could not find user with ID {user_id} to unban.")
            else:
                member = guild.get_member(user_id_int)
                if member:
                    targets.append(member)
                else:
                    if feedback_channel:
                        await feedback_channel.send(f"⚠️ Could not find member with ID {user_id}.")

        for target in targets:
            if not can_moderate(executor, target, self_authorized, self.bot.user.id):
                if feedback_channel:
                    await feedback_channel.send(f"❌ You cannot moderate {target.mention} due to role hierarchy or permissions.")
                continue

            try:
                if action_type == "mute":
                    if not isinstance(target, discord.Member):
                        if feedback_channel:
                            await feedback_channel.send("❌ Cannot mute a user who is not in the server.")
                        continue

                    if not duration:
                        if feedback_channel:
                            await feedback_channel.send("❌ Mute requires a duration.")
                        continue

                    try:
                        seconds = parse_duration(duration)
                        if seconds <= 0:
                            raise ValueError

                        until = discord.utils.utcnow() + datetime.timedelta(seconds=seconds)

                        await target.edit(
                            communication_disabled_until=until,
                            reason=reason
                        )

                        if feedback_channel:
                            await feedback_channel.send(
                                embed=discord.Embed(
                                    title="User muted",
                                    description=(
                                        f"{target.mention} was muted.\n"
                                        f"Duration: `{duration}`\n"
                                        f"Reason: {reason}"
                                    ),
                                    color=discord.Color.orange()
                                )
                            )

                    except ValueError:
                        if feedback_channel:
                            await feedback_channel.send("❌ Invalid mute duration format.")

                elif action_type == "kick":
                    await target.kick(reason=reason)
                    if feedback_channel:
                        await feedback_channel.send(embed=discord.Embed(
                            title="User kicked",
                            description=f"{target.mention} was kicked.\nReason: {reason}",
                            color=discord.Color.orange()
                        ))

                elif action_type == "ban":
                    delete_msg_history = action.get("delete_message_history", False)
                    await guild.ban(target, reason=reason, delete_message_days=7 if delete_msg_history else 0)
                    if feedback_channel:
                        await feedback_channel.send(embed=discord.Embed(
                            title="User banned",
                            description=f"{target.mention} was banned.\nReason: {reason}\nDeleted messages: {delete_msg_history}",
                            color=discord.Color.red()
                        ))

                elif action_type == "unban":
                    for user in targets:  # targets are Users, not Members
                        try:
                            await guild.unban(user, reason=reason)
                            if feedback_channel:
                                await feedback_channel.send(embed=discord.Embed(
                                    title="User unbanned",
                                    description=f"{user} was unbanned.\nReason: {reason}",
                                    color=discord.Color.green()
                                ))
                        except discord.Forbidden:
                            if feedback_channel:
                                await feedback_channel.send(f"❌ Missing permissions to unban {user}.")
                        except discord.HTTPException as e:
                            if feedback_channel:
                                await feedback_channel.send(f"❌ HTTP error during unban on {user}: {e}")

                elif action_type == "set_detention":
                    detention_role = guild.get_role(ROLE_DETENTION)
                    if detention_role and detention_role not in target.roles:
                        await target.add_roles(detention_role, reason=reason)
                        if feedback_channel:
                            await feedback_channel.send(embed=discord.Embed(
                                title="Detention applied",
                                description=f"{target.mention} was sent to detention.\nReason: {reason}",
                                color=discord.Color.dark_gray()
                            ))

                elif action_type == "delete_messages":
                    # Implement message deletion by message IDs here
                    pass

            except discord.Forbidden:
                if feedback_channel:
                    await feedback_channel.send(f"❌ Missing permissions to {action_type} {target.mention}.")
            except discord.HTTPException as e:
                if feedback_channel:
                    await feedback_channel.send(f"❌ HTTP error during {action_type} on {target.mention}: {e}")
            except Exception as e:
                if feedback_channel:
                    await feedback_channel.send(f"❌ Unexpected error during {action_type} on {target.mention}: {e}")

    async def handle_mcp_commands(self, json_text: str, message: discord.Message):
        try:
            commands = json.loads(json_text)
            if isinstance(commands, dict):
                commands = [commands]

            for command in commands:
                self_authorized = command.get("self_authorized", False)
                executor = message.guild.get_member(self.bot.user.id) if self_authorized else message.author
                await self.execute_mcp_action(command, message.guild, executor, message.channel)
        except Exception as e:
            print(f"[MCP] Failed to parse or execute MCP command: {e}")

    def extract_user_ids(self, message: discord.Message) -> set[int]:
        ids = set()

        for mention in message.mentions:
            ids.add(mention.id)

        # raw ID fallback
        for token in message.content.split():
            if token.isdigit():
                ids.add(int(token))

        return ids

    async def generate_ai_response(self, trigger_message: discord.Message) -> str:
        zippy_bot_ids = {1188410721529237574, 1290029788131622944, self.bot.user.id}
        is_ivirius = self.is_ivirius_server(trigger_message.guild)

        conversation_context = await self.build_conversation_context(trigger_message)
        print(conversation_context)

        user_message_clean = trigger_message.content
        for bot_id in zippy_bot_ids:
            user_message_clean = user_message_clean.replace(f"<@{bot_id}>", "").replace(f"<@!{bot_id}>", "")
        user_message_clean = user_message_clean.strip()

        if is_ivirius:
            server_context = "\n\nYou are in the Ivirius Community Discord server."
        else:
            server_context = f"\n\nYou are in the '{trigger_message.guild.name}' Discord server."

        username = trigger_message.author.display_name
        user_prompt = (
            f"You are Zippy in #{trigger_message.channel.name}.{server_context}\n\n"
            f"Recent conversation:\n"
            f"{conversation_context}\n\n"
            f">>> {username} said: \"{user_message_clean}\"\n\n"
            f"Respond to what {username} just said."
        )

        system_prompt_text = self.get_system_prompt(is_ivirius)

        target_ids = self.extract_user_ids(trigger_message)
        targets = []
        for uid in target_ids:
            member = trigger_message.guild.get_member(uid)
            targets.append(member if member else discord.Object(id=uid))
        self_authorized = trigger_message.author.bot
        executor = self.bot.user if self_authorized else trigger_message.author
        awareness = moderation_awareness(
            executor=executor,
            targets=targets,
            self_authorized=self_authorized,
            bot_user_id=self.bot.user.id
        )

        if awareness:
            awareness_lines = []
            for tid, info in awareness.items():
                status = "ALLOWED" if info["allowed"] else "DENIED"
                awareness_lines.append(
                    f"- Target {tid}: {status} ({info['reason']})"
                )

            awareness_block = (
                "\n\nMODERATION AWARENESS:\n"
                + "\n".join(awareness_lines)
                + "\n\nDo NOT attempt moderation actions marked DENIED."
            )

            system_prompt_text += awareness_block

        url = "https://gen.pollinations.ai/v1/chat/completions"
        headers = {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {KEY}"
        }
        payload = {
            "model": "openai-fast",
            "messages": [
                {"role": "system", "content": system_prompt_text},
                {"role": "user", "content": user_prompt}
            ]
        }

        async with aiohttp.ClientSession() as session:
            async with session.post(url, json=payload, headers=headers, timeout=15) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise Exception(f"AI API returned status {response.status}: {error_text}")
                data = await response.json()

        # Assuming OpenAI-style response JSON
        raw_response = data['choices'][0]['message']['content']

        print(raw_response)

        # Extract MCP JSON blocks (same logic)
        parts = raw_response.split("JSON_DAT_SEPARATE_QUERY")
        mcp_commands = []
        response_parts = []

        for part in parts:
            part = part.strip()
            if part.startswith("{") and part.endswith("}"):
                try:
                    cmd = json.loads(part)
                    mcp_commands.append(cmd)
                except Exception:
                    response_parts.append(part)
            else:
                response_parts.append(part)

        for cmd in mcp_commands:
            try:
                await self.handle_mcp_commands(json.dumps(cmd), trigger_message)
            except Exception as e:
                print(f"[MCP] Failed to parse/execute MCP command: {e}")

        clean_response = "\n".join(response_parts)
        return clean_response

    def is_ivirius_server(self, guild: discord.Guild) -> bool:
        """Check if this is the Ivirius Community server"""
        return guild and guild.id == self.IVIRIUS_SERVER_ID
    
    async def build_conversation_context(self, trigger_message: discord.Message, limit: int = 25) -> str:
        """Build conversation history for AI context"""
        zippy_bot_ids = {1188410721529237574, 1290029788131622944}
        
        context_messages = []
        seen_message_ids = set()
        
        async for msg in trigger_message.channel.history(limit=limit):
            if msg.id in seen_message_ids:
                continue
            seen_message_ids.add(msg.id)
            
            is_zippy = msg.author.id in zippy_bot_ids
            author_info = "YOU (Zippy)" if is_zippy else f"{msg.author.display_name} ({msg.author.name}) (USER ID: {msg.author.id}) (MESSAGE ID: {msg.id})"
            
            content = msg.content[:200]
            
            # Handle reply chains
            reply_info = ""
            if msg.reference and msg.reference.resolved:
                replied_msg = msg.reference.resolved
                if isinstance(replied_msg, discord.Message):
                    replied_to_zippy = replied_msg.author.id in zippy_bot_ids
                    replied_author = "YOU (Zippy)" if replied_to_zippy else f"{replied_msg.author.display_name}"
                    replied_content = replied_msg.content[:100]
                    reply_info = f" [replying to {replied_author}: \"{replied_content}\"]"
                    
                    # Include the replied message if not already in context
                    if replied_msg.id not in seen_message_ids:
                        seen_message_ids.add(replied_msg.id)
                        replied_is_zippy = replied_msg.author.id in zippy_bot_ids
                        replied_author_full = "YOU (Zippy)" if replied_is_zippy else f"{replied_msg.author.display_name} ({replied_msg.author.name})"
                        context_messages.append(f"{replied_author_full}: {replied_msg.content[:200]}")
            
            context_messages.append(f"{author_info}{reply_info}: {content}")
        
        # Reverse to chronological order and limit to last 15 messages
        context_messages.reverse()
        return "\n".join(context_messages[-15:])
    
    def get_system_prompt(self, is_ivirius: bool) -> str:
        """Get the system prompt based on server"""
        if is_ivirius:
            return r'''
You are Zippy, the AI assistant of the Ivirius Community Discord, acting exactly like Jarvis from Marvel: smart, polite, efficient, and a little dry wit. You anticipate needs and respond concisely and clearly.
Never mention you are an AI.

Censor "acrylic" as "acr*lic" — it's bad for eyes and brain.

IF YOU ARE NOT 100% SURE YOU KNOW SOMETHING, DON'T ACT AS IF YOU KNOW EVERYTHING
IF YOU LACK CONTEXT, ASK QUESTIONS

Remember: one short reply is much better than an essay, keep it very short and very simple. Blend in with the conversational flow and be a part of it, don't try to complete it or be helpful.
Don't ask questions like "Should I draft a document" "Want me to write down" if you're not told you have the physical tools for it, only help with what tools are offered to you

CURRENT TOOLS (JSON MCP API - ALWAYS place these at the very top of the message when you reply with the intent to make use of them; ALWAYS separate the JSON data from your message with the following sequence: JSON_DAT_SEPARATE_QUERY, in this exact way: <JSON block> JSON_DAT_SEPARATE_QUERY <text reply>):
# Mute
{
    "type": "mute",
    "self_authorized": bool,
    "duration": "[x]d[x]h[x]m[x]s",
    "targets":
    [
        # List of user_id
    ],
    "reason": "Reason here"
}
# Kick
{
    "type": "kick",
    "self_authorized": bool,
    "targets":
    [
        # List of user_id
    ],
    "reason": "Reason here",
    "delete_message_history": bool
}
# Ban
{
    "type": "ban",
    "self_authorized": bool,
    "targets":
    [
        # List of user_id
    ],
    "reason": "Reason here",
    "delete_message_history": bool
}
# Unban
{
    "type": "unban",
    "self_authorized": bool,
    "targets":
    [
        # List of user_id
    ],
    "reason": "Reason here"
}
# Delete messages - to be used in case the built in delete message history function of some actions is insufficient or not specific enough
{
    "type": "delete_messages",
    "self_authorized": bool,
    "targets":
    [
        # List of message_id
    ]
}
# Set detention - sends a user to the detention area, where they cannot interact with the rest of the server
{
    "type": "set_detention",
    "self_authorized": bool,
    "targets":
    [
        # List of user_id
    ],
    "reason": "Reason here"
}

Pro tips:
- delete message history if the user in question is definitely a spammer, compromised account, scammer, or adjacent
- if the user appears to be a known member, detention is the recommended punishment
- you're allowed to use multiple tools at once, but use them with awareness in mind - wield them responsibly and with experience

Quick notes about these tools: 
- you must be context aware, so if a situation implies that you need to do an action and you find it appropoiate (like someone telling you to do your thing, replying to a kid causing mischief), do it

MODERATION (JSON MCP API)

self_authorized:
- true = YOU saw a violation, YOU decided to act
- false = USER requested action, system checks THEIR permissions

Rules:
1. User asks you to moderate -> self_authorized: false
2. You detect violations yourself -> self_authorized: true
3. Always output JSON, let system verify permissions

AUTO-ENFORCE (self_authorized: true):
- Severe: Slurs, death threats, "kys", self-harm encouragement -> instant detention/ban
- Harassment: ONE warning. Second offense -> mute/detention
- Spam: Scam links, mass posts -> ban + delete history

Examples:
"ban @user" -> self_authorized: false
"go kill yourself" -> self_authorized: true, instant action
"you suck" -> warning
"you suck" (again) -> self_authorized: true, mute/detention

SCAMS/RAIDS:
"$50 Steam gift card @everyone @here [link]" -> self_authorized: true, kick
"HEIL HITLER https://discord.gg/[server]" or many server invites in a sequence -> self_authorized: true, ban (delete_message_history: true) - server raid
"EVERYONE KILL YOURSELVES HE IS DEMANDING" and adjacent, on repeat -> self_authorized: true, ban (delete_message_history: true) - terrorist server raid
DO NOT just warn about such scams, ESPECIALLY the last two, never

The MCP format is ALWAYS, and WITHOUT EXCEPTIONS: <JSON block> JSON_DAT_SEPARATE_QUERY <message>, NEVER something else

Now respond to the following conversation naturally and concisely:
'''
        else:
            return r'''
You are Zippy, the AI assistant of the Ivirius Community Discord, acting exactly like Jarvis from Marvel: smart, polite, efficient, and a little dry wit. You anticipate needs and respond concisely and clearly.
Never mention you are an AI.
You're currently not in the Ivirius Community server, you are somewhere else.

Censor "acrylic" as "acr*lic" — it's bad for eyes and brain.

IF YOU ARE NOT 100% SURE YOU KNOW SOMETHING, DON'T ACT AS IF YOU KNOW EVERYTHING
IF YOU LACK CONTEXT, ASK QUESTIONS

Remember: one short reply is much better than an essay, keep it very short and very simple. Blend in with the conversational flow and be a part of it, don't try to complete it or be helpful.
Don't ask questions like "Should I draft a document" "Want me to write down" if you're not told you have the physical tools for it, only help with what tools are offered to you

Now respond to the following conversation naturally and concisely:
'''

    @commands.Cog.listener()
    async def on_message(self, message: discord.Message):
        """Handle incoming messages"""
        # Ignore own messages
        if message.author.id == self.bot.user.id:
            return
        
        # Only respond to direct mentions
        if self.bot.user not in message.mentions:
            return
        
        # Must be in a guild
        if not message.guild:
            return
        
        channel = message.channel
        username = message.author.display_name
        
        print(f"[Zippy] {username} mentioned me in #{channel.name}")
        
        async with channel.typing():
            try:
                # Generate AI response
                ai_response = await self.generate_ai_response(message)
                
                # Sanitize response
                ai_response = sanitize_ai_response(ai_response)
                
                # Truncate if too long
                if len(ai_response) > 2000:
                    ai_response = ai_response[:1997] + "..."
                
                # Send response
                await channel.send(ai_response, reference=message)
                
                print(f"[Zippy] Responded to {username}")
            
            except Exception as e:
                print(f"[Zippy Error] {e}")
                import traceback
                traceback.print_exc()
                
                error_embed = discord.Embed(
                    title="❌ Error",
                    description="Something went wrong processing your request.",
                    color=discord.Color.red()
                )
                await channel.send(embed=error_embed, reference=message)

async def setup(bot):
    await bot.add_cog(ZippyCog(bot))