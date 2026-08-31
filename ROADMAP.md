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
