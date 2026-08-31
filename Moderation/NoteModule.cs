using Discord;
using Discord.Interactions;
using System.Text.Json;

namespace Ivirius.Zippy.Moderation;

/// <summary>
/// Handles and keeps track of notes for a specific user.
/// </summary>
public class NoteModule : InteractionModuleBase<SocketInteractionContext>
{
    // Command declarations

    [SlashCommand("note", "Add a note for a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task NoteAsync([Summary("user")] IUser user, [Summary("note")] string note)
        => await ExecuteNoteAsync(user.Id.ToString(), note);

    [SlashCommand("note-bulk", "Add a note to one or more users by ID")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task NoteBulkAsync(
        [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds,
        [Summary("note")] string note)
        => await ExecuteNoteAsync(userIds, note);

    [SlashCommand("remove-note", "Remove a specific note from a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task RemoveNoteAsync(
        [Summary("user")] IUser user,
        [Summary("note_id", "The GUID of the note to remove")] string noteId)
        => await ExecuteRemoveNoteAsync(user, noteId);

    [SlashCommand("notes", "View all notes for a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task NotesAsync([Summary("user")] IUser user)
        => await ExecuteGetNotesAsync(user);

    [SlashCommand("clear-notes", "Clear all notes from a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task ClearNotesAsync([Summary("user", "The user to clear notes from")] IUser user)
        => await ExecuteClearNotesAsync(user);

    // Implementations

    public async Task ExecuteNoteAsync(string userIds, string reason)
    {
        await DeferAsync();

        var ids = userIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => ulong.TryParse(id, out var uid) ? uid : 0)
            .Where(id => id != 0)
            .ToList();

        if (ids.Count == 0)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("No valid user IDs were provided.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
        }

        var successLines = new List<string>();
        var failureLines = new List<string>();

        foreach (var userId in ids)
        {
            try
            {
                var guildUser = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, userId);
                if (guildUser is null)
                    throw new Exception("User not found in this server.");

                // Apply timeout
                NoteStorage.AddNote(Context.Guild.Id, userId, Context.User.Id, reason);

                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
            }
            catch (Exception ex)
            {
                var errorMsg =
                ex.Message.Contains("error 50013") ? "Missing permissions." :
                ex.Message;

                failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
            }
        }

        bool allFailed = successLines.Count == 0;
        bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

        var title = allFailed ? "Note Not Added"
            : partialFail ? "Note Partially Failed"
            : "Note Added";

        var color = allFailed ? Color.Parse("#d13a08")
            : partialFail ? Color.Parse("#e0a021")
            : Color.Parse("#66dea0");

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .AddField("Status", string.Join("\n", successLines.Concat(failureLines)), inline: false)
            .AddField("Reason", reason, inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteRemoveNoteAsync(IUser user, string noteId)
    {
        await DeferAsync();

        bool removed = NoteStorage.RemoveNote(Context.Guild.Id, user.Id, noteId);

        if (!removed)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("Note not found.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Note Removed")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Note `{noteId}` has been removed from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }


    public async Task ExecuteGetNotesAsync(IUser user, bool silent = true)
    {
        await DeferAsync(ephemeral: silent);

        var notes = NoteStorage.GetNotes(Context.Guild.Id, user.Id);

        if (notes == null || notes.Count == 0)
        {
                var emptyEmbed = new EmbedBuilder()
                .WithTitle($"Notes for {user.Username}")
                .WithDescription("No notes found for this user.")
                .WithColor(Color.Parse("#66dea0"))
                .Build();

            await FollowupAsync(embed: emptyEmbed, ephemeral: silent);
            return;
        }

        var chunkedNotes = notes.Chunk(10).Take(10);
        var embedsToSend = new List<Embed>();

        int page = 1;
        foreach (var chunk in chunkedNotes)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"Notes for {user.Username} (Page {page})")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"**Total notes:** {notes.Count}")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp();

            foreach (var note in chunk)
            {
                string safeReason = note.Reason.Length > 900
                ? $"{note.Reason[..900]}..."
                : note.Reason;

                embedBuilder.AddField(
                    $"`{note.Id}`",
                    $"**Moderator:** <@{note.ModeratorId}>\n**Timestamp:** <t:{((DateTimeOffset)note.Timestamp).ToUnixTimeSeconds()}:R>\n```\n{safeReason}\n```",
                    true
                );
            }

            embedsToSend.Add(embedBuilder.Build());
            page++;
        }

        await FollowupAsync(embeds: embedsToSend.ToArray(), ephemeral: silent);
    }

    public async Task ExecuteClearNotesAsync(IUser user)
    {
        await DeferAsync();

        int count = NoteStorage.ClearNotes(Context.Guild.Id, user.Id);

        var embed = new EmbedBuilder()
            .WithTitle("Notes Cleared")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Cleared **{count} note{(count != 1 ? "s" : "")}** from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }
}

// Note model
public class NoteEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Reason { get; set; } = string.Empty;
    public ulong ModeratorId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// JSON storage helper
public static class NoteStorage
{
    private static readonly string DataDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                                          "Zippy-Discord-Bot",
                                                                          "data");
    private static readonly string Path = System.IO.Path.Combine(DataDirectory, "notes.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, Dictionary<string, List<NoteEntry>>> Load()
    {
        if (!File.Exists(Path))
            return [];

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<NoteEntry>>>>(json) ?? [];
    }

    private static void Save(Dictionary<string, Dictionary<string, List<NoteEntry>>> data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
    }

    public static List<NoteEntry> GetNotes(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();
        return data.TryGetValue(gKey, out var guild) && guild.TryGetValue(uKey, out var notes) ? notes : [];
    }

    public static NoteEntry AddNote(ulong guildId, ulong userId, ulong moderatorId, string reason)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.ContainsKey(gKey)) data[gKey] = [];
        if (!data[gKey].ContainsKey(uKey)) data[gKey][uKey] = [];

        var entry = new NoteEntry { Reason = reason, ModeratorId = moderatorId };
        data[gKey][uKey].Add(entry);
        Save(data);
        return entry;
    }

    public static bool RemoveNote(ulong guildId, ulong userId, string noteId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var notes))
            return false;

        var entry = notes.FirstOrDefault(w => w.Id == noteId);
        if (entry is null) return false;

        notes.Remove(entry);
        Save(data);
        return true;
    }

    public static int ClearNotes(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var notes))
            return 0;

        int count = notes.Count;
        notes.Clear();
        Save(data);
        return count;
    }
}
