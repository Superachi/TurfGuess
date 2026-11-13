# Deploying an update

How to put a new build on the server (for example Wispbyte) **without losing the game's save data or the bot token**.

## What lives where

| File / folder | What it is | On update |
|---|---|---|
| `turfguess.db` (plus `turfguess.db-shm` and `turfguess.db-wal` while the bot runs) | **The save data**: rounds, votes, points, bets, the weekly competition | **Never overwrite or delete.** |
| `images/` | Round and reveal images for past and current rounds | **Keep.** Missing images are regenerated, but the current round's images must stay in step with the database. |
| `.env` | The bot token and channel ID (server only) | **Leave alone.** Replacing it means entering the token again. |
| `appsettings.json` | Default settings, shipped in the zip | Replace, but see below. |
| `TurfGuess.Bot`, `*.dll`, `*.so`, `*.json` (other than `appsettings.json`) | The program itself | **Replace.** |
| `*.pdb` | Debug symbols | Optional. Replace or ignore. |

The database path and image folder come from `Database:Path` and `Images:Directory` and are relative to the folder the bot is started from. If you changed them on the server, keep the files where your settings point.

## The pipeline zip

The GitHub Actions pipeline ("Build & package") produces the program files only. It doesn't contain a database, images or `.env`, so **the zip can't overwrite your save data. Deleting files on the host can.**

The usual way to lose data is clearing out the hosted environment before uploading the new build ("select all, delete, upload"). That also deletes `turfguess.db`, `turfguess.db-wal` and `turfguess.db-shm`, `images/` and `.env`, and the bot then starts with an empty database and posts a first round as if it were new.

**When redeploying, don't delete:**
- `turfguess.db`, `turfguess.db-wal` and `turfguess.db-shm`. Delete these only for a deliberate reset ([situation 4](#4-you-want-to-reset-the-game-on-purpose)). The `-wal` file can hold recent votes that aren't in the main `.db` yet, so removing `.db` alone or the `-wal` / `-shm` files alone also loses data.
- `images/`
- `.env`

Delete or overwrite only the program files listed as "Replace" in the table above. Uploading the zip's contents over the existing files (overwrite on conflict) is enough; you rarely need to delete anything.

You get the zip from:
- **Tagged releases** (`v*` tags): attached to the GitHub release.
- **Pushes to `main` and pull requests**: the workflow run's **Artifacts** section (kept 30 days). GitHub wraps the artifact in a zip of its own, so unzip once to reach the files.

> Also make sure no local `turfguess.db` ends up in what you upload (for example from your own `dotnet publish` output). It would replace the live save data with a test database. The pipeline zip never has one.

## Which situation are you in?

### 1. Normal code update

Most updates. Settings and database layout didn't change.

1. Stop the bot.
2. Upload the zip's contents over the existing files. **Don't delete anything first**, in particular not `turfguess.db`, `turfguess.db-wal`, `turfguess.db-shm`, `images/` or `.env`.
3. Start the bot.

Optionally, **back up `turfguess.db` first**: with the bot stopped, copy the file somewhere safe. It's one file; do it before every update.

### 2. The update changes `appsettings.json`

The zip's `appsettings.json` has the defaults. If you changed settings on the server (round time, channel, game name, difficulty, etc.), copying the file over **resets them to the defaults**. Either:

- Keep your server's `appsettings.json`, and add the new settings from the zip by hand (compare the two files), or
- Copy the zip's file over, then re-apply your changes.

Better in the long run: put server-specific values in `TURFGUESS_*` environment variables or in `.env` (see below), so `appsettings.json` can always be replaced wholesale. Secrets never belong in `appsettings.json` anyway.

### 3. First deployment on a new server

Nothing exists yet, so copy everything from the zip. Then:

1. Create `.env` (or set the environment variables) with the token and channel; see below.
2. Start the bot. It creates `turfguess.db` and `images/` itself and posts the first round.

### 4. You want to reset the game on purpose

Stop the bot, delete `turfguess.db*` and the `images/` folder, then start it. All points and history are gone; a fresh first round is posted. Back up first if there's any chance you'll want the old data.

### 5. Moving the save data to a new server

Stop the bot on the old server, copy `turfguess.db` (and `-wal` / `-shm` files if present) and the `images/` folder to the new one, put the new build next to them and start it. Never run the same bot token on two servers at once.

## The `.env` file (Wispbyte and similar hosts)

On hosts like Wispbyte, the token is set once in a `.env` file in the app folder (or in the panel's variables). **Don't touch it when updating:**

- Don't upload a `.env` over it, and don't delete it when clearing out old files.
- The zip doesn't contain one, so a normal upload leaves it alone. Don't "wipe the folder, then upload", or you will have to enter the token again.
- If you do need to recreate it, the names are:

```
TURFGUESS_Discord__Token=YOUR_BOT_TOKEN
TURFGUESS_Discord__ChannelId=YOUR_CHANNEL_ID
```

Note the double underscore. See [Configure](USER_GUIDE.md#3-configure) for the full settings list.

## Checklist

- [ ] Bot stopped
- [ ] `turfguess.db` backed up
- [ ] Did **not** delete `turfguess.db`, `turfguess.db-wal`, `turfguess.db-shm`, `images/` or `.env` on the host
- [ ] No `*.db*` or `.env` file in the files you're uploading
- [ ] `appsettings.json` decision made (keep yours, or replace and re-apply changes)
- [ ] Bot started; the log shows it connected, with no startup errors about missing settings
