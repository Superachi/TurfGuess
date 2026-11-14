# TurfGuess

## Slash commands

Whenever you add, rename or remove a slash command (`/turf …` or `/turf-admin …`), update the help command in the same change:

- `MessageText.Help` in `src/TurfGuess.Bot/Game/MessageText.cs` (admin commands go in the `isAdmin` section, player commands in the main list).
- The command tables in `docs/COMMANDS.md` and `docs/USER_GUIDE.md`.
- The test that covers the help text, if there is one.
