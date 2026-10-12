# IRONFRONT

**A real-time strategy game built with Unity, developed through an experiment in AI-led software engineering.**

Ironfront is a single-player RTS inspired by classic games such as *Command & Conquer*. Players establish military bases, gather resources, construct buildings, produce vehicles and engage in tactical warfare against autonomous computer opponents.

The project also explores a broader question:

**Can AI independently develop a credible, playable and enjoyable video game from human-defined requirements?**

Rather than writing the game's code manually, development is driven by AI coding agents working from structured milestone specifications. The human role centres on defining requirements, testing gameplay, evaluating quality and directing the project.

---

## Features

### Base Building & Economy
- Construct and manage military bases.
- Harvest resources to fund expansion and production.
- Build refineries, power plants, vehicle factories and research facilities.
- Manage production queues and economic priorities.

### Tactical Combat
- Control multiple units using familiar RTS selection and movement controls.
- Deploy light tanks, heavy tanks, reconnaissance vehicles and artillery.
- Issue movement, attack and attack-move commands.
- Use defensive structures to protect important locations.
- Research upgrades to improve military and economic capabilities.

### Autonomous Opponents
- Play against AI commanders with independent economies and military strategies.
- Opponents construct bases, harvest resources, produce armies and launch attacks.
- AI commanders scout, defend, expand and adapt their strategies.
- Multiple difficulty settings provide different levels of challenge without relying on artificial resource advantages.

### Maps & Exploration
- Four battlefields with distinct terrain and strategic challenges.
- Larger maps encourage exploration and territorial control.
- Multiple possible starting locations provide variety between matches.
- Fog of war makes reconnaissance and gathering intelligence important.
- Configurable skirmishes allow players to customise matches.

### Repairs & Strategic Expansion
*Planned or under development.*

- Repair damaged buildings.
- Construct Repair Pads to service vehicles using an automated queue.
- Sell unwanted buildings for a partial refund.
- Restrict construction to established base territories.
- Deploy Mobile Command Vehicles to establish forward bases.

---

## The AI Development Experiment

Ironfront is primarily an experiment in autonomous AI-assisted game development.

The development process follows a straightforward model:

1. **Human specification:** Gameplay requirements and acceptance criteria are defined.
2. **AI implementation:** The coding agent designs and implements the systems in Unity.
3. **Automated verification:** Compilation, unit tests, integration tests and AI-versus-AI simulations are performed.
4. **Human playtesting:** The game is played and assessed for usability, strategic depth and enjoyment.
5. **Iteration:** Issues and feedback are returned to the AI for further development.

The objective is to minimise direct human involvement in coding while retaining human oversight of design, quality and project direction.

This repository records the results, including development successes, technical difficulties and the limitations encountered.

### What We're Evaluating

- Can AI build and integrate complex gameplay systems?
- Can it maintain and extend an existing codebase?
- Can it debug problems independently?
- Can it create effective computer-controlled opponents?
- Can it balance game systems through simulation?
- Can it produce coherent visuals and user interfaces?
- Can it deliver a complete, distributable game?

The project is intended to evaluate these questions through practical development rather than isolated demonstrations.

---

## Development Progress

| Milestone | Description | Status |
|---|---|---|
| 001 | RTS camera, selection and movement | Complete |
| 002 | Base construction and resource economy | Complete |
| 003 | Combat, damage and destruction | Complete |
| 004 | Autonomous AI commander | Complete |
| 005 | Gameplay polish and performance | Complete |
| 006 | Visual identity and presentation | Complete |
| 007 | Additional units, research and fog of war | Complete |
| 008 | Multiple maps and configurable skirmishes | Complete |
| 009 | Building and vehicle repair systems | Complete |
| 010 | Construction territory, sales and forward bases | Complete |
| Release | Windows release candidate and independent testing | Planned |

The development plan may evolve as features are implemented and evaluated.

---

## Technology

| Technology | Purpose |
|---|---|
| Unity 6 | Game engine |
| C# | Gameplay programming |
| Universal Render Pipeline | Rendering |
| Unity AI Navigation | Pathfinding and navigation |
| Claude Code | Primary AI development agent |
| Unity Editor integration | AI interaction with the game project |
| Unity Test Framework | Automated testing |
| Git | Source control |

---

## Project Structure

The Unity project follows the standard Unity directory structure, with additional game-specific organisation and development documentation.

```text
Ironfront/
├── Assets/
│   └── ...
├── Packages/
├── ProjectSettings/
├── Docs/
├── CLAUDE.md
└── README.md
```

The exact structure may evolve as the AI development agent extends the project.

---

## Running the Project

### Requirements

- Windows development environment
- Unity Hub
- The Unity Editor version recorded in `ProjectSettings/ProjectVersion.txt`
- Compatible Unity project packages

### Getting Started

1. Clone the repository.
2. Open Unity Hub.
3. Select **Add project from disk**.
4. Select the cloned repository directory.
5. Open the project using its specified Unity Editor version.
6. Allow Unity to import the project's assets and packages.
7. Open the main game scene or start the game using the project's existing scene configuration.

Unity may need to rebuild its local project cache during the first import.

### Standalone Builds

A packaged Windows release is planned. Until a verified release is published, the Unity project is the primary means of running the game.

---

## Testing & Quality Assurance

Ironfront uses a combination of automated and human testing.

**Automated testing** covers appropriate gameplay rules, resource transactions, combat calculations, construction, AI decisions and other systems.

**AI-versus-AI simulations** are used to investigate match balance, strategic behaviour, starting-position advantages and potential gameplay deadlocks.

**Human playtesting** evaluates aspects that cannot be adequately assessed by automated tests, including responsiveness, usability, enjoyment and strategic decision-making.

Passing automated tests does not, by itself, establish that the game is enjoyable or ready for release.

---

## Development Philosophy

The project follows several principles:

- Prioritise functioning gameplay over feature quantity.
- Deliver features in small, independently testable milestones.
- Keep gameplay systems maintainable and reusable.
- Use data-driven configuration where appropriate.
- Require evidence before declaring features complete.
- Record errors, limitations and human intervention.
- Preserve existing functionality through regression testing.
- Give AI agents meaningful autonomy while retaining human oversight.

The project is not intended to recreate *Command & Conquer* directly. It draws inspiration from the genre while developing its own mechanics, assets and identity.

---

## Project Status

**Active development — pre-release**

Ironfront has progressed through its initial gameplay, combat, AI, visual presentation and map-development milestones.

The current focus is on completing additional base management and expansion mechanics before preparing a standalone Windows release candidate.

No final release date has been announced.

---

## Contributing

Ironfront is currently an AI-led development experiment rather than a conventional community-developed game.

Issues, bug reports and constructive gameplay feedback are welcome, subject to the repository's collaboration settings.

Development contributions may be considered as the project progresses.

---

## Licence

No open-source licence is granted unless a separate `LICENSE` file is added to this repository.

The game and its assets should not be redistributed or reused without permission.

Third-party components and assets remain subject to their respective licences.

---

*Ironfront — exploring how far AI can go in building a complete video game.*