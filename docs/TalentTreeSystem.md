# Talent Tree System — Design & Phased Plan

> Status: **DRAFT / Phase 0** — design freeze in progress. Not yet implemented.
> Companion to the existing S.P.E.C.I.A.L. system (`Content.Shared/_Misfits/Special`).
> All future code changes must follow the `// #Cythisiax ...` annotation convention
> (see `misfits-global.instructions.md`).

---

## 1. Overview

Add a Fallout-style **talent tree** to the misfits-14 character setup, layered on top of the
existing S.P.E.C.I.A.L. system. Players get **10 free talent points** at character creation
(no leveling for now) and spend them across **4 talent builds**:

| Build | Theme | Primary SPECIAL | Playstyle |
|-------|-------|-----------------|-----------|
| **Melee** | Wasteland Brawler | STR · END · AGI | Close combat, unarmed, thrown |
| **Ranged** | Gunslinger | PER · AGI · LUCK | Guns, rifles, heavy weapons, crits |
| **Misc** | Wasteland Scholar | INT · CHA · LUCK | Crafting, trading, chems, luck |
| **Survival** | Wasteland Survivor | END · PER · LUCK | Scavenging, needs, radiation, resilience |

Each build is a **10-tier ladder** (Tier 1 → Tier 10), **one talent per tier** = **40 talents total**.
Within a build you must own Tier *N−1* before buying Tier *N* (like climbing a branch on the
Fallout 4 perk poster). You may spend points across multiple builds; 10 points means you can
**fully complete one build** or go partial in several — a real build-identity choice.

**Non-goals (v1):** no XP/leveling gating, no respecs, no talent *ranks* (each talent is a
flat one-time purchase), no in-round re-spending.

---

## 2. How Fallout does talents — reference

What we're borrowing (and what we're deliberately simplifying):

| Fallout concept | Game examples | How it maps here |
|-----------------|---------------|------------------|
| **SPECIAL attributes as gates** | FO1/2/NV perks require stat minimums | Not required as hard gates (SPECIAL already drives effects on its own). Talents are *additive* bonuses on top. |
| **Tiered unlocks by level** | FO3 perk chart (levels 2–30); FO4 poster ranks gated by level+stat | Simplified to **tier ladders** (buy T1 → T2 → …) since we have no leveling. |
| **Branch/prerequisite chains** | FO4 7 stat posters, Dragonflight trees | **Ladder prerequisite**: within a build, Tier N needs Tier N−1. No cross-build prereqs in v1. |
| **Point budget / pacing** | FO1/2 40-point pool; FO4 perk ranks | **10 free points**, no generation. Fills 1 tree fully or spreads thin. |
| **Niche perks** | Lead Belly, Light Step, Scrounger, Fortune Finder, Jury Rigging, Life Giver, etc. | Kept as named talents (flavor + mechanical hook). |
| **Capstone keystones** | FO4 poster capstones, WoW keystone nodes | **Tier 10 capstone** per build — the identity payoff. |
| **Ranks (e.g., Educated 1–3)** | FO1/2, FO76 perk cards | **Dropped in v1** — each talent is a single purchase. (Future: ranks.) |
| **INT → more points** | FO: INT grants skill points/level | **Flagged as future work** — INT 8/10 could grant +1/+2 talent points, tying the two systems together without a leveling system. |

Design philosophy for balance (mirrors the SPECIAL README): *SS14 combat is real-time and
rounds are short — initial effects are deliberately small, and most values live in YAML tuning
so they can be rebalanced without code changes.*

### 2.1 Existing perks (Traits) — the no-duplicate constraint

The "perks" players already pick in character setup are the SS14 **Traits** system
(`Resources/Prototypes/**/Traits/*.yml`; the lobby tab is literally labelled *"Perks"* via
`humanoid-profile-editor-traits-tab`). There is **no separate `perk`/`skill`/`feat`/`talent`
prototype type** in this codebase — traits ARE the perks. Fallout-flavored ones live in
`_Misfits/Traits/` and `_Nuclear14/Traits/` (Iron Fist, Life Giver, Bloody Mess, Adamantium
Skeleton, Careful Steps, Unnatural Regen, Sneak, Moving Asset, Gamma Shield, Dermal Armor,
pet/mount/riding perks, accents, paracusia, etc.).

**Rule:** every talent name AND effect in §5 must not duplicate an existing perk. Where the
classic Fallout perk name is already taken by an existing trait, we substitute a *different*
Fallout-lore talent (never a made-up name) — the goal is **"all-Fallout, no duplicates."**

| Dropped draft talent | Why | Replacement (Fallout lore) |
|----------------------|-----|----------------------------|
| Iron Fist (Melee T1) | `N14IronFist` exists (unarmed +2) | **Tenderizer** (FO4/76 — consecutive-hit damage ramp) |
| Steady (Melee T5) | is a scoped-ranged perk in lore; melee damage overlaps `N14IronFist` | **Big Leagues** (FO4 — melee weapon damage); **Steady** moved to Ranged T6 |
| Adamantium Skeleton (Melee T9) | `MisfitsAdamantiumSkeleton` exists (ungibbable) | **Stonewall** (FO:NV — knockdown immunity, resist while standing) |
| Bloody Mess (Misc T9) | `MisfitsBloodyMess` exists (gib threshold) | **Robotics Expert** (FO:NV — robot damage/EMP) |
| Life Giver (Survival T3) | `N14LifeGiver` exists (crit/dead threshold) | **Fireproof** (FO4 — heat/explosive resist) |
| Light Step (Survival T4) | `N14CarefulSteps` covers trap immunity + base `LightStep` existed | **Green Thumb** (FO4/76 — harvest doubling) |
| Moving Target (Survival T6) | `N14MovingAsset` exists (movement speed) | **Sprinter** (FO:NV — bigger speed, no wounded slow) |
| Healing Factor (Survival T7) | `N14UnnaturalReg` already provides passive regen | **Intense Training** (FO:NV — +1 END, ties to SPECIAL) |
| Solar Powered (regen) | passive regen would duplicate `N14UnnaturalReg` | reframed as **daylight stat conditioning** (+1 STR/END) |

> Note: N14 already has perks covering passive regen (`N14UnnaturalReg`), trap/mine immunity
> (`N14CarefulSteps`), stealth footsteps (`N14Sneak`), and movement speed (`N14MovingAsset`) —
> so the talent tree deliberately does **not** include those mechanics under new names.

---

## 3. Locked design decisions

1. **4 builds × 10 tiers × 1 talent = 40 talents.**
2. **10 free talent points** at character creation (`MaxPoints = 10`).
3. **Ladder prerequisites**: within a build, Tier N requires Tier N−1 owned. No cross-build prereqs.
4. **No leveling / XP / respec** in v1 (see §11 for future hooks).
5. Talents are chosen **at character creation**, stored in the character profile (mirrors SPECIAL),
   and applied to the runtime component **on spawn**.
6. Balance knobs live in a **`talentTuning` YAML prototype** (mirrors `specialTuning`).
7. All runtime queries go through a **`SharedTalentSystem`** (mirrors `SharedSpecialSystem`).
8. New gameplay hooks (armor pen, on-kill, regen, low-health buff, death protection) are
   implemented as **new systems/effects**, and existing systems that already consult
   `SharedSpecialSystem` gain additional `SharedTalentSystem` checks.
9. UI mirrors the existing SPECIAL window pattern exactly (button → `DefaultWindow`).
10. **No duplication with existing perks (Traits).** The 40 talent names and their effects must
    not collide with any existing trait/perk in the codebase (the Traits system *is* the perk
    system — there is no separate `perk`/`skill`/`feat` prototype type). Names come from Fallout
    lore across all games; where the lore name is already used by an existing perk (e.g. Iron Fist,
    Life Giver, Bloody Mess, Adamantium Skeleton, Light Step), a different Fallout-lore talent is
    substituted instead (§2.1).

---

## 4. Data model & architecture (anchors)

### New shared types (mirror the SPECIAL stack)

```
Content.Shared/_Misfits/Talent/
├── TalentBuild.cs            // enum: Melee, Ranged, Misc, Survival  (+ TalentBuilds.All)
├── TalentProfile.cs          // serializable profile copy (mirrors SpecialProfile)
├── TalentEvents.cs           // events: TalentChanged, TalentsApplied...
├── SharedTalentSystem.cs     // runtime query surface (mirrors SharedSpecialSystem)
├── Prototypes/
│   └── TalentTuningPrototype.cs  // balance knobs (mirrors SpecialTuningPrototype)
└── Components/
    └── TalentComponent.cs    // runtime owned-talents (mirrors SpecialComponent)
```

- **`TalentProfile`** (shared, `[DataDefinition, Serializable, NetSerializable]`)
  - `List<string> OwnedTalents` (prototype IDs)
  - `const int MaxPoints = 10;`  `int SpentPoints => OwnedTalents.Count;`
  - `int AvailablePoints => MaxPoints - SpentPoints;`
  - `bool IsValid`, `EnsureValid(profile)` — same fallback-to-default philosophy as `SpecialProfile`.
  - `bool Has(string talentId)`, `Add(string talentId)`, `Remove(...)`, `Clone()`, `MemberwiseEquals(...)`.
- **`TalentComponent`** (runtime, `[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]`)
  - `[AutoNetworkedField] List<string> OwnedTalents` — populated on spawn from the profile.
  - Reversible-effect caches, if needed (mirror `AppliedStaminaCritThresholdModifier` pattern).
- **`SharedTalentSystem`**
  - `HasTalent(EntityUid, string talentId)` → bool
  - `HasTalentInBuild(EntityUid, TalentBuild)` → bool
  - `GetOwnedTalents(EntityUid)` → list
  - `GetEffectiveBuildPoints(...)` (future INT-bonus hook)
- **`TalentTuningPrototype`** — one field per talent's magnitude, defaulting in code
  (mirror `SpecialTuningPrototype.Fallback`), values in YAML: `Resources/Prototypes/_Misfits/Talent/talent_tuning.yml`.

### Profile plumbing (mirrors SPECIAL)

| Existing (SPECIAL) | New (Talents) |
|--------------------|---------------|
| `HumanoidCharacterProfile.Special` | `HumanoidCharacterProfile.Talents` (`TalentProfile`) |
| `SpecialComponent` copied on spawn | `TalentComponent` copied on spawn |
| `PersistentPlayerDataSystem` rows for SPECIAL | persist `TalentProfile` alongside |
| `CharacterInfoSystem` shows SPECIAL | show owned talents (optional, phase 5) |
| `SharedSpecialSystem.GetEffective(...)` | `SharedTalentSystem.HasTalent(...)` |

### Application model

Two application strategies, used together:

1. **Direct hooks** — gameplay systems that already read `SharedSpecialSystem` get an
   additional `SharedTalentSystem.HasTalent(...)` branch. e.g. melee damage system checks
   `MeleeBigLeagues`/`MeleeTenderizer`/`MeleeWreckingBall`; gun spread checks `RangedGunslinger`/
   `RangedRifleman`/`RangedWeaponHandling`.
2. **Stat-style talents** (e.g. `MeleeToughness` stamina threshold, `SurvivalIntenseTraining`
   +1 END) — applied on spawn via a `TalentApplyOnSpawnSystem` that mutates the same reversible
   caches SPECIAL uses (`AppliedHealthThresholdModifier` etc.), so they stack/recompute cleanly.

---

## 5. The 40-talent catalog

> All values are **starting points**, tuned in `talent_tuning.yml`. "Hook" = existing system it
> plugs into; "NEW hook" = small new system/effect required (listed in §8).

### 5.1 MELEE — "Wasteland Brawler"  (STR · END · AGI)

| Tier | ID | Name | Effect | Hook |
|-----|-----|------|--------|------|
| 1 | `MeleeIronFist` | **Iron Fist** | Unarmed damage +15% | existing unarmed damage pipeline |
| 2 | `MeleeStrongBack` | **Strong Back** | Carry-pull speed +25%; less slowed by carried weight | `strengthCarryPullSpeedMultiplierPerPoint` |
| 3 | `MeleeToughness` | **Toughness** | Stamina crit threshold +10%; small flat damage reduction | `enduranceStaminaCritThresholdPerPoint` |
| 4 | `MeleeHeaveHo` | **Heave Ho!** | Thrown weapons: throw speed +20%, throw damage +25% | `strengthThrowSpeedMultiplierPerPoint` |
| 5 | `MeleeSteady` | **Steady** | Melee damage +15% | `strengthMeleeDamageMultiplierPerPoint` |
| 6 | `MeleeActionBoy` | **Action Boy** | Melee/unarmed action delay −20% | `agilityActionDelayMultiplierPerPoint` |
| 7 | `MeleePiercingStrike` | **Piercing Strike** | Melee/unarmed ignore 40% of target armor/resist | NEW: armor-penetration modifier |
| 8 | `MeleeSlayer` | **Slayer** | Unarmed & melee attack rate +25% | NEW: melee attack-rate modifier |
| 9 | `MeleeAdamantiumSkeleton` | **Adamantium Skeleton** | −30% incoming melee damage; limb damage resist | NEW: incoming-melee damage modifier |
| 10 | `MeleeWreckingBall` | **Wrecking Ball** (capstone) | Melee damage +30%; melee hits knock targets back | NEW: melee knockback |

### 5.2 RANGED — "Gunslinger"  (PER · AGI · LUCK)

| Tier | ID | Name | Effect | Hook |
|-----|-----|------|--------|------|
| 1 | `RangedGunslinger` | **Gunslinger** | One-handed guns: spread/recoil −15% | `perceptionSpreadMultiplierPerPoint` |
| 2 | `RangedRifleman` | **Rifleman** | Two-handed rifles: spread/recoil −15%, damage +5% | perception spread + damage |
| 3 | `RangedQuickDraw` | **Quick Draw** | Weapon equip/holster & action delay −15% | `agilityActionDelayMultiplierPerPoint` |
| 4 | `RangedConcentratedFire` | **Concentrated Fire** | Consecutive hits on same target: +5% accuracy/hit (max 5 stacks) | NEW: accuracy stacking |
| 5 | `RangedCommando` | **Commando** | Automatic weapons: damage +10% | NEW: auto-weapon damage class |
| 6 | `RangedSniper` | **Sniper** | Aimed/slow weapons: spread −25%; long-range damage +10% | NEW: ranged falloff/spread |
| 7 | `RangedWeaponHandling` | **Weapon Handling** | Heavy weapons: spread/recoil −25% | `perceptionHeavyGunMultiplierPerPoint` |
| 8 | `RangedBetterCriticals` | **Better Criticals** | Ranged critical damage +50% | `luckCriticalDamageMultiplier` |
| 9 | `RangedGrimReapersSprint` | **Grim Reaper's Sprint** | On kill: next reload/action 40% faster | NEW: on-kill modifier |
| 10 | `RangedDeadeye` | **Deadeye** (capstone) | +15% crit chance at close/mid range; 5% of shots auto-crit | NEW: per-weapon crit chance |

### 5.3 MISC — "Wasteland Scholar"  (INT · CHA · LUCK)

| Tier | ID | Name | Effect | Hook |
|-----|-----|------|--------|------|
| 1 | `MiscSwiftLearner` | **Swift Learner** | Handcraft & lathe crafting time −15% | `intelligenceLatheTimeMultiplierPerPoint` + handcraft |
| 2 | `MiscScrounger` | **Scrounger** | Ammo find chance in loot/scavenge +40% | `luckLootChancePerPoint` |
| 3 | `MiscFortuneFinder` | **Fortune Finder** | Cap/money find in loot +40% | loot table / NEW cap-find |
| 4 | `MiscEducated` | **Educated** | Buy prices −10%, sell prices +10% | `charismaTradeMultiplierPerPoint` |
| 5 | `MiscChemist` | **Chemist** | Chem/drug duration +40% | NEW: chem duration |
| 6 | `MiscChemResistant` | **Chem Resistant** | Reduced addiction chance & negative chem side effects | NEW: addiction resistance |
| 7 | `MiscJuryRigging` | **Jury Rigging** | Repairs & lathe use −25% materials | `intelligenceLatheMaterialUseMultiplierPerPoint` |
| 8 | `MiscNerdRage` | **Nerd Rage!** | Below 25% HP: +20% damage, +15% action speed | NEW: low-health buff |
| 9 | `MiscBloodyMess` | **Bloody Mess** | All damage +10% | NEW: global damage modifier |
| 10 | `MiscFortunesFavor` | **Fortune's Favor** (capstone) | All luck-driven chances +25%; on-kill chance to trigger a bonus strike | NEW: luck-chance amp + on-kill strike |

### 5.4 SURVIVAL — "Wasteland Survivor"  (END · PER · LUCK)

| Tier | ID | Name | Effect | Hook |
|-----|-----|------|--------|------|
| 1 | `SurvivalLeadBelly` | **Lead Belly** | Irradiated/rotten food & drink 50% less harmful; needs decay slower | `enduranceNeedDecayMultiplierPerPoint` |
| 2 | `SurvivalRadResistance` | **Rad Resistance** | Toxin/rad damage −30% | `enduranceToxinDamageMultiplierPerPoint` |
| 3 | `SurvivalLifeGiver` | **Life Giver** | +Max health (flat bonus) | `enduranceHealthModifierPerPoint` |
| 4 | `SurvivalLightStep` | **Light Step** | Mines/traps 40% less likely to trigger (mine delay +40%) | `perceptionMineDelayMultiplierPerPoint` |
| 5 | `SurvivalAquaboy` | **Aquaboy/Aquagirl** | Underwater endurance 2× longer; water hazards less harmful | NEW: underwater/water hazard |
| 6 | `SurvivalMovingTarget` | **Moving Target** | Sprint/hurt speed penalty removed; movement speed +10% | `agilityMovementSpeedMultiplierPerPoint` |
| 7 | `SurvivalHealingFactor` | **Healing Factor** | Slow passive HP regen; food/drink healing +25% | NEW: passive regen |
| 8 | `SurvivalSolarPowered` | **Solar Powered** | Outdoors/lit areas: +regen & small stat bonus | NEW: environment buff |
| 9 | `SurvivalSurvivalist` | **Survivalist** | Scavenge more food/water; hunger & thirst decay −30% | need decay + loot |
| 10 | `SurvivalLastStand` | **Last Stand** (capstone) | Once per life: survive lethal damage at 1 HP with 3s invulnerability + small heal | NEW: death protection |

---

## 6. UI plan

### 6.1 Character creation (lobby) — `HumanoidProfileEditor.xaml(.cs)`

- Add a **"TALENTS"** section/tab alongside the existing SPECIAL section
  (`HumanoidProfileEditor.xaml.cs` already hosts `BuildSpecialRow(...)`, the point-budget
  label at ~line 2491, and charisma-loadout logic at ~line 202).
- Layout: 4 build columns (Melee | Ranged | Misc | Survival), each a vertical list of 10 tier
  nodes. Each node: tier number, talent name, effect tooltip, and **Locked / Available / Owned**
  state:
  - **Locked** — previous tier in that build not owned.
  - **Available** — previous tier owned and points remain.
  - **Owned** — already purchased (highlighted).
- Point budget: `TalentProfile.MaxPoints` (10) with a remaining-points label
  (mirror `SpecialPointsLabel`).
- Buy = +1 point spent; refund = −1 (v1: refund only while still in the editor, before confirm).

### 6.2 In-game character window — `CharacterWindow.xaml(.cs)`

- Add an `OpenTalentsButton` beside the existing `OpenSpecialButton` / `OpenWalletButton`
  (the SPECIAL button is currently `Visible="False"` at `CharacterWindow.xaml` — TALENTS can
  ship visible, or mirror the same visibility toggle).
- New **`TalentWindow`** (`DefaultWindow`, mirrors `SpecialWindow` in
  `Content.Client/UserInterface/Systems/Character/Windows/`):
  - Same 4-build-column layout, **read-only** in-game (view your build).
  - `SpecialApplyButton` / `SpecialCancelButton` / `SpecialConfirmRow` pattern reused for
    creation-time edits.
- New Loc strings: `character-info-talents-*`, `character-info-talent-window-button`,
  `character-info-talents-points-remaining`, per-talent name/desc, per-build labels.
  (SPECIAL strings: `character-info-special-*` in `Resources/Locale/...`.)

---

## 7. Server-side application & new hooks

### 7.1 Apply on spawn

- `TalentApplyOnSpawnSystem` (server) — on spawn, copy `TalentProfile` → `TalentComponent`
  and apply stat-style talents into the reversible caches (same machinery as `SpecialStatBoostEffect`:
  `Content.Server/_Misfits/EntityEffects/Effects/SpecialStatBoostEffect.cs`).
- Stat-style: `MeleeToughness` (stamina threshold), `SurvivalLifeGiver` (health),
  `SurvivalLeadBelly`/`SurvivalSurvivalist` (need decay), `MeleeStrongBack` (carry pull).

### 7.2 Direct hooks into existing systems

The following already consult `SharedSpecialSystem` and should gain `SharedTalentSystem` checks:

| Existing system | Talent(s) |
|-----------------|-----------|
| Melee/unarmed damage (`strengthMeleeDamageMultiplierPerPoint`) | `MeleeIronFist`, `MeleeSteady`, `MeleeWreckingBall` |
| Throw speed (`strengthThrowSpeedMultiplierPerPoint`) | `MeleeHeaveHo` |
| Carry pull (`strengthCarryPullSpeedMultiplierPerPoint`) | `MeleeStrongBack` |
| Stamina crit threshold / health (`endurance*`) | `MeleeToughness`, `SurvivalLifeGiver` |
| Need decay (`enduranceNeedDecayMultiplierPerPoint`) | `SurvivalLeadBelly`, `SurvivalSurvivalist` |
| Toxin damage (`enduranceToxinDamageMultiplierPerPoint`) | `SurvivalRadResistance` |
| Spread/recoil (`perceptionSpreadMultiplierPerPoint`) | `RangedGunslinger`, `RangedRifleman` |
| Heavy gun (`perceptionHeavyGunMultiplierPerPoint`) | `RangedWeaponHandling` |
| Mine delay (`perceptionMineDelayMultiplierPerPoint`) | `SurvivalLightStep` |
| Trade (`charismaTradeMultiplierPerPoint`) | `MiscEducated` |
| Lathe time/material (`intelligenceLathe*`) | `MiscSwiftLearner`, `MiscJuryRigging` |
| Action delay (`agilityActionDelayMultiplierPerPoint`) | `MeleeActionBoy`, `RangedQuickDraw`, `MiscNerdRage` |
| Movement speed (`agilityMovementSpeedMultiplierPerPoint`) | `SurvivalMovingTarget` |
| Crit chance/damage (`luck*`) | `RangedBetterCriticals`, `RangedDeadeye`, `MiscFortunesFavor` |
| Loot chance (`luckLootChancePerPoint`) | `MiscScrounger`, `MiscFortuneFinder` |

### 7.3 New hooks required (small, focused systems)

1. **Armor penetration** (`MeleePiercingStrike`) — damage-events modifier skipping X% armor/resist.
2. **Melee attack rate** (`MeleeSlayer`) — reduce melee swing cooldown/recoil.
3. **Incoming melee reduction** (`MeleeAdamantiumSkeleton`) — damage-events incoming-modifier.
4. **Melee knockback** (`MeleeWreckingBall`) — apply knockback impulse on melee hit.
5. **Accuracy stacking** (`RangedConcentratedFire`) — per-target hit streak → spread reduction.
6. **Weapon damage class** (`RangedCommando`) — auto-fire weapon category multiplier.
7. **Ranged falloff/spread** (`RangedSniper`) — per-distance spread & damage modifier.
8. **On-kill effects** (`RangedGrimReapersSprint`, `MiscFortunesFavor`) — on-kill event → reload/action buff or bonus strike.
9. **Per-weapon crit chance** (`RangedDeadeye`) — crit chance modifier on gun fire.
10. **Chem duration / addiction** (`MiscChemist`, `MiscChemResistant`) — hook chem/drug system.
11. **Global damage modifier** (`MiscBloodyMess`) — generic outgoing-damage multiplier.
12. **Low-health buff** (`MiscNerdRage`) — threshold watcher → damage/action-speed buff.
13. **Passive regen** (`SurvivalHealingFactor`) — timed regen while not critically hurt.
14. **Environment buff** (`SurvivalSolarPowered`) — outdoors/lit check → regen/stat bonus.
15. **Underwater/water hazard** (`SurvivalAquaboy`) — drowning/water damage protection.
16. **Death protection** (`SurvivalLastStand`) — once-per-life lethal-hit survival + invuln window.

> Recommended cut: ship **Phases 1–4** (data + UI + apply + the *existing-hook* talents) first;
> the 16 new hooks in §7.3 can land incrementally after the framework is proven.

---

## 8. Persistence & validation

- **Character profile**: add `TalentProfile Talents` to `HumanoidCharacterProfile` (mirror `Special`),
  validated by `EnsureValid` (fallback to default on malformed/over-budget input, never partial-clamp).
- **Persistent player data**: extend `PersistentPlayerDataSystem` (which already stores SPECIAL at
  `Content.Server/_Misfits/PlayerData/PersistentPlayerDataSystem.cs` ~line 172) to store the talent
  profile alongside SPECIAL for character info/history.
- **Server-side validation**: on set/apply, reject invalid profiles — reuse the
  `SpecialProfile.IsValid`-style philosophy (`PersistentPlayerDataSystem` currently rejects
  out-of-range SPECIAL sums; mirror for talent point budget).
- **Round-scoped runtime**: `TalentComponent` is networked like `SpecialComponent`
  (AutoNetworkedField) so hooks on client+server see owned talents consistently.

---

## 9. Tests

- **`TalentProfileTest`** (`Content.Tests/Shared/Misfits/Talent/`) — mirror `SpecialProfileTest`:
  bounds, `AvailablePoints`, `IsValid`, `EnsureValid` fallback, `Has/Add/Remove`, memberwise equality.
- **Ladder validation test** — buying Tier N without Tier N−1 in the same build is rejected.
- **Integration test** — mirror `AdminCloneSpecialTest` (`Content.IntegrationTests/Tests/_Misfits/Special/`):
  spawn with a profile, assert `TalentComponent.OwnedTalents`, and (later) assert a gameplay effect
  (e.g., melee damage multiplier) changes with/without a talent.

---

## 10. Phased implementation plan

### Phase 0 — Design freeze (this document) ✅
- [x] Fallout talent knowledge captured (§2)
- [x] 4 builds × 40 talents specced (§5)
- [x] Architecture mapped to existing SPECIAL stack (§4)
- [x] Decisions locked (§3)

### Phase 1 — Shared data model
- [ ] `TalentBuild.cs`, `TalentProfile.cs`, `TalentEvents.cs`
- [ ] `TalentComponent.cs`, `SharedTalentSystem.cs` (HasTalent/query surface)
- [ ] `TalentTuningPrototype.cs` + `Resources/Prototypes/_Misfits/Talent/talent_tuning.yml`
- [ ] `TalentProfileTest` (data-model + ladder validation)

### Phase 2 — Character creation UI (lobby)
- [ ] `HumanoidProfileEditor` TALENTS section (4 columns × 10 tiers, buy/refund, point label)
- [ ] Loc strings (`character-info-talents-*`)
- [ ] Profile serialization/validation into `HumanoidCharacterProfile`

### Phase 3 — In-game window
- [ ] `TalentWindow.xaml(.cs)` (read-only build view, mirrors `SpecialWindow`)
- [ ] `OpenTalentsButton` in `CharacterWindow.xaml` + controller binding

### Phase 4 — Server application (existing hooks)
- [ ] `TalentApplyOnSpawnSystem` (profile → `TalentComponent` + reversible caches)
- [ ] Wire `SharedTalentSystem.HasTalent` checks into existing systems (§7.2)
- [ ] Integration test (spawn → owned talents → one gameplay assertion)

### Phase 5 — Persistence
- [ ] `PersistentPlayerDataSystem` talent column/row (mirror SPECIAL)
- [ ] `CharacterInfoSystem` shows owned talents (optional)

### Phase 6 — New hooks & balance
- [ ] Implement new hooks from §7.3 in priority order (on-kill, crit, regen, death-protection first)
- [ ] Balance pass in `talent_tuning.yml`

---

## 11. Open questions / future work

1. **INT → +talent points** (recommended next): INT ≥ 8 grants +1, INT 10 grants +2 talent points
   — the classic "INT gives more points" Fallout hook, no leveling needed.
2. **Respecs**: once per life / via a wasteland NPC / a rare chem? v1 = none.
3. **Talent ranks**: converting tier 8–10 talents into multi-rank nodes later (FO4 style).
4. **Cross-build synergies**: e.g., `MeleeToughness` + `SurvivalLifeGiver` stacking thresholds.
5. **In-round talent unlocks** if a leveling system is ever introduced (points per level, tier gating).
6. **Character info window visibility** for talents (Phase 5 optional).
7. **Do capstones require the full 10 in that build, or just Tier 9?** Default: Tier 9 (allows a
   1-point-dip into a second build while still capping one — but with 10 points you can't both cap
   a build AND dip; revisit during playtest).
