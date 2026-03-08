# Show Incoming Damage

A mod for Slay the Spire 2 that displays predicted incoming damage for all players during combat.

## Features

- Displays predicted net damage next to each player's health bar (character and multiplayer player list)
- Color-coded: red for incoming HP loss, green when fully blocked, orange for pet damage
- Hover over the damage number to see a detailed breakdown panel:
  - Block sources: current block, power block gains (e.g. Metallicize), orb block gains (e.g. Frost), each with running total
  - Damage sources: end-of-turn card damage, debuff damage, enemy attacks with per-hit calculation
  - Shows block consumption, pet absorption, and actual HP loss per step
  - Enemy attack icons reflect actual damage tier (matching in-game intent icons)
  - All names and keywords use in-game localization
- Damage prediction accounts for block gains, pet HP absorption, and multi-hit attacks
- Auto-updates while hovering when game state changes
- Uses IL analysis to detect which powers/cards/orbs deal damage or grant block, with result caching

## Known Issues

- Mods that add multi-hit effects to powers/cards may not be correctly accounted for, as IL static analysis cannot reliably detect loop-based repeated calls
- Some runtime-dynamic values (e.g. damage that changes based on game state during resolution) may not be accurately predicted
