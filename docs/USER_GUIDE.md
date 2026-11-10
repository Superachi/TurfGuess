# Turf Guess: User Guide

This guide covers what Turf Guess does, how to play, and how to use the tools that are available today. For how it works under the hood, see the [Developer Guide](DEVELOPER_GUIDE.md).

---

## Overview

**Turf Guess** is a daily guessing game for a Discord server.

Every day the bot posts a picture of an arena. Two teams of paint have flowed in from opposite sides, one from the left and one from the right, and fought over the middle. One color covers slightly more floor than the other. **Your job is to guess which color won.** You vote with a single button click. At the same time the next day, the bot reveals the exact percentages, lists who guessed right, and posts the next arena.

What makes it fun:
- **It's close.** The winning color covers only 50.3–52.3% of the painted floor, so you really have to look.
- **No two rounds are alike.** Every arena, paint pattern and color pair is generated from scratch.
- **It's quick.** One look, one click, once a day.

### Current status

| Part | Status |
|---|---|
| Arena and paint generator | ✅ Done |
| Round and reveal images | ✅ Done |
| **Preview tool** for generating sample images on your own computer | ✅ Done |
| The Discord bot (daily posts, voting, reveals, admin commands) | 🟡 Built and unit-tested, not yet tried on a live server |

[How to play](#how-to-play) is for players. [Running the bot](#running-the-bot) is for the server admin setting it up. [The Preview tool](#the-preview-tool) is for trying out rounds on your own computer.

---

## How to play

1. **Every day at the round time** (for example 09:00), the bot posts a new arena in the game channel, with two buttons, one per team color, e.g. `🩷 Pink` and `💚 Green`.
2. **Click the color you think covers more floor.** Only you see your vote: the bot confirms it with a message nobody else can see. The round message lists **who** has voted, but never what they picked, so you can't just follow the crowd.
3. **Changed your mind?** Click the other button. Your latest click counts, right up until the reveal.
4. **At the next round time**, voting closes and the bot posts the result:
   - the winning color,
   - each color's share of the floor, plus how much was left unpainted,
   - how many people voted for each color,
   - the names of everyone who guessed right. Names are shown without pinging anyone.
5. The next arena is posted straight away.

Each round runs from one round time to the next, which is normally 24 hours.

### Reading an arena

![A round image](images/round_example.png)

| What you see | What it is |
|---|---|
| Two colors of paint | The two teams. Each team's name is simply its color. |
| Grey floor | Unpainted floor. It still counts as floor, but for neither team. |
| Dark background | Out of bounds. It doesn't count. |
| Light grey blocks with a shadow | Obstacles. Paint can't go on or under them, and they don't count. |
| Striped rectangle at each end | The spawn pads where each team started. They don't count. |
| A mixed band in the middle | Where the teams fought. Both colors interleave here. |

**How the winner is decided:** only paintable floor counts. Obstacles, out of bounds and spawn pads are left out. The color with more painted floor wins.

**Tips:**
- The map is **symmetric** (rotated 180°), so neither team has a better side.
- Players rush forward, so the floor near each team's own spawn is often left **unpainted**, with stray splats and droplets around the edges. Don't let a big grey patch on one side fool you.
- Sometimes a team **painted its spawn diligently**, so its side is almost fully painted and the grey patches end up on the other side. A tidy spawn doesn't mean that team won.
- The middle is messy on purpose. Look at the overall area, not just the front line.

### Difficulty rank

The round message shows how hard the round is, e.g. *S-rank difficulty 🔥🔥🔥🔥*. The rank only says how close the two colors are, never which one is ahead.

| Rank | Shown as | Winner's share of the painted floor |
|---|---|---|
| **C** (easiest) | C-rank difficulty 🔥 | 51.9% or more |
| **B** | B-rank difficulty 🔥🔥 | 51.5% – 51.9% |
| **A** | A-rank difficulty 🔥🔥🔥 | 51.1% – 51.5% |
| **S** | S-rank difficulty 🔥🔥🔥🔥 | 50.7% – 51.1% |
| **X** (hardest) | X-rank difficulty 🔥🔥🔥🔥🔥 | under 50.7% |

With the default settings (50.3–52.3%), each rank comes up about one day in five. The bands are fixed, so if you change `Generator:Difficulty`, some ranks come up more often or not at all.

### The reveal

![A reveal image](images/reveal_example.png)

The reveal shows the same arena with a banner underneath:
- **"PINK WINS"**, or whichever color won.
- A **bar** split into the first color, unpainted floor and the second color, sized by how much floor each one covers.
- The exact percentages, e.g. *Pink 46.2% · 14.1% unpainted · Green 39.6%*.

The percentages are shares of the **whole floor**, unpainted included, so they don't add up to 100% between the two colors. The winner is still simply the color with the bigger number.

### Team colors

Each round uses one of three color pairs, picked at random:

| Pair | Colors |
|---|---|
| 🩷 Pink vs 💚 Green | `#E0559A` / `#2E7D32` |
| 🧡 Orange vs 💙 Blue | `#DE6624` / `#343BC4` |
| 💛 Yellow vs 💙 Blue | `#D0BE08` / `#3A0CCD` |

Which color starts on the left is random too.

---

## Running the bot

This section is for the server admin. You need the **.NET 10 SDK** and this repository on the computer that will run the bot. That computer has to stay on for the game to run.

### 1. Create the Discord bot

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications) and click **New Application**. Give it a name, for example "Turf Guess".
2. Open **Bot** in the left menu:
   - Click **Reset Token** and copy the token. **Treat it like a password:** anyone with it can control the bot. You'll paste it in step 3. Never put it in a file you share or commit.
   - No privileged intents are needed. Leave all three switches off.
3. Open **General Information** and copy the **Application ID**.

### 2. Invite it to your server

Open this link in a browser, with your Application ID filled in:

```
https://discord.com/oauth2/authorize?client_id=YOUR_APPLICATION_ID&scope=bot+applications.commands&permissions=117760
```

Pick your server and confirm. The permissions it asks for are View Channel, Send Messages, Embed Links, Attach Files and Read Message History. If the game channel is private, also add the bot to that channel.

To find the **channel ID**:
1. In Discord, open **User Settings → Advanced** and turn on **Developer Mode**.
2. Right-click the game channel and choose **Copy Channel ID**.

**Playing in a thread.** The game can also run in a thread, including a forum post. Right-click the thread, copy its ID the same way, and use that as the channel ID. Some things to set up:
- **Archiving.** Discord archives a thread after a period without new messages. The bot can't update the voter list or close the round in an archived thread. In the thread's settings, set **Hide After Inactivity** to **1 Week**, so the daily posts keep it open.
- **Permissions.** Posting in a thread needs **Send Messages in Threads**. The invite link doesn't ask for it, but most servers allow it for everyone. If the bot can't post, check this permission for the bot in the parent channel.
- **Private threads.** Add the bot to the thread, e.g. by mentioning it there.

### 3. Configure

The token never goes in `appsettings.json`. Store it as a user secret, which is kept outside the repository:

```powershell
cd D:\Documents\HobbyProjects\TurfGuess
dotnet user-secrets set "Discord:Token" "YOUR_BOT_TOKEN" --project src\TurfGuess.Bot
dotnet user-secrets set "Discord:ChannelId" "YOUR_CHANNEL_ID" --project src\TurfGuess.Bot
```

Alternatively, use environment variables, which is handy on a server:

```powershell
$env:TURFGUESS_Discord__Token = "YOUR_BOT_TOKEN"
$env:TURFGUESS_Discord__ChannelId = "YOUR_CHANNEL_ID"
```

The other settings are in [`src/TurfGuess.Bot/appsettings.json`](../src/TurfGuess.Bot/appsettings.json):

| Setting | Default | Meaning |
|---|---|---|
| `Game:RoundTime` | `09:00` | Time of day when the round is revealed and the next one posted |
| `Game:TimeZone` | `Europe/Amsterdam` | Time zone for the round time (daylight saving is handled) |
| `Game:Name` | `Judd's Judgment` | The game's name, shown in message titles, image captions and the reveal. Change it to anything you like. |
| `Game:ShowVoteCount` | `true` | List who has voted on the round message, e.g. "3 players have voted: @alice, @bob, @carol". Names only, never what they picked. Sorted by name, and at most 50 names, then "…and N more". |
| `Game:MaxListedWinners` | `50` | Most correct guessers named in the reveal |
| `Game:Ink` | `Marbled` | `Marbled`, `Flat` or `Shaded` |
| `Generator:Difficulty:MinWinnerShare` / `MaxWinnerShare` | `50.3` / `52.3` | How close rounds are, as the winner's % of the painted floor |
| `Database:Path` | `turfguess.db` | Where rounds and votes are stored |
| `Images:Directory` | `images` | Where round and reveal images are stored |

`Database:Path` and `Images:Directory` are relative to the folder you start the bot from.

### 4. Start it

```powershell
cd D:\Documents\HobbyProjects\TurfGuess\src\TurfGuess.Bot
dotnet run -c Release
```

On first start the bot:
1. connects and registers its admin commands in your server,
2. creates the database,
3. posts the first round straight away.

The first round is revealed at the next round time. If that's less than 6 hours away, it's revealed at the round time after that. Stop the bot with **Ctrl+C**.

If something's wrong with the settings, the bot won't start and lists every problem, for example a missing token or an unknown time zone.

**Downtime is fine.** If the bot was off when a round should have been revealed, it reveals that round as soon as it's back and posts a new one. That new round still ends at the normal round time, so the schedule never drifts. Clicks while the bot is offline fail with "This interaction failed". Players can simply click again once it's back. The bot never posts a round or reveal twice, even after a crash.

### Logs

The bot writes one line per event to its console, with a timestamp. When it runs as a service, the service manager collects these lines, e.g. `journalctl` on Linux. What it logs:

| Event | Example |
|---|---|
| A new round is created, **with the answer** | `Round 12: created, Pink vs Green, seed 4821…. Answer: Pink wins with 53.85% of the painted floor (coverage Pink 46.2%, Green 39.6%, 14.1% unpainted). Reveal at 2026-11-11 09:00 +01:00.` |
| The round is posted | `Round 12: posted in channel 1234… (message 5678…), Pink vs Green. Voting is open until 2026-11-11 09:00 +01:00.` |
| Someone votes | `Round 12: alice (1111…) voted Pink.` |
| Someone changes their vote | `Round 12: alice (1111…) changed their vote from Pink to Green.` |
| Someone clicks their current choice again | `Round 12: alice (1111…) voted Green again (no change).` |
| A click after voting closed | `Round 12: bob (2222…) clicked left after voting closed; rejected.` |
| Voting closes | `Round 12: voting closed with 11 votes.` |
| The reveal, with full stats | `Round 12: revealed (message 9999…). Pink wins with 53.85% of the painted floor. Coverage: Pink 46.2%, Green 39.6%, 14.1% unpainted. Votes: 11 (Pink 7, Green 4). Correct: 7/11 (64%): alice, carol, ….` |
| An admin command | `Admin dave (3333…) ran /turf-admin reveal-now; revealing the current round early.` |

The answer is in the log from the moment a round is created, so don't share the logs with players. The log level is set in `appsettings.json` under `Logging:LogLevel`.

### Admin commands

These slash commands are only visible to server administrators. Replies are only visible to you.

| Command | What it does |
|---|---|
| `/turf-admin status` | Shows the current round: its number, status, colors, reveal time and how many have voted |
| `/turf-admin reveal-now` | Reveals the current round right away and posts the next one. Useful for testing without waiting a day. |

### Preview the messages without Discord

To see exactly what the bot will post, without a token or a server:

```powershell
cd D:\Documents\HobbyProjects\TurfGuess
dotnet run --project src\TurfGuess.Bot -c Release -- --preview-messages
```

This builds a sample round with the real generator and prints every message for it:
1. the round post with its buttons,
2. the private vote confirmation,
3. the live list of who has voted,
4. the "voting is closed" reply,
5. the round post after it closes,
6. the reveal.

Mentions and timestamps are shown as Discord would display them.

| Option | What it does |
|---|---|
| `--seed <n>` | Use a different sample round (default `42`) |
| `--raw` | Show the exact markup sent to Discord (`<@id>` mentions, `<t:…>` timestamps) |

It uses your `appsettings.json`, so changes to the game name, round time, time zone or voter list setting show up in the preview.

---

## The Preview tool

The Preview tool generates sample rounds on your own computer, with no Discord involved. Use it to see what rounds look like, compare settings, or test your own guessing.

### Requirements

- The **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download)).
- This repository, checked out locally.

### Quick start

Open a terminal in the project folder (the one that contains `TurfGuess.slnx`).

**PowerShell:**
```powershell
cd D:\Documents\HobbyProjects\TurfGuess
dotnet run --project src\TurfGuess.Preview -c Release
```

**Bash / Git Bash:**
```bash
cd /d/Documents/HobbyProjects/TurfGuess
dotnet run --project src/TurfGuess.Preview -c Release
```

This generates 12 rounds and writes them to the `preview` folder:

| File | What it is |
|---|---|
| `round_<seed>.png` | The image the bot would post |
| `reveal_<seed>.png` | The matching reveal image |
| `contact_sheet.png` | All rounds side by side, labeled with seed, colors and the winning share |

It also prints a table in the terminal:

```
  seed try winner      target  actual  L cov  R cov    unp dil rank     ms
    42   1 Pink       53.85 % 53.85 % 46.2 % 39.6 % 14.1 %   -    C    639
```

| Column | Meaning |
|---|---|
| `seed` | The number the round was generated from. The same seed always gives the same round. |
| `try` | How many attempts the generator needed (normally 1). |
| `winner` | The winning color. |
| `target` / `actual` | The intended winning share of the painted floor, and the share actually counted. They always match. |
| `L cov` / `R cov` | Left and right team's share of the whole floor. |
| `unp` | Unpainted share of the floor. |
| `dil` | Which teams painted their own spawn diligently this round: `L`, `R`, `LR` or `-`. |
| `rank` | The [difficulty rank](#difficulty-rank), from `C` (easiest) to `X` (hardest). |
| `ms` | Time it took to generate the round. |

> **Tip:** to test your guessing, open `contact_sheet.png`, cover up the labels, guess each round, then check.

### All options

Add options after `--`, for example:
```powershell
dotnet run --project src\TurfGuess.Preview -c Release -- --count 6 --seed 100
```

**Generating rounds**

| Option | Default | What it does |
|---|---|---|
| `--count <n>` | `12` | How many rounds to generate |
| `--seed <n>` | `42` | Seed of the first round. Rounds use `seed`, `seed+1`, `seed+2`, … |
| `--out <folder>` | `preview` | Where the images go |
| `--no-reveal` | off | Skip the reveal images |
| `--columns <n>` | `4` | Columns in the contact sheet |

**Look**

| Option | Default | What it does |
|---|---|---|
| `--ink <style>` | `Marbled` | How the paint is drawn: `Marbled` (swirled, oil-slick bands), `Flat` (one solid color per team) or `Shaded` (lit, glossy ink) |
| `--radius <cells>` | `0.75` | How rounded the **outer** corners of the arena are. `0` = sharp, must be below `1` |
| `--inner-radius <cells>` | `0.5` | How rounded the **inner** corners are. `0` = sharp, must be below `1` |
| `--ppc <n>` | `16` | Pixels per map cell. Changes the image size (16 → 1536×864) |
| `--symmetry <mode>` | `Rotate` | `Rotate` (180°, the default) or `Mirror` (left-right mirror) |

**Color pair sheets**

| Option | What it does |
|---|---|
| `--pairs` | Instead of rounds, renders **one arena in every color pair** to `color_pairs.png`. Uses `--seed` for the arena. |
| `--pairs-file <file.json>` | Use your own list of color pairs instead of the built-in three |
| `--pairs-out <name.png>` | File name for the sheet (default `color_pairs.png`) |

A pairs file is a JSON list. Each pair has an `A` and a `B` color with a name, a hex color and an emoji:

```json
[
  { "A": { "Name": "Coral", "Color": "#FF6B6B", "Emoji": "🩷" },
    "B": { "Name": "Teal",  "Color": "#1FB6AA", "Emoji": "🩵" } }
]
```

### Examples

Flat colors with sharp corners:
```powershell
dotnet run --project src\TurfGuess.Preview -c Release -- --ink Flat --radius 0 --inner-radius 0
```

Every color pair on the same arena:
```powershell
dotnet run --project src\TurfGuess.Preview -c Release -- --pairs --seed 50
```

Try out new color pairs:
```powershell
dotnet run --project src\TurfGuess.Preview -c Release -- --pairs --pairs-file my_pairs.json --pairs-out my_pairs.png
```

A quick batch of 4 rounds without reveals, into a separate folder:
```powershell
dotnet run --project src\TurfGuess.Preview -c Release -- --count 4 --no-reveal --out preview\quick
```

---

## FAQ

**Does paint under obstacles count?**
No, and there isn't any. Obstacles are solid, so paint never reaches them, and they're left out of the floor entirely.

**Why don't the two percentages add up to 100%?**
They're shares of the whole floor, and some floor is unpainted. The reveal shows the unpainted share between them.

**Can there be a tie?**
No. The winner always has more than 50% of the painted floor.

**My click said "This interaction failed". What now?**
The bot was probably offline for a moment. Click again once it's back. Your vote only counts once the bot confirms it.

**Can other people see what I voted?**
No. Only you see the confirmation. The round message only shows how many people voted, never the split.

**Is it possible to cheat by counting pixels?**
Yes. It's a casual game, and cheating is out of scope.

**Why does the same seed always give the same round?**
Everything is generated from the seed, which makes rounds easy to reproduce and check.
