# Weekly competition (design draft)

Status: implemented on this branch (not yet merged). Trophies are tallied but not displayed. Branch: `weekly-competition`.

## Rules

- A week runs Monday to Monday. The week boundary uses a time zone set in config (e.g. `Game:TimeZone`).
- Each round has a rank, C to X, worth 1 to 5 points (`Points = (int)rank + 1`).
- Every player who votes is **participating**. Nothing is ever lost without a bet.
- Each voter can toggle a **🎲 Bet** on the round at any time until the reveal. It only counts if they voted.

| Outcome        | No bet  | Bet         |
|----------------|---------|-------------|
| Correct guess  | +points | +2 × points |
| Wrong guess    | 0 (~)   | −points     |

- A player's weekly score never goes below 0. A loss is capped at the current score.
- A **win** is a correct guess. Wins are tallied per player per week.
- Weekly winner: highest points. Tie: most wins. Still tied: everyone tied is a winner.
- Trophies (weeks won) are tallied and stored, but **not displayed yet**.

## Messages

### Round message (voting open)

```
Which team do you think painted more? Cast your vote!

Rank A | 🔥🔥🔥
Worth 3 points · bet: win +6 / lose −3
Results in 2 hours (Fri 18:00).

3 players have voted: 🎲@Ann, @Bo, 🎲@Cy
```

A 🎲 before a name means that player is betting. Only who voted and who bets is shown, never the side.
Buttons: the two team votes plus a 🎲 Bet toggle.

### Reveal

```
## 🔴 Red wins!
Red 51.0% · Blue 49.0% · 3% unpainted
Votes: 🔴 4 · 🔵 3

Winners (4): 🎲@Ann (+6), @Bo (+3), 🎲@Dee (+6), @Eli (+3)
Losers (1): 🎲@Cy (−3), @SafePlayer (~)

Weekly standings
1. @Ann 14 pts · 5 wins
2. @Dee 11 pts · 4 wins
3. @Bo 9 pts · 5 wins
```

- The standings leader keeps the 🏆 marker in the mockup for now; whether it stays is open since trophies are not displayed yet.
- Edge cases: no winners, no losers, nobody voted. The Winners and Losers lines are left out when empty.
- Both lists are cut off at the existing `maxListed` and message limit ("…and N more").
- A bet loss that hits the floor shows the capped amount, e.g. `🎲@Cy (−1)` when only 1 point was left.

### Weekly rollover (Monday)

Posted right after the first reveal of the new week (the reveal on Monday of the round that started Sunday). The week boundary is Monday 00:00 in the configured time zone, and that reveal is the first one scored in the new week.

```
🏁 Week 46 is over!
🏆 @Ann wins with 41 pts and 17 wins.
A new week starts now.
```

Ties name every tied winner.

## Open for implementation

- Storage: per-round `bet` flag on votes, per-round points delta, per-week totals, a trophy tally.
- Standings show the top 3. A public leaderboard command for more is a later addition.
- A bet loss at the floor shows the capped amount, e.g. `(−1)`; a player already at 0 shows `(~)`.
