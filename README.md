# SMSModForge

Two-project toolkit for authoring mod content for **Starmaker Story 1.8E**:

1. **SMSModForge** — WPF desktop editor for authoring mod packs.
2. **SMSModForge.PackPlugin** — BepInEx plugin that loads those packs into
   the running game. Standalone — does not depend on
   [SMSAndroidsCore](../SMSAndroids); finds vanilla scene objects directly.

A "mod pack" is one folder on disk containing a `modpack.json` manifest and
the PNGs / JSON it references. Packs carry five slices of content:

- **Busts** — per-character outfits with jiggle parameters, expressions, and
  particle slots. The plugin clones the vanilla `Anna_YellowSexy` bust
  prototype and dresses it with the authored sprites.
- **Places** — custom navigable levels with their own art, parallax, map
  button, and roomtalk. The plugin clones `14_Beach` and re-skins it; map
  buttons are wired by stable name so two packs never collide on the
  navigator-index allocation.
- **Vanilla extensions** — pack-authored navigator buttons that hang off a
  vanilla place's strip (e.g. an "entry point" button on `14_Beach` that
  takes the player to your `SecretCave`).
- **Dialogues** — node-graph dialogues attached to roomtalks (vanilla or
  pack-defined), with text / choice / random nodes, per-node actions and
  conditions, start gating, optional one-shot semantics, and optional
  suppression of the parent roomtalk's vanilla `Trigger` for rooms whose
  default dialogue you want to override.
- **Actors + Variables** — speakers (with default-bust + expression
  mappings) and pack-defined variables (typed, optionally persisted to a
  per-pack JSON file under `BepInEx/plugins/SMSModForge/Saves/`).

> The project was originally named **SMSBustForge** when it only authored
> busts. Pre-rename packs that still use `bustpack.json` and live under
> `BustPacks/` are accepted by both the editor (Load + auto-rewrite to
> `modpack.json` on Save) and the plugin (fallback path).

## Build / run

The editor (WPF) and the plugin (BepInEx) are sibling projects in the same
solution. The editor needs only the .NET 8 SDK; the plugin needs the
sibling [SMSAndroids checkout](../SMSAndroids) cloned next to this one so it
can resolve its DLL references from `..\..\SMSAndroids\Libraries\`.

```pwsh
# Editor
dotnet build SMSModForge\SMSModForge.csproj
dotnet run --project SMSModForge

# BepInEx plugin (drops into BepInEx/plugins/ on your game install)
dotnet build SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj
```

The plugin's Debug `OutputPath` is hardcoded to a specific local install
path in `SMSModForge.PackPlugin.csproj` — change it for your machine.

## Layout of a pack on disk

```
<PackRoot>/
  modpack.json               ← manifest (this editor reads/writes it)
  Sprites/
    Newgirl/
      Newgirl00.PNG          ← base, 256×256
      Newgirl00Mask.PNG      ← JiggleSprite mask: R=bounce, G=wave, B=noise, A=intensity
      NewgirlBlink.PNG       ← blink overlay
      Mouth1.PNG … Mouth4.PNG
      ExpressionHappy.PNG, ExpressionAngry.PNG, ExpressionSad.PNG, ExpressionFlirty.PNG
  Locations/
    BeachRoom.PNG            ← place base, 2048×1136
    BeachRoomB.PNG           ← secondary, 2048×1136
    BeachRoomMask.PNG        ← mask, 256×143
  Particles/
    Newgirl_Wet.json         ← optional (v1.1; for now use the "Wet" preset)
```

Bust PNGs **must be 256×256 RGBA**. Place sprites follow the vanilla level
shape (`2048×1136` for the two big sprites, `256×143` for the mask).

## What the editor authors

### Busts tab

- **Per-outfit sprite slots** (base / mask / blink) and shared **mouth** /
  **expression** prefixes.
- **Per-outfit JiggleSprite uniforms** — `_JiggleSpeed`, `_JiggleStrength`,
  `_JiggleFrequency`, `_NoiseScale`, `_NoiseSpeed`, `_NoiseStrength`,
  `_Color`, `_PixelSnap`.
- **Particle references** — defaults to one `"Wet"` preset.
- **Mask painter** — paint R/G/B/A channels directly on the mask PNG with a
  soft brush; the preview reflects strokes in real time.

### Places tab

- **One place per row** in the left list; click + Place to add, − Remove to
  delete.
- **Identity**: pack-local `key`, internal GO name, button display name.
- **Level art**: three PNG paths (base / secondary / mask) relative to the
  pack root.
- **Behaviour**: parallax strength slider, optional "keep outdoor audio +
  particles" for places that should carry rain / ambience.
- **Navigator buttons**: for each place, list the buttons that should appear
  when *this* place is the active level. Each button targets another place
  by stable reference — `vanilla:<goName>`, `self:<key>`, or
  `pack:<otherPackId>.<key>` — and optionally swaps music on transit.

#### Why named references, not indices

The vanilla game keys destinations by **sibling index** under `5_Levels`
(via the `Upcoming-Level` GC2 variable). Each new place is appended to that
list, so its index depends on the order packs initialise. If two packs both
baked literal indices into their navigator buttons, the second pack would
end up pointing at the first pack's levels.

The editor instead emits stable names. At mod load time
`SMSAndroids/PlacePacks.cs` allocates one sibling index per place as it
builds them, records the mapping under
`pack:<packId>.<key>` → index, and resolves every navigator button in a
second pass — so cross-pack references work regardless of which mod loaded
first.

### Dialogues tab

- **Dialogue list** on the left, plus a per-dialogue editor with identity
  (key, display name, target roomtalk picker, `disableVanillaTrigger`,
  `oneShot`), **start conditions** (an AND-conjunction the dispatcher
  checks each frame while the parent roomtalk's level is active), and a
  **flat node list** with `+ Root` / `+ Child` / `− Remove` operations.
- **Node editor** on the right: kind (Text / Choice / Random), actor +
  expression pickers, line text, optional `tag`, jump mode
  (Continue / Exit / Jump), per-node conditions, and two action lists
  (`actionsOnStart` / `actionsOnFinish`).
- **Action vocabulary** (extensible): `SetVariable`, `IncrementVariable`,
  `SetActorBust`, `SetActorExpression`, `SetGameObjectActive`,
  `EmitSignal`, `SwitchMusic`, `EndDialogue`, `Wait`.
- **Condition vocabulary**: `VariableEquals`, `VariableGreaterThan`,
  `VariableLessThan`, `VariableExists`, `GameVariableEquals`,
  `LevelActive`, `GameObjectActive`, `Random`, `AlwaysTrue`. Each
  condition supports a `negate` flag.

Each action/condition serialises as `{ "type": "…", "params": { ... } }`.
The editor renders the params dictionary as a single `key=value`-per-line
TextBox — concise to author, easy to copy, and forward-compatible if we
add new fields per type without changing the schema.

### Actors tab

- **Per-pack speakers**, each with a `key`, display name, and the bust
  GameObject name that represents them by default. The plugin's
  `ActorRegistry` listens to `Dialogue.EventStartNext` and routes the
  right bust + expression for the speaking actor — replacing
  SMSAndroids' marker-GameObject-activation workaround with direct
  event-driven dispatch.
- **Per-actor expression mappings** override the default
  "expression key matches a child name under `<bust>/MBase1/Expressions/`"
  behaviour. Add entries to map e.g. `Surprised → SurprisedSpecial`
  on one bust.

### Variables tab

- **Pack-defined variables**, each with a name, type
  (`Bool / Int / Float / String`), default value, `persisted` flag,
  and an optional description.
- Persisted variables write through to
  `BepInEx/plugins/SMSModForge/Saves/<packId>.json` on every mutating
  action. Non-persisted variables reset on every fresh CoreGameScene.

## Live bust preview

The right-hand pane on the Busts tab runs a **CPU port of `Sprites/JiggleSprite`**
at 30 FPS against the actual mask PNG and the live slider values. Mouth /
blink / expression toggles overlay on top. All edits feed the preview in
real time. The CPU port (`Rendering/JiggleShader.cs`) is a transliteration
of the HLSL so what you see in the editor matches what the game draws.

## Vanilla character catalog + art

The Actors tab's **Default bust** picker lists every vanilla bust under
`2_Bust_Manager` (~314 entries on 1.8E) alongside any pack-authored
outfits, so an actor can speak using an existing in-game character
without the pack shipping new art. The dialogue node editor previews
the speaker's bust + expression live.

Two pieces of data drive this:

1. **Catalog of GO names** — `SMSModForge/Model/VanillaBusts.cs`.
   Parsed once from the reference hierarchy dump at
   `SMSAndroids/ReferenceClasses/CoreGameScene_Hierarchy_1.8E.txt`. Pure
   text — no textures.
2. **Actual PNG art** — `SMSModForge/Resources/VanillaBustArt/<BustGoName>/`.
   `Base.PNG`, `Blink.PNG`, `Mouth1.PNG`…`Mouth4.PNG`,
   `ExpressionHappy.PNG`/`Angry`/`Sad`/`Flirty`. Committed to the repo
   and copied to `<output>/VanillaBustArt/` next to `SMSModForge.exe`
   at build time. Comes from a Unity-editor script run once in the
   vanilla game's Unity project.

### Extracting the art (one-time, repo-side)

Required when bringing the editor up on a fresh checkout, or refreshing
after a game update that changes the bust roster.

1. Open the **vanilla Starmaker Story Unity project** in the Unity
   editor matching the game's Unity version.
2. Copy `Tools/UnityEditor/SMSModForgeArtExtractor.cs` into any
   `Assets/Editor/` folder inside that project (create one if needed —
   Unity treats `Editor` folders as editor-only code).
3. Open the scene that contains the `2_Bust_Manager` GameObject (the
   `CoreGameScene`).
4. Run **Tools › SMSModForge › Extract Bust Art…**. Pick
   `SMSModForge/Resources/VanillaBustArt/` (inside this repo) as the
   output folder. The script walks every direct child of
   `2_Bust_Manager` and writes the PNG layers per bust.
5. Commit the new `Resources/VanillaBustArt/` contents.
6. Rebuild the editor. The csproj's `Content` glob copies every PNG to
   `<output>/VanillaBustArt/`; the dialogue-node preview reads from
   there at run-time via `AppContext.BaseDirectory`.

Re-running the extractor is safe — it overwrites existing PNGs.

### Regenerating the *names* catalog after a game update

If a game update only adds/removes busts (no rebuild needed for the
existing art), regenerate the names list from a fresh hierarchy dump:

```pwsh
python Tools/regen_vanilla_busts.py `
    --hierarchy ../SMSAndroids/SMSAndroids/ReferenceClasses/CoreGameScene_Hierarchy_<version>.txt
```

The script writes a fresh `VanillaBusts.cs` directly. The auto-derived
`Character` grouping label is best-effort (prefix-before-underscore with
a small override table); review and refine the result before committing
if you care about display quality.

## Install (end users)

Packs go under your game's BepInEx folder:

```
<game>/BepInEx/plugins/SMSModForge/ModPacks/<packId>/
    modpack.json
    Sprites/...
    Locations/...
```

The plugin DLL itself goes in `BepInEx/plugins/SMSModForge.PackPlugin.dll`.

## Schema version

`modpack.json` carries `"$schema": "smsmodforge/modpack/v1"`. The plugin
currently treats this field as informational; future bumps will gate loader
paths on it. The editor still **reads** older packs that have
`"$schema": "smsbustforge/bustpack/v1"` and `bustpack.json` filenames — it
just rewrites them as `modpack.json` on the next save. The plugin only
reads `modpack.json`, so pre-rename packs need to be re-saved through the
editor before they show up in-game.

## Plugin architecture (dialogues)

The plugin builds GC2 `Dialogue` MonoBehaviours from the pack manifest
without depending on SMSAndroids:

- **`DialogueBuilder`** — instantiates a `Dialogue` under the target
  roomtalk transform, uses public `Content.AddToRoot` / `AddChild` for the
  node tree, and reflection for the few private serialised fields we set
  on each `Node` (`m_NodeType`, `m_Conditions`, `m_Tag`, `m_Jump`).
  The plugin harvests a `DialogueSkin` from the first vanilla dialogue it
  finds in `8_Room_Talk`, so packs don't ship an asset bundle.
- **`PackCondition : Condition`** — a custom GC2 `Condition` subclass that
  delegates to the pack's `ConditionEvaluator`. Attached to each node's
  `m_Conditions` so GC2's native gating (including
  `NodeTypeChoice.HideUnavailable`) routes through pack state.
- **`DialogueDispatcher`** — one per pack. Per frame, it checks dialogue
  start conditions, enforces single-dialogue-at-a-time globally (polling
  `Dialogue.Current` + active children under `8_Room_Talk` — interops with
  SMSAndroids' equivalent check when installed), drives the actor /
  bust update on `Dialogue.EventStartNext`, and runs per-node actions
  on both `EventStartNext` and `EventFinishNext`. Honours
  `disableVanillaTrigger` by toggling the parent roomtalk's `Trigger`
  while the player is inside its level.
- **`PackVariableStore`** — typed dictionary with disk persistence per
  pack. The dispatcher flushes after any action that touched a persisted
  variable.

The action / condition vocabularies are open-ended: adding a new entry
means a new constant in `NodeActionTypes` / `NodeConditionTypes` on the
editor side, plus a new `case` in the plugin's `ActionRuntime` /
`ConditionEvaluator`. No serialisation changes — `{type, params}` carries
anything.

## Roadmap

- **Custom particle editor** — author a full `ParticleSystem` module dump and
  ship it as `Particles/*.json` alongside the pack.
- **Per-type action / condition param fields** — the editor currently renders
  params as a `key=value`-per-line TextBox; replacing that with per-type
  guided field grids would catch typos at author time.
- **Custom Instruction subclass for OnStart/OnFinish lists** — letting packs
  use GC2's instruction lists directly would unlock parity with vanilla
  dialogues that mix mod-authored and game-authored nodes.
