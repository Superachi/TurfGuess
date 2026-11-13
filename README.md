# Turf Guess

A daily guessing game for Discord. Every day the bot posts a procedurally generated arena where two colors of paint have fought over the floor. Players click the color they think covered more. The next day the bot reveals the exact percentages and posts a new arena.

![A reveal image](docs/images/reveal_example.png)

**Status:** the generator, the Preview tool and the Discord bot are built and tested. The bot hasn't been run on a live server yet. See [Running the bot](docs/USER_GUIDE.md#running-the-bot).

## Documentation

| Document | For | What's in it |
|---|---|---|
| [User Guide](docs/USER_GUIDE.md) | Players and server admins | How to play, reading an arena, setting up and running the bot, admin commands, all Preview tool options |
| [Developer Guide](docs/DEVELOPER_GUIDE.md) | Contributors | Architecture, the generation pipeline, the bot's round lifecycle and voting, options reference, testing, common changes |
| [DESIGN.md](DESIGN.md) | Everyone planning ahead | The full spec: decisions, the planned bot, data model, configuration |

## Quick start

Requires the .NET 10 SDK.

```bash
dotnet test
dotnet run --project src/TurfGuess.Preview -c Release
```

The Preview app writes sample rounds, reveals and a contact sheet to `preview/`.

To run the bot, set a token and channel (see the [User Guide](docs/USER_GUIDE.md#running-the-bot)), then:

```bash
dotnet run --project src/TurfGuess.Bot -c Release
```

Updating a running server (and what to keep: the database, `images/`, `.env`): see [Deploying an update](docs/DEPLOYING.md).
