"""Find every Canvas in a set of scene hierarchy dumps and describe what is on it.

WHY THIS EXISTS
    The game's UI is not in one place. Most of it hangs off the main gameplay
    canvas, but there are others - a canvas per minigame, a canvas for the event
    overlay, a canvas in the studio - and some of them are nested inside another
    canvas rather than being surfaces of their own. Before anything can be
    authored against that, the question "how many places can UI actually go, and
    what is already on each one" needs a counted answer rather than an
    impression formed by scrolling a ninety-thousand-line file.

    This reads that file instead of the game. A hierarchy dump lists every
    GameObject with its components and its active state, which is all a census
    needs, so this runs in a second and gives the same answer twice.

WHAT IT CANNOT SEE
    A dump is a photograph of a scene as it loads. A canvas that only exists
    once something instantiates a prefab is not in it and will not be counted -
    so treat the totals as "at least this many", and see the LEADS section,
    which lists the UI-sounding scripts found on objects that hold no canvas
    beneath them, as the likeliest place for one to be made later.

    It also has no geometry. Nothing here knows how big anything is or where it
    sits; that needs the scene open in Unity. This answers how many surfaces
    there are and what is on them, which is what decides how much geometry is
    worth extracting and from where.

USAGE
    python CanvasCensus.py <dump.txt> [<dump.txt> ...] [-o report.md] [--json out.json]

    Dumps are the per-scene hierarchy listings, in the format

        Scene: CoreGameScene
        --------------------------------------------------
        - SomeObject [self:True, hierarchy:True]
          * Transform
          * Canvas
          - AChild [self:False, hierarchy:False]
            * RectTransform

    where indentation is two spaces per level, "- " introduces a GameObject and
    "* " one of its components.
"""

from __future__ import annotations

import argparse
import io
import json
import os
import re
import sys
from collections import Counter, OrderedDict

NODE = re.compile(r'^(?P<indent> *)- (?P<name>.*) \[self:(?P<self>True|False), '
                  r'hierarchy:(?P<hier>True|False)\]$')
COMPONENT = re.compile(r'^(?P<indent> *)\* (?P<name>.+)$')
SCENE = re.compile(r'^Scene: (?P<name>.+)$')

# Unity's own UI vocabulary. Anything on a canvas that is NOT one of these is a
# game script, and those are the interesting half - they are what makes the UI
# behave, and a tab that only reproduced the Unity components would produce
# very accurate pictures that do nothing.
UNITY_UI = {
    'RectTransform', 'Transform', 'CanvasRenderer', 'Canvas', 'CanvasScaler',
    'CanvasGroup', 'GraphicRaycaster', 'Image', 'RawImage', 'Text', 'Mask',
    'RectMask2D', 'Button', 'Toggle', 'ToggleGroup', 'Slider', 'Scrollbar',
    'ScrollRect', 'InputField', 'Dropdown', 'Outline', 'Shadow', 'Selectable',
    'LayoutElement', 'ContentSizeFitter', 'AspectRatioFitter',
    'GridLayoutGroup', 'HorizontalLayoutGroup', 'VerticalLayoutGroup',
    'HorizontalOrVerticalLayoutGroup', 'EventSystem', 'StandaloneInputModule',
    'InputSystemUIInputModule', 'EventTrigger', 'PositionAsUV1',
    # TextMeshPro ships with Unity and is used here in preference to UI.Text.
    'TextMeshProUGUI', 'TextMeshPro', 'TMP_InputField', 'TMP_Dropdown',
    'TMP_ScrollbarEventHandler', 'TextContainer', 'TMP_SubMeshUI',
}

# Things that draw or arrange. Counted per surface to give a sense of weight - a
# canvas with four of these is a widget, one with nine hundred is a screen.
WIDGETS = ['Image', 'RawImage', 'TextMeshProUGUI', 'Text', 'Button', 'Toggle',
           'Slider', 'Scrollbar', 'ScrollRect', 'InputField', 'Mask',
           'RectMask2D', 'GridLayoutGroup', 'HorizontalLayoutGroup',
           'VerticalLayoutGroup', 'ContentSizeFitter', 'LayoutElement',
           'Outline', 'Shadow', 'CanvasGroup']

# Scripts whose name suggests UI is built or shown at runtime. Used only for the
# leads list, which is somewhere to look rather than something found.
#
# Deliberately narrow. Widening it to Dialog and Tooltip matched most of the
# game - both are everywhere and neither implies a canvas - and a thousand-row
# list of leads is the same as no list at all.
UI_SCRIPT_HINT = re.compile(r'(UI|Canvas|Screen|Menu|Popup|Panel|Hud|HUD'
                            r'|Overlay|Window)')


class Node(object):
    """One GameObject: its name, its components, and where it sits."""

    __slots__ = ('name', 'depth', 'active_self', 'active_hierarchy',
                 'components', 'parent', 'children', 'scene')

    def __init__(self, name, depth, active_self, active_hierarchy, scene):
        self.name = name
        self.depth = depth
        self.active_self = active_self
        self.active_hierarchy = active_hierarchy
        self.components = []
        self.parent = None
        self.children = []
        self.scene = scene

    @property
    def path(self):
        parts, node = [], self
        while node is not None:
            parts.append(node.name)
            node = node.parent
        parts.reverse()
        return '/'.join(parts)

    def walk(self):
        yield self
        for child in self.children:
            for node in child.walk():
                yield node


def parse(path):
    """Read one dump into its list of scene roots.

    Raises on a line it does not recognise rather than skipping it: a silently
    dropped subtree would take a whole surface out of the count and the count
    would still look plausible."""
    scene = os.path.splitext(os.path.basename(path))[0]
    roots, stack, current = [], [], None

    with io.open(path, encoding='utf-8-sig', errors='replace') as handle:
        for number, raw in enumerate(handle, 1):
            line = raw.rstrip('\r\n')
            if not line.strip():
                continue

            found = SCENE.match(line)
            if found:
                scene = found.group('name')
                continue
            if set(line.strip()) == set('-'):     # the rule under the header
                continue

            found = NODE.match(line)
            if found:
                depth = len(found.group('indent')) // 2
                node = Node(found.group('name'), depth,
                            found.group('self') == 'True',
                            found.group('hier') == 'True', scene)
                del stack[depth:]
                if stack:
                    node.parent = stack[-1]
                    stack[-1].children.append(node)
                else:
                    roots.append(node)
                stack.append(node)
                current = node
                continue

            found = COMPONENT.match(line)
            if found:
                if current is None:
                    raise ValueError('%s:%d: a component before any GameObject'
                                     % (path, number))
                current.components.append(found.group('name').strip())
                continue

            raise ValueError('%s:%d: unrecognised line: %r'
                             % (path, number, line))

    return scene, roots


def describe(canvas):
    """Everything the report says about one canvas, gathered once."""
    subtree = list(canvas.walk())

    # Descendants belonging to a NESTED canvas are still descendants, but
    # counting them here as well as under the nested canvas would count them
    # twice. Both numbers are kept: the total is the size of the surface, the
    # own count is how much of it this canvas is directly responsible for.
    nested = [n for n in subtree if n is not canvas and 'Canvas' in n.components]
    inside_nested = set()
    for other in nested:
        for node in other.walk():
            if node is not other:
                inside_nested.add(id(node))

    components = Counter()
    scripts = Counter()
    for node in subtree:
        components.update(node.components)
        for name in node.components:
            if name not in UNITY_UI:
                scripts[name] += 1

    nested_in = None
    walker = canvas.parent
    while walker is not None:
        if 'Canvas' in walker.components:
            nested_in = walker.path
            break
        walker = walker.parent

    return OrderedDict([
        ('scene', canvas.scene),
        ('path', canvas.path),
        ('name', canvas.name),
        ('depth', canvas.depth),
        ('isSurface', nested_in is None),
        ('nestedIn', nested_in),
        ('activeSelf', canvas.active_self),
        ('activeInHierarchy', canvas.active_hierarchy),
        ('hasScaler', 'CanvasScaler' in canvas.components),
        ('hasRaycaster', 'GraphicRaycaster' in canvas.components),
        ('canvasComponents', list(canvas.components)),
        ('descendants', len(subtree) - 1),
        ('ownDescendants', len(subtree) - 1 - len(inside_nested)),
        ('nestedCanvases', [n.path for n in nested]),
        ('directChildren', [
            OrderedDict([
                ('name', child.name),
                ('activeSelf', child.active_self),
                ('descendants', len(list(child.walk())) - 1),
                ('components', list(child.components)),
            ]) for child in canvas.children]),
        ('widgets', OrderedDict(
            (w, components[w]) for w in WIDGETS if components[w])),
        ('scripts', OrderedDict(scripts.most_common())),
    ])


def leads(roots, canvas_ids):
    """Objects carrying a UI-sounding script that sit outside every canvas in
    the scene. A canvas made at runtime leaves no trace in a dump; this is the
    nearest thing to a trace it does leave.

    Both halves of "outside" are needed. Skipping only objects with a canvas
    beneath them keeps every leaf inside a canvas, since a leaf has nothing
    beneath it at all - which is how this first came back with a thousand rows
    that were all ordinary widgets on surfaces already counted."""
    out = []
    for root in roots:
        walk_stack = [(root, False)]
        while walk_stack:
            node, under_canvas = walk_stack.pop()
            inside = under_canvas or id(node) in canvas_ids
            for child in node.children:
                walk_stack.append((child, inside))
            if inside:
                continue
            if any(id(n) in canvas_ids for n in node.walk()):
                continue
            hits = [c for c in node.components
                    if c not in UNITY_UI and UI_SCRIPT_HINT.search(c)]
            if hits:
                out.append((node.scene, node.path, hits))
    out.sort()
    return out


def report(surveys, write, sources=()):
    """The census, as Markdown."""
    canvases = [c for _, cs in surveys for c in cs]
    surfaces = [c for c in canvases if c['isSurface']]
    nested = [c for c in canvases if not c['isSurface']]

    write('# Vanilla UI census\n\n')
    if sources:
        # A copy of this report kept anywhere has to say what it was made from,
        # or the next person cannot tell a stale one from a current one.
        write('<sub>Generated by `Tools/Ui/CanvasCensus.py` from %s.</sub>\n\n'
              % ', '.join('`%s`' % os.path.basename(s) for s in sources))
    write('%d canvases across %d scene(s): **%d independent surfaces**, '
          '%d nested inside another canvas.\n\n'
          % (len(canvases), len(surveys), len(surfaces), len(nested)))
    write('A nested canvas is not a place to put UI - it is a batching or\n'
          'sorting split inside a surface that already exists. The surfaces\n'
          'are the number that matters.\n\n')

    # -- The surfaces -------------------------------------------------
    write('## Surfaces\n\n')
    write('`live` is whether the canvas is on when the scene finishes loading.\n'
          'Most are off, and are switched on by the game when their moment\n'
          'arrives - so "no" means dormant, not unused.\n\n')
    write('| Scene | Path | live | scaler | raycast | objects | children |\n')
    write('|---|---|:--:|:--:|:--:|--:|--:|\n')
    for c in sorted(surfaces, key=lambda c: (c['scene'], -c['descendants'])):
        write('| %s | `%s` | %s | %s | %s | %d | %d |\n' % (
            c['scene'], c['path'],
            'yes' if c['activeInHierarchy'] else 'no',
            'yes' if c['hasScaler'] else '-',
            'yes' if c['hasRaycaster'] else '-',
            c['descendants'], len(c['directChildren'])))
    write('\n')

    # Two surfaces can share a path: the game has sibling GameObjects with the
    # same name. Anything that addresses a surface by its path has to deal with
    # that, so it is said here rather than left to be discovered by whichever
    # of the two got picked.
    seen = Counter((c['scene'], c['path']) for c in surfaces)
    clashes = sorted(k for k, n in seen.items() if n > 1)
    if clashes:
        write('### Paths that do not identify one surface\n\n')
        write('Sibling GameObjects with the same name, so these paths match\n'
              'more than one canvas. A path is not a usable address for them.\n\n')
        for scene, path in clashes:
            write('- `%s` (%s) - %d canvases\n' % (path, scene, seen[(scene, path)]))
        write('\n')

    if nested:
        write('## Nested canvases\n\n')
        write('| Scene | Path | inside | objects |\n|---|---|---|--:|\n')
        for c in sorted(nested, key=lambda c: (c['scene'], c['path'])):
            write('| %s | `%s` | `%s` | %d |\n'
                  % (c['scene'], c['path'], c['nestedIn'], c['descendants']))
        write('\n')

    # -- Each surface, opened one level -------------------------------
    write('## What is on each surface\n\n')
    write('One level down only: on a canvas that is the list of screens,\n'
          'panels and strips it is made of, which is the granularity a UI tab\n'
          'would offer as a thing to change or add to.\n\n')
    for c in sorted(surfaces, key=lambda c: (c['scene'], -c['descendants'])):
        write('### `%s`\n\n' % c['path'])
        write('*%s, %s at load.*\n\n'
              % (c['scene'], 'live' if c['activeInHierarchy'] else 'dormant'))
        write('- On the canvas itself: %s\n'
              % ', '.join('`%s`' % x for x in c['canvasComponents']))
        write('- %d objects below it%s\n' % (
            c['descendants'],
            (', %d of them under a nested canvas'
             % (c['descendants'] - c['ownDescendants']))
            if c['nestedCanvases'] else ''))
        if c['widgets']:
            write('- Draws and arranges with: %s\n' % ', '.join(
                '%d %s' % (n, k) for k, n in c['widgets'].items()))
        if c['scripts']:
            top = list(c['scripts'].items())[:8]
            write('- Game scripts inside: %s%s\n' % (
                ', '.join('%d %s' % (n, k) for k, n in top),
                ' (+%d more kinds)' % (len(c['scripts']) - len(top))
                if len(c['scripts']) > len(top) else ''))
        if not c['directChildren']:
            write('- No children - an empty surface.\n\n')
            continue
        write('\n| Child | on | objects | components |\n|---|:--:|--:|---|\n')
        for child in sorted(c['directChildren'], key=lambda k: -k['descendants']):
            plumbing = ('RectTransform', 'CanvasRenderer', 'Transform')
            shown = [x for x in child['components'] if x not in plumbing]
            write('| %s | %s | %d | %s |\n' % (
                child['name'], 'yes' if child['activeSelf'] else 'no',
                child['descendants'],
                ', '.join('`%s`' % x for x in shown) or '-'))
        write('\n')

    # -- The vocabulary -----------------------------------------------
    write('## What the UI is built out of\n\n')
    write('Every component found under a surface, with the ones Unity ships\n'
          'separated from the ones this game wrote. The second list is the\n'
          'real specification: reproducing only the first would give shapes\n'
          'that sit there and do nothing.\n\n')
    unity, game = Counter(), Counter()
    for c in canvases:
        if not c['isSurface']:
            continue                    # already counted under its surface
        unity.update(c['widgets'])
        game.update(c['scripts'])
    write('**Unity**\n\n')
    for name, count in unity.most_common():
        write('- `%s` x %d\n' % (name, count))
    write('\n**This game**\n\n')
    for name, count in game.most_common():
        write('- `%s` x %d\n' % (name, count))
    write('\n')


def main(argv=None):
    parser = argparse.ArgumentParser(
        description='Census of every Canvas in one or more scene hierarchy '
                    'dumps.')
    parser.add_argument('dumps', nargs='+', help='hierarchy dump .txt files')
    parser.add_argument('-o', '--out',
                        help='write the Markdown report here (default: stdout)')
    parser.add_argument('--json', dest='json_out',
                        help='also write the full structured census here')
    args = parser.parse_args(argv)

    surveys, all_roots, canvas_ids = [], [], set()
    for path in args.dumps:
        scene, roots = parse(path)
        all_roots.extend(roots)
        found = [n for r in roots for n in r.walk() if 'Canvas' in n.components]
        canvas_ids.update(id(n) for n in found)
        surveys.append((scene, [describe(n) for n in found]))

    buffer = io.StringIO()
    report(surveys, buffer.write, args.dumps)

    hints = leads(all_roots, canvas_ids)
    if hints:
        buffer.write('## Leads: UI scripts with no canvas under them\n\n')
        buffer.write('Not findings. A canvas built at runtime is invisible to\n'
                     'a dump, and if one exists it is most likely made by one\n'
                     'of these.\n\n')
        buffer.write('| Scene | Path | Scripts |\n|---|---|---|\n')
        for scene, path, hits in hints:
            buffer.write('| %s | `%s` | %s |\n'
                         % (scene, path, ', '.join('`%s`' % h for h in hits)))
        buffer.write('\n')

    text = buffer.getvalue()
    if args.out:
        with io.open(args.out, 'w', encoding='utf-8', newline='\n') as handle:
            handle.write(text)
        print('Report written to %s' % args.out)
    else:
        sys.stdout.write(text)

    if args.json_out:
        payload = OrderedDict([
            ('sources', list(args.dumps)),
            ('canvases', [c for _, cs in surveys for c in cs]),
        ])
        with io.open(args.json_out, 'w', encoding='utf-8', newline='\n') as handle:
            handle.write(json.dumps(payload, indent=2, ensure_ascii=False))
        print('Census written to %s' % args.json_out)

    canvases = [c for _, cs in surveys for c in cs]
    surfaces = sum(1 for c in canvases if c['isSurface'])
    print('%d canvases: %d surfaces, %d nested'
          % (len(canvases), surfaces, len(canvases) - surfaces))
    return 0


if __name__ == '__main__':
    sys.exit(main())
