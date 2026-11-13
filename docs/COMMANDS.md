# Player commands

Everything a player can do, by slash command and by button. Admin commands (`/turf-admin …`) are in the [user guide](USER_GUIDE.md).

## Slash commands

| Command | Where | Who | What it does |
|---|---|---|---|
| `/turf battle [show-votes:true]` | Game channel only | Anyone | Starts a 🔫 Regular Battle: a short round (`Game:BattleLength`, 15 minutes by default) that is never scored. With `show-votes`, the message lists who voted for which team and voters are told their vote is public. Only one battle runs at a time; while one is open the command just says so. If today's battle message already exists, it is reset into the new battle instead of posting another message, and you get a private link to it. |
| `/turf battle-reveal` | Game channel only | The player who started the battle, or an admin | Reveals the running Regular Battle now instead of waiting for the timer. |
| `/turf standings` | Any channel | Anyone | Shows this week's standings: place, points and wins, with 🏆 for the leader(s). |
| `/turf all-time` | Any channel | Anyone | Shows everyone who has played: weekly 🏆 trophies, and the ⚓ ranked and 🔫 regular battle record as wins–losses and win rate. Counts every revealed round, a win being a correct guess. |
| `/turf subscribe` | Any channel | Anyone | Turns the result DMs back on after you opted out. They are on by default (see [below](#result-messages-turf-subscribe)). The bot sends a test DM straight away and warns you if it can't reach you. |
| `/turf unsubscribe` | Any channel | Anyone | Opts you out of the result DMs. |

Replies to all of these are ephemeral: only you see them.

## Buttons on round messages

| Button | On | What it does |
|---|---|---|
| Team buttons (🔴 / 🔵 …) | Ranked and regular rounds | Cast or change your vote until the reveal. Only you see the confirmation, so nobody learns how you voted (except in a `show-votes` battle). |
| 🎲 Bet | Ranked rounds only | Toggles your bet: a correct guess earns double points, a wrong one loses the round's points (your weekly score never drops below 0). A bet only counts if you also voted. If you have 0 points this week, the bet is switched on automatically the first time you vote, since you have nothing to lose. |
| Reveal | Running regular battle | Same as `/turf battle-reveal`: the starter or an admin reveals it early. |
| 🔁 Reset | Finished regular battle | Turns the message into a fresh battle. If another battle is already running, the button becomes a Reveal button for it. |

## Result messages (`/turf subscribe`)

By default the bot DMs you a small card after every round you voted in (opt out with `/turf unsubscribe`). It is colored like the winning team and links to the result in the server:

```
⚓ Ranked Battle · Round #12
✅ You guessed right!
🔴 Reds won with 41.2% vs. 🔵 Blues 38.0%
🎲 Bet on · +6 points
[Go to the result in the server]
---
To stop these messages, use /turf unsubscribe in the server.
```

Ranked rounds also show whether you bet and the points you gained or lost (a bet that lost nothing, or a wrong guess without a bet, shows "no change in points"). Regular battles are not scored, so they have no points line.

### Events that send you a DM

You must not have opted out, and a DM is sent when:

1. **You subscribe again after opting out.** One test message to confirm DMs get through.
2. **A ⚓ Ranked Battle you voted in is revealed.** This covers the normal daily reveal, an admin's `/turf-admin reveal-now`, and a late reveal after the bot was down (the DM arrives when the reveal is finally posted).
3. **A 🔫 Regular Battle you voted in is revealed.** This covers the timer running out and an early reveal by `/turf battle-reveal` or the Reveal button.

### Events that do not

- A round you did not vote in. Bets without a vote don't count.
- A round an admin cancelled with `/turf-admin reset-round`. It is never revealed or scored.
- Votes cast or changed while a round is open, a round being posted, and the weekly summary.
- Anything while you are opted out. Rounds revealed in that time are not sent later.

If your DMs are closed, the bot logs it and skips you; the reveal itself is never held up. Allow direct messages from server members (Server Settings → Privacy Settings) to receive them. Delivery is at most once: if the bot crashes in the instant after marking a round done, that round's DMs are lost rather than sent twice.
