# Roadmap

What is planned but not built. Items move out of here into `CHANGELOG.md` when
they ship.

---

## Next (1.2.0)

### ~~A UI tab~~ — done

Shipped. The question this entry said had to be settled first — **which parts of
the UI a pack should be able to touch** — was answered **both**, in one tab
rather than two.

The reason they share a tab is that they are the same job from an author's side:
a tree of objects, the properties of the selected one, and a picture of the
result. Splitting them would have asked an author to decide up front which kind
of thing they were making, when the honest answer is usually a bit of both — a
screen anchored to the game's own holds vanilla objects the pack has changed and
objects the pack invented, side by side, and both are equally the point.

What that cost, and what made it affordable, was storing only the differences on
a vanilla-based screen. An object nobody touches is not in the pack, so altering
one label in the shop stays a pack that alters one label rather than a copy of
the shop that breaks when the game is patched.

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
