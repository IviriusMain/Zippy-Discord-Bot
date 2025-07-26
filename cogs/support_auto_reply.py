import discord
from discord.ext import commands

SUPPORT_CHANNEL_ID = 1217994528041074741  # your customer support channel

class SupportAutoResponder(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    @commands.Cog.listener()
    async def on_message(self, message: discord.Message):
        # Ignore the bot's own messages
        if message.author == self.bot.user:
            return

        # Only trigger in the support channel
        if message.channel.id != SUPPORT_CHANNEL_ID:
            return

        # Only trigger on webhook messages with embeds
        if not message.embeds or not message.webhook_id:
            return

        embed = message.embeds[0]
        support_content = embed.description or embed.title or "No content found."

        # Create a thread from the message
        thread = await message.create_thread(
            name="Customer Support",
            auto_archive_duration=60,  # archive after 1 hour of inactivity
        )

        # Optional: Show typing while generating AI reply
        async with thread.typing():
            try:
                # Generate AI reply (replace this function with your logic)
                ai_reply = await self.generate_ai_response(support_content)
                await thread.send(ai_reply)
            except Exception as e:
                await thread.send("Sorry, I couldn’t generate a response.")
                print(f"AI reply error: {e}")

    async def generate_ai_response(self, prompt):
        import urllib.parse
        import aiohttp

        # Add user context to prompt
        full_prompt = (
            f"{prompt}\n\n"
            "Reply as Ivirius Customer Support, an automated AI model for customer support in Ivirius Community. Reply with the usual 'Thank you for contacting us', 'We're sorry to hear you're experiencing issues with [X]', etc. If it's just spam, angry messages, or ragebait, reply **only** with: 'The user may be spamming or posting irrelevant content. This support form will be closed.'"
        )
        encoded_prompt = urllib.parse.quote(full_prompt)

        # Trimmed system prompt — focused on personality + app context
        system_prompt = urllib.parse.quote(
            """
Knowledge:
- Ivirius Community is a dev group crafting beautiful, fluent Windows apps since 2020.
- Ivirius Text Editor: A modern WordPad replacement with autosave, tabs, voice typing, dark mode.
- Ivirius Text Editor Plus: Adds AI writing, table tools, vertical tabs, homepage, themes.
- Rebound: A safe WinUI 3 mod platform replacing classic tools with modern alternatives.
"""
        )

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

# Setup to add cog to bot
async def setup(bot):
    await bot.add_cog(SupportAutoResponder(bot))