# Roadmap

What is planned but not built. Items move out of here into `CHANGELOG.md` when
they ship.

---

## Next (1.2.0)

### A UI tab

A tab for authoring the game's interface, alongside the existing content tabs.

Nothing is decided about it yet, and the shape of the tab follows from one
question that has not been answered: **which parts of the UI a pack should be
able to touch** — replacing art on existing elements, adding new elements of its
own, or both. That decides whether this is a skinning tab (a list of vanilla UI
objects, each with a sprite override, in the shape the vanilla-place extensions
already use) or an authoring one (arbitrary elements with their own transforms
and conditions, closer to the Places tab's GameObject tree).

Worth settling before any of it is designed, because the two answers share
almost no code.

### A key-press condition

A condition that passes on a keyboard key, so a pack can put something behind
a keypress rather than behind a button or a line of dialogue.

The interesting part is not reading the key. It is that "pressed" and "held"
are different questions, and the condition vocabulary already has a context
split that decides which one is answerable where:

- **Polled** (dialogue start conditions, button visibility) re-evaluates every
  frame, so *is this key down* works and *was it just pressed* is a single
  frame the author will usually miss.
- **Rule** polls too, but has a discrete fired moment. *Is this key down* here
  re-fires on every frame the key is held, which is the same trap `Random`
  falls into per-frame and why `Timer` is offered only on rules.
- **OneShot** (a node's own conditions, level hooks) is checked once when the
  node is reached. Asking whether a key happened to be down at that instant is
  almost never what anybody means, so this may simply not belong there.

So the shape is probably two conditions rather than one, offered in different
contexts, rather than a single type with a mode dropdown that is wrong in two
of the three places it appears.

Points to decide:

- **Edge detection needs somewhere to live.** `ConditionEvaluator` is handed
  the variable store, a logger and a pack id, and nothing else. `DailyChance`
  got around having no state by deriving its roll from (pack, id, day); a key
  press cannot be derived and needs per-frame state kept somewhere.
- **Whose key is it.** The game has its own bindings and the player may be
  typing into a field. A pack reading raw input will fight both unless there
  is a rule about when a pack condition is allowed to see a key at all.
- **Which keys to offer.** A closed list an author picks from is safer than a
  typed key name, and it is the only version that can leave out the keys the
  game already owns.

### Auto-update, from the GitHub releases

The editor should notice when a newer version has been published and offer it,
rather than relying on someone seeing a Discord post.

Most of what this needs is already in place: releases are tagged `vMAJOR.MINOR.PATCH`
on `ArcoFrio/SMSModForge`, and the running version is stamped in
`SMSModForge.csproj` and in `Plugin.pluginVersion`, so a check is a comparison
between the assembly version and the latest tag from the releases API.

Points to decide:

- **Notify, or install.** Telling the author there is an update and linking to
  it is a small feature. Downloading and swapping the files underneath a running
  editor is a much larger one, and it has to leave the BepInEx plugin in step
  with the editor — the two are versioned together and 1.1.0 already has a
  case where a mismatch breaks packs silently.
- **Offline and rate limits.** The check must never block startup or complain at
  someone working without a network; the unauthenticated API allows 60 requests
  an hour per address.
- **Opt out.** Some authors will not want an editor that talks to the network at
  all, so this wants a setting, and the setting wants to be honoured before the
  first request rather than after it.
