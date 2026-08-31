# SMSModForge 1.1.0

A toolkit for making mod content for **Starmaker Story 1.8E**, in two parts:

1. **SMSModForge** — a Windows desktop editor for authoring mod packs.
2. **SMSModForge.PackPlugin** — a BepInEx plugin that loads those packs into
   the running game. Standalone: it finds the vanilla scene objects itself and
   does not depend on any other mod.

A **mod pack** is one folder containing a `modpack.json` manifest and the PNGs
and audio it references. You build one in the editor, export it, and drop it
into the game.

---

## For authors

### What you need

- **Windows**, and the **[.NET Desktop Runtime 8.0][dotnet]** for the editor.
  It must be the *Desktop* runtime — the plain .NET runtime will not start a
  WPF application. If it is missing, Windows offers you the download.
- **BepInEx** installed in your Starmaker Story folder, for the plugin.

[dotnet]: https://dotnet.microsoft.com/download/dotnet/8.0

### Installing

**The plugin**, into your game folder:

```
<game>/BepInEx/plugins/
    SMSModForge.PackPlugin.dll
    Newtonsoft.Json.dll          ← required; the game does not provide it
    VanillaFrames/               ← must sit beside the DLL
        PhotoFrame.png
        SexyFrame.png
```

`VanillaFrames/` is found relative to the DLL, so keep them together or scenes
that use a vanilla border will quietly lose it.

**Your packs**, either loose or as the single file the editor exports:

```
<game>/BepInEx/plugins/SMSModForge/ModPacks/
    MyPack/                      ← a pack folder
        modpack.json
        ...
    SomeoneElsesPack.smspack     ← or an exported archive
```

The editor itself can live anywhere; it does not need to be inside the game
folder.

### Learning it

The editor teaches itself. On the **⚒ ModForge** tab you will find:

- **Fourteen guided tutorials** that walk you through building a real pack, one
  control at a time — a character and its face, a room you can walk into and
  dress, a door on the world map, conversations that branch and remember, a
  jiggle mask, NPCs, music and sound, variables, and rules. They run in order,
  each one carrying on in the pack the last one left, and everything they make
  is kept. The practice art and audio ship with the editor.
- A tutorial you have finished stays ticked, and says so if it has been
  rewritten since you ran it.
- **A 33-topic reference** with search, covering every tab and field.
- **Validate**, which lists what is wrong with your pack and jumps you to it.

If you read nothing else, run the first tutorial. It ends with your character
loaded in the game.

### What a pack can contain

| | |
|---|---|
| **Characters** | Speakers with their own bust art, outfits, expressions, and a jiggle mask |
| **NPCs** | Standing figures, defined once and placed into any number of rooms |
| **Places** | New rooms, or your own additions to the game's existing ones |
| **Map Buttons** | Entries on the world map so players can reach your places |
| **Dialogues** | Branching conversations, with actions and conditions per line |
| **Scenes** | Full-screen CGs, with a frame and a sound |
| **Music** | Background tracks |
| **SFX** | Sound effects, fired by an action or by words in a line |
| **Wallpapers** | Images the player unlocks and can set in-game |
| **Variables** | The pack's memory, optionally surviving a restart |
| **Integration** | Rules that watch the game and act without a conversation |

Dialogue nodes and integration rules draw on **26 action types** and
**23 condition types**. The in-app reference documents them; this file
deliberately does not duplicate the list, because a copy here would rot.

---

## Layout of a pack on disk

The editor manages this for you — it is here so you can read a pack by hand.

```
<PackRoot>/
  modpack.json                 ← the manifest
  Sprites/
    MyGirl/
      Base.PNG                 ← bust art, 256×256 RGBA
      Mask.PNG                 ← jiggle mask: R/G/B = Bounce/Wave/Noise, A scales all three
      Blink.PNG                ← optional
      Mouth1.PNG … Mouth4.PNG  ← optional
      ExpressionHappy.PNG …    ← optional: Happy / Angry / Sad / Flirty
  Locations/
    MyRoom.PNG                 ← a room layer, 2048×1136
    MyRoomMask.PNG             ← optional, 256×143
  Scenes/, Wallpapers/, Music/, SFX/
```

Bust art is **256×256 RGBA**. Room layers follow the vanilla level shape,
`2048×1136`, with masks at `256×143`.

**Masks are optional.** A room or bust with no mask simply does not move — the
plugin binds a fully transparent mask rather than refusing to build it.

A place mask is a different shape from a bust one: a single intensity plane, and
it lives in **alpha**, so painting a place mask's colour channels does nothing.
The mask painter already knows which kind it is editing and hides the layers
that do not apply.

A place draws in two layers. The editor calls them **back** and **front** by
where they sit; the manifest calls them `secondarySprite` and `baseSprite`,
which is the game's own naming and reads backwards. Every field's tooltip
names the key it writes.

---

## For developers

### Building

Both projects live in one solution. The editor needs only the **.NET 8 SDK**:

```pwsh
dotnet build SMSModForge\SMSModForge.csproj
dotnet run   --project SMSModForge
```

The plugin additionally needs the game's managed assemblies — BepInEx, Harmony,
Unity, GC2 and the base game's `Assembly-CSharp`. None are redistributable, so
they are not in this repo. Copy `Directory.Build.props.example` to
`Directory.Build.props` and fill in the two paths it asks for: where those
assemblies live, and where a plugin build should land. That copy is git-ignored,
so your paths stay on your machine.

```pwsh
copy Directory.Build.props.example Directory.Build.props
# edit it, then:
dotnet build SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj
```

Build it without that file and it stops with a message naming the file to copy,
rather than a wall of unresolved references.

**Build a release from a clean output directory.** The content globs use
`PreserveNewest`, which copies changed files but never deletes removed ones, so
an incremental build can carry forward assets that should no longer ship.

### Vanilla data the editor ships

Two separate things, refreshed differently.

**The catalog of names** — `SMSModForge/Model/VanillaBusts.cs`, currently 285
busts. **Not** every bust in the game: some exist in the scene with no content
that shows them, and this tool has no business advertising unreleased work.
They are excluded deliberately, and refreshing the list is therefore not a
matter of re-running the extraction. See the file's own header before touching
it.

**The preview art** — `SMSModForge/Resources/VanillaBustArt/` and
`VanillaLevelArt/`, extracted from the game's Unity project by the scripts in
`Tools/UnityEditor/`. Roughly 450 MB at full resolution, which is not what
ships:

```pwsh
python SMSModForge\Tools\MakeArtThumbnails.py --scale-bust 1.5 --scale-level 4
```

This writes `Resources/VanillaArtThumbs/`, which is what the build actually
copies — about 67 MB. Busts are point-sampled, because they are upscaled again
for display and want hard pixels rather than blur; levels use a smooth filter,
because they are only ever viewed smaller than the thumbnail. The preview
restores each image to its recorded original size on load, so layout is
identical to shipping full-resolution art and only sharpness differs.

The generator ships art **only for busts in the catalog**. Drop one from
`VanillaBusts.cs` and its artwork stops shipping too — the two cannot drift.

### Checking the documentation

```pwsh
python SMSModForge\Tools\DocCoverage.py
```

Reads the editor's functions out of the source — tabs, fields, buttons, menu
items, and every action and condition type — and reports what the in-app
reference never mentions. It is keyed to what the **picker** shows, not what
the code calls things: `SetVariable`, `IncrementVariable`,
`PickRandomFromList` and `CountList` are not offered by name at all, appearing
as a single **Variable** entry with an Operation dropdown, and `ActivateScene`
as Set-Active's **Scene** category.

### Plugin architecture

- **`DialogueBuilder`** builds GC2 `Dialogue` behaviours from the manifest,
  using the public `Content.AddToRoot` / `AddChild` for the node tree and
  reflection for the few private serialised fields on each `Node`. It harvests
  a `DialogueSkin` from the first vanilla dialogue under `8_Room_Talk`, so
  packs ship no asset bundle.
- **`PackCondition : Condition`** delegates to the pack's `ConditionEvaluator`,
  so GC2's own gating — including `NodeTypeChoice.HideUnavailable` — routes
  through pack state.
- **`DialogueDispatcher`**, one per pack, checks start conditions, enforces one
  dialogue at a time, and runs per-node actions on start and finish.
- **`PackVariableStore`** is a typed store with per-pack persistence under
  `BepInEx/plugins/SMSModForge/Saves/<packId>.json`.
- **`BustFactory` / `PlaceFactory` / `SceneFactory` / `NpcFactory`** clone the
  vanilla prototypes and re-dress them. A slot the pack does not fill is
  **emptied**, never left carrying the prototype's own art.

Adding an action or condition means a constant in `NodeActionTypes` /
`NodeConditionTypes`, a schema entry describing its parameters, and a case in
the plugin's `ActionRuntime` / `ConditionEvaluator`. The wire format is
`{ "type": …, "params": { … } }` and does not change.

### Schema and compatibility

`modpack.json` carries `"$schema": "smsmodforge/modpack/v1"`. The editor still
reads pre-rename packs — `bustpack.json` with
`"$schema": "smsbustforge/bustpack/v1"` — and rewrites them as `modpack.json`
on the next save. The plugin reads only `modpack.json`, so an old pack has to
be re-saved through the editor before the game will see it.

> The project was called **SMSBustForge** when it only authored busts, which is
> why the repository still carries that name.
