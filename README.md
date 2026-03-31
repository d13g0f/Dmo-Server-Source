# Digimon MMO Server – Systems Engineering and Server Architecture

This project began as a personal challenge to better understand how MMORPG servers work internally while revisiting a game from my adolescence.

Working on the server became an opportunity to explore real-time multiplayer architecture, server-authoritative gameplay systems, and the process of debugging large legacy codebases.

---

## Project Motivation

The project was driven by curiosity about how large multiplayer game systems operate behind the scenes.

By studying and modifying the server, the goal was to understand:

- how real-time game state is managed on the server
- how packet-driven gameplay logic is implemented
- how complex gameplay systems interact inside a persistent world

This project became a practical way to study MMORPG backend architecture and gameplay system design.

---

## Reverse Engineering the Gameplay Flow

One of the first challenges was identifying where specific gameplay actions were implemented in the codebase.

The initial development process consisted of mapping in-game actions to the server scripts responsible for executing them.

This required tracing gameplay flows through:

- packet processors
- gameplay managers
- character and digimon behavior models

Understanding which systems handled each action was essential before making any fixes or architectural improvements.

---

## Custom Debugging Infrastructure

Traditional console logging proved ineffective when debugging long gameplay sessions in a large MMO server.

To solve this, a **modular file-based logging system** was implemented where each gameplay subsystem writes logs into its own directory.

Example structure:
/logs/combat
/logs/hatch
/logs/skills
/logs/trade



This approach allowed long gameplay sessions to be analyzed retrospectively and helped identify subtle inconsistencies in combat systems, progression logic, and gameplay events.

---

## Tooling for Asset Iteration

One of the biggest development obstacles was the complicated asset pipeline required to modify gameplay data.

Adding a new item or skill required multiple steps:

1. editing the database
2. exporting intermediate data files
3. converting them with external tools
4. importing them into the client format

To simplify iteration, a tool was created that converts XML asset data into JSON so it can be loaded directly by the server.

This allowed gameplay assets such as skills and buffs to be edited and reloaded dynamically through an administrative command without modifying the database.

Although the system was planned to expand across additional modules, the project was eventually discontinued before full integration.

---

## Combat System Redesign

The original combat formulas contained several hardcoded values and inconsistencies.

Both **PvE and PvP damage formulas** were redesigned to better control power scaling between players and enemies.

The new formulas were designed to:

- reduce uncontrolled power creep
- normalize attribute and element scaling
- produce more consistent damage outcomes

While not official formulas, they provided a more balanced gameplay experience during testing.

---

## Discord Integration

A Discord bot was also integrated with the server to support administrative actions and player support tools.

Examples include:

- automated account creation through Discord
- administrative commands such as `!unstuck`

The `!unstuck` command executes server-side logic to safely relocate a character if they become stuck in an invalid map state.

---

## PvP Safety Improvements

One of the final systems implemented was a **PvP protection system** designed to prevent spawn-killing.

Features included:

- temporary spawn invulnerability
- PvP-specific damage calculation
- filtering of protected targets during attack processing
- safe fallback teleporting if PvP maps failed to load

Separating PvP damage logic from PvE calculations improved gameplay fairness and stability.

---

## Lessons Learned

Working on this project provided practical experience with:

- MMORPG server architecture
- packet-driven gameplay systems
- debugging large legacy codebases
- designing server-authoritative gameplay logic
- balancing combat systems
