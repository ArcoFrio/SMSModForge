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

### ~~Auto-update, from the GitHub releases~~ — done

Shipped. The three points this entry raised were settled like this:

- **Notify, or install.** Install. The editor stages the new build beside its
  settings, and a copy of that build started from there does the swap once the
  editor has exited — Windows will not let a running exe be overwritten. The
  plugin is replaced in the game folder first, while the editor is still up to
  say so if it fails, and only the files ModForge owns are touched.
- **Offline and rate limits.** The check is started and not awaited, and every
  way it can fail — no network, no releases, a rate limit, a repository that is
  not public — comes back as "no update" without a word. Only the manual check
  answers either way, because somebody asked it.
- **Opt out.** Read before the request, so off means no request.

One thing this depends on that is not code: **the repository has to be public**
for the releases API to answer. Until it is, every check finds nothing, exactly
as it does offline.
