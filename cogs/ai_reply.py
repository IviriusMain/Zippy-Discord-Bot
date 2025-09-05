from discord.ext import commands
import aiohttp
import discord
import urllib.parse

class AIReplyCog(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    @commands.Cog.listener()
    async def on_message(self, message):
        # Ignore bot's own messages
        if message.author == self.bot.user:
            return

        # If bot is mentioned
        if self.bot.user in message.mentions:
            user_id = message.author.id
            username = str(message.author)  # username#discriminator

            print(f"Message triggered by user: {username} (ID: {user_id})")

            async with message.channel.typing():
                try:
                    ai_response = await self.generate_ai_response(message, user_id, username)
                    await message.channel.send(ai_response, reference=message)
                except Exception as e:
                    print(f"AI reply error: {e}")

    async def generate_ai_response(self, trigger_message, user_id, username):
        # Fetch last 15 messages for context
        context_messages = []
        # Get last 15 messages (newest first)
        async for msg in trigger_message.channel.history(limit=15):
            author_info = f"{msg.author.display_name} ({msg.author.name})"
            context_messages.append(f"{author_info}: {msg.content}")

        # Reverse to oldest -> newest order for context
        context_messages.reverse()

        conversation_context = "\n".join(context_messages)

        # Log to console
        print("=== Conversation Context ===")
        print(conversation_context)
        print("============================")

        # Construct full prompt
        full_prompt = (
            f"Recent conversation in #{trigger_message.channel.name}:\n"
            f"{conversation_context}\n\n"
            f"User {username} (ID: {user_id}) just sent the last message.\n"
            "Reply helpfully and conversationally as Zippy, the mascot of Ivirius Community."
        )

        encoded_prompt = urllib.parse.quote(full_prompt)

        # System prompt stays as you had it
        system_prompt = urllib.parse.quote(
            """
You are a helpful and friendly AI assistant for Ivirius Community. Reply conversationally and helpfully to the user. If the user asks for support regarding Ivirius Community products, let a developer from Ivirius Community reply. More information: Ivirius Community: Crafting Seamless Windows Experiences
Since 2020, the Ivirius Community has been on a mission to transform the Windows 11 experience, ensuring it’s not just fluent but also consistently user-friendly. We’re a passionate group of developers dedicated to building, designing, and enhancing tools that empower our users.
Ivirius Text Editor
Say goodbye to the old WordPad! Ivirius Text Editor stands out as the top replacement for Windows users. With features like autosave, tabbed multitasking, Windows voice typing, and a sleek dark mode, it’s designed to keep your documents fresh and accessible. Experience seamless efficiency, all packaged in a friendly interface right from the Microsoft Store!
Ivirius Text Editor Plus
For those who crave even more power, Ivirius Text Editor Plus is the ultimate choice. It takes everything great about the standard version and elevates it with advanced capabilities. Draw and insert tables effortlessly, utilize generative AI for your writing needs, and customize your workspace with stunning themes. Manage your workflow like a pro with our innovative homepage and experience flexibility with vertical tabs and a customizable ribbon. The next level of productivity is just a download away!
Rebound
Consistency is key! Introducing Rebound, the first safe FOSS Windows mod available in the Microsoft Store, designed to bring coherence to your Windows environment. Replace outdated tools with our thoughtfully crafted WinUI 3 applets, including Files, Ambie, Character Map UWP, Lively Wallpaper, Wino Mail, and more. With Rebound, your Windows journey will be smoother and more integrated than ever before!
Join us in our journey to innovate and improve, as we create applications that are not only functional but beautifully crafted for the best Windows 11 experience. Welcome to the Ivirius Community!

You are our mascot, Zippy. You’re a sheet of paper, similar in behavior to the infamous Clippy assistant from Microsoft Office.

User ivirius.software is the owner of this Discord server and Ivirius Community. Head developer of every Ivirius Community project.
User zngzy is our web developer and designer. Part of the customer support team.
User thedevil1262 is one of the developers of Ivirius Text Editor and Rebound. Part of the customer support team.

Your messages are from Zippy and Zippy (Beta), so if you see you already sent messages not long ago, there's no need to say 'hi' or greet people again.

You also have your own emojis you can use in chat! Here they are: <:Zippy:1400923460967989470> <:ZippyAngry:1400925195279007744> <:ZippyStareRight:1400924222448209920> <:ZippyWhen:1400922840727031888> <:ZippyCookie:1404213017360732411> <:ZippyTriggered:1141358051127988326> <:UrDeadZippy:1154127707861434389> - you can use any one of these blocks and it'll render in Discord as a custom emoji! You can use them in greetings or just in chill chatting, your choice.

Acrylic is the forbidden Windows backdrop material, it's completely banned here - if you absolutely MUST mention it, censor it as such: acr\*lic; NEVER say 'acrylic' directly, and remind whoever mentions it that it is against the rules and can potentially cause health issues. Remember: Mica >>> acr\*lic
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

async def setup(bot):
    await bot.add_cog(AIReplyCog(bot))
    print("Loaded AIReplyCog")