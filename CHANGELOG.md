# Changelog

## 1.10.1

The place preview is now where you work on a place's objects. Pick an object or a character in the picture or in the hierarchy beside it, and everything about it is in the panel next to it: move it, copy and paste it, send it to a recycle bin, give it a mask to move by, let it drift with the place, give it a light or an effect, and pick which part of it you are dragging. Lights and characters drift with the place as they do in the game. Wet characters drip as the game's own do, in two kinds, Wet and Shower. Components play in the preview as you edit them, and the preview can fill the whole screen. Many things the preview drew differently from the game now match it.

### Before you update
- **Wet characters and busts drip about four times as much.** The game's own wet look, Anna in her towel, is two sets of drops: a few thin ones and three times as many a little bigger and lower down. ModForge copied only the first, so every wet NPC and bust had a quarter of the drops Anna shows, and the faintest ones. It now copies both, so after you update and export, the wet characters in packs you have already made drip as Anna does. For an NPC that should stay lighter, set **Amount** below 1 in its Wet particles box on the NPCs tab (or, for one placement, on its Wet part in the place preview).
- **A place's objects now stay still unless you give them a mask.** Until now, an object without a mask of its own moved with the place's back layer's mask whenever the back layer had one (the game builds a place's objects from its back layer), and an object's own mask did nothing unless the back layer had a mask too. Your packs are converted as they open, and the editor tells you how many objects it touched. Objects that moved with the back layer's mask are set to **The back layer's mask**, so they keep moving as before. An object with a mask of its own that the game left still (its place's back layer had no mask, or it sits on one of the game's levels) is set to **Nothing (still)**, so it stays still as before; its mask is kept, and choosing **Its own mask** makes it move. The blank masks you may have put on objects or back layers to work around this still work, and are no longer needed. Packs exported with an earlier ModForge keep working as they did.

### New
- **Masks for a place's objects.** Pick an object in the place preview and choose how its picture moves under **Moves by**, in the panel beside the picture: **Nothing (still)**, **The back layer's mask**, **Its own mask, like a place** (water, leaves, curtains: one layer of how much) or **Its own mask, like a character** (bounce, wave and noise, with the jiggle's speed, strength and noise settings under it). **Edit** paints the mask in the mask studio, over the object's picture, with the place preview beside it showing every stroke as it lands. The place preview now draws objects moving by their masks, which it never did before. Works on your places and on the game's levels you extend. Checked in the game.
- **Two kinds of wet effect: Wet and Shower.** The game has two: Anna's towel look, a few thin white streaks over the body (called Wet in the editor), and the bathroom's shower, many fine blue-grey streaks falling from a small box over the head and lit by the room's lamps. Until now ModForge only had the first. Choose under **Kind**: for an NPC, in its **Wet particles** box on the NPCs tab; for a bust, in the outfit's **Particles** box. A bust's shower falls from the top of its picture, drawn unlit like the towel's drops, since the game never puts a shower on a bust and its lamps light a bust differently from a room. Both kinds are checked against the game in the preview.
- **Make a shower stop at the floor.** Pick a character's Wet part with the Shower kind and tick **Drops start along the top** to have them come from the top of the box only, like a shower head as wide as the box, and **Drops stop at the box's floor** to have each one disappear the moment it reaches the bottom of the box instead of falling off the screen. Drag the box's bottom side down to the floor. In the game nothing is drawn below the floor, frame after frame.
- **Tune a wet NPC's drops, and change them for one placement.** An NPC's **Wet particles** box on the NPCs tab now has **Kind**, a **Tint** the drops' own colour is multiplied by (with **Pick…** for a colour picker; a lower alpha makes them fainter), **Amount** (times as many drops), **Drop size** and **Fall speed**. Every place the NPC stands starts from those. To change one placement only, pick the character in the place preview, then its Wet part: the panel beside the preview has the same settings, empty meaning the NPC's (the kind reads **Same as the NPC**), plus the placement's own **Box width** and **Box height**. **Reset** there takes everything back to the NPC's. Checked in the game on a shower; Wet takes them the same way.
- **The NPCs tab's preview shows the NPC's drops.** With **Enabled** ticked in its Wet particles box, the preview draws the NPC's drops where a new placement puts them, alone or over the room behind, in its kind and look. With **Start active** unticked they are drawn faint, as the place preview draws something that is off when the place opens; with Enabled unticked there are none. Until now the box's ticks changed nothing in that preview.
- **Drag the sides of a wet NPC's drop box.** With the Wet part picked, each side of its box has a grip: drag one out or in and only that side moves, the side across from it staying where it is. This changes where the drops start and nothing else; the drops stay the size they were. (The Wet part's own scale still makes the box and the drops bigger or smaller together, as in the game.) The towel's box covers both of its sets of drops, so it reaches a little lower than its Wet part's middle suggests. Ctrl+Z takes a drag back. Checked in the game: a box made wide and low there starts its drops across all of it, at their usual size.
- **Wet busts drip in the bust preview.** An outfit with **Wet particle active on spawn** ticked now shows the game's drops over the bust, as dialogues show them: thin, faint streaks falling from a tall box round the middle of the bust (or, with the Shower kind, fine ones from the top of its picture), over the room behind or over the bust alone. Checked against the game.
- **While you paint a place's mask, you can see the layer through what is in front of it.** On a place's front or back layer, or an object, the place preview beside the mask studio draws everything in front of what you paint see-through, so you can watch the part you paint move behind it. The studio's own canvas still shows only the layer you paint. Pointing at a spot names what covers it in the game: in the game that is what shows there, not the layer. For example, a "near" layer cut from the same picture can sit over much of the front layer and look exactly like it. To make those parts move in the game, give the object in front a mask of its own.
- **The place preview shows where the player's view ends.** The game always shows the same part of a place, 19.2 by 10.8 units round its middle, on any screen (it stretches that to fit the screen). That is exactly what the preview shows when the picture fits the view. Your place's pictures are bigger: a 2048 x 1136 picture shows about the middle 83% across and 85% down, and the rest only comes into sight as the cursor drifts the place. Zoom out or drag the view and everything the player can't see is now a little darker, with a thin line round the edge of what they can.
- **Everything for a place's objects is in the place preview now.** The GameObjects list under the preview is gone. Its buttons are over the hierarchy beside the picture: **+ Object** makes an object in the middle of the view, picked and ready to drag, **+ Character** puts one of your NPCs in the place, and on a game level you extend, **Fill from vanilla hierarchy** is there too. Everything about an object is in the panel to the right of the picture when you pick it, in the picture or in the hierarchy. Double-click a problem in the issues list and its object is picked in the preview. A short note under each preview says where things are.
- **Preview parallax and Apply shaders stay as you left them.** Tick either one beside a place preview and it stays ticked on every place, every game level you extend and every pack, and the next time you open ModForge. Untick it anywhere and it's off everywhere. They still start unticked.
- **Lights drift with the place.** With **Preview parallax** on, a place's lights now drift with it as you move the cursor, as they do in the game. They used to stay put while the place moved under them. You choose how each light drifts under **Moves with**, beside the picture:
  - **Back**: with the place's back picture, like something far away (the moon, a street lamp seen through a window). New lights do this.
  - **Front**: with the front picture, so the light stays on the lamp, window or screen it shines from. Every light you made before does this, and so do the game's own lamps. Nothing about them changes.
  - **Own**: by a strength you set, starting from where the light drifts now.

  It's the light's object that drifts, so a picture on the same object drifts with it. The game's levels mostly have no back picture of the kind your places have, so on most of them there is no Back to pick and a new light drifts with the front. Checked in the game. The whole-place light (Darkness) lights everything evenly, so it has nothing to drift.
- **Characters can drift on their own too.** Pick a character standing in a place and the same **Moves with** is beside the picture: **Front** (the floor they stand on), **Back** (with the place's back picture, like someone far away), or **Own** (a strength you set). Their shadow, blink, reflection and anything they hold drift with them. Every character you placed before stays on Front, which is what they did before and what the game's own crowds do, and new ones start there too. Checked in the game.
- **See how far a free-shape light reaches.** A free-shape light is at full strength inside the shape you draw and shines out from all of its edge, fading the way a round light fades from its middle and rounding off at the corners. The preview now draws how far it reaches as a dashed line round the shape, and you can drag the knob on that line to make it reach further or less far. The setting beside the picture is now called **Reach** (it was Soft edge).
- **Everything about the object you pick in a place preview, beside the picture.** Click an object in the picture or in the list beside it, and the panel on the right now has all of its settings as well as the light and effect it had before: its name, Copy and Paste, sprite, position, turn, size, sort order, parallax, Start active, alpha, mask, components and Active when. Pick a character standing in the place and you get all of theirs: which NPC, name, where the body, shadow, blink and wet sit, Start active, sorting order, components and Active when. Ctrl+Z works on them as everywhere else. The objects inside it stay in the list beside the picture, where you can click them to edit them. The panel is a little wider to make room for the settings, so the picture next to it is a little narrower until you drag its corner.
- **Keys in the place preview.** Click the picture or the hierarchy beside it, and then:
  - Ctrl+C copies the object you picked, with everything under it.
  - Ctrl+V pastes it under the object you picked, or among the place's own objects when nothing is picked.
  - Ctrl+D duplicates the picked object beside itself.
  - Delete sends it to the recycle bin.

  The copy you paste or duplicate is picked, ready to drag. Characters standing in the place work the same way, and so do the game's own objects:
  - A copy of one of the game's lights, like a place's Light 2D, is a light of your own with the same settings, standing where it does.
  - A copy of any other object of the game's is a copy of the game's object, drawn in the preview as the original is.
  - Delete switches the game's object off where it stands, since a pack can't take it out of the game, and the bin switches it back on.

  The NPCs object is the one place every character goes under, so it can't be copied or deleted; paste characters under it. While you work in the picture, Ctrl+D no longer duplicates the whole place in the list. Hover over the Hierarchy heading for a reminder of the keys.
- **A recycle bin in every place.** Whatever you delete from a place goes to the recycle bin at the bottom of the hierarchy beside the preview, with everything that was under it. That's true whether you press Delete in the preview or use the remove button in the panel beside the picture.
  - ↶ puts it back where it was, under the same object.
  - ✕ deletes it for good.
  - Empty the bin clears it.

  The bin is saved with the pack and left out of the mod you export. The game's own objects can't be taken out of the game, so removing one switches it off where it stands, and the bin switches it back on as it was. Before, removing one of a game level's objects only dropped what your pack changed about it until you next opened the pack.
- **Copies of the game's objects show in the preview.** A copy of one of a game level's objects was a row with nothing in the picture. It's now drawn where the game puts it, with the original's picture.
- **A floating component: FloatingSprite.** Add it to an object (or a character standing in a place) and it floats up and down smoothly for ever, everything under it with it, for monsters in vats, something bobbing in water, a hovering light. Its settings: **Height** (how far it travels), **Time** (seconds for one rise and fall), **Sideways** (an extra slow sway, 0 for none), **Start at** and **Random start**, so several side by side don't move in step. Checked against the game.
- **Components play in the place preview, live as you edit them.** What a component does to the picture now shows in the preview while you change its settings: Fade in and Fade out (replayed after a short pause, so you can watch them; the game plays them once, when the object appears), Blinking, Floating, Random child (a new pick every few seconds here; the game picks once) and, of the game's own, ConcertLightSwing, CarInteriorShake (once its Shake on start is ticked), RandomSpriteBlender and CinemaSpriteChanger. Where the game picks at random, the preview's picks are its own. Each was checked against the game. OffsetScrolling stays still in the preview because it does nothing to a picture in the game either; its note in the list now says so.
- **Every component in the list says what it does.** The list of components now has a short line beside each one, the game's own scripts and Unity's included, not only ModForge's. Its groups are now called **ModForge**, **Vanilla** and **Unity**.
- **Show a place preview over the whole screen.** Press **Full screen** beside the preview's heading (or F11 on the Places tab): the picture fills the whole screen, with the hierarchy and the picked object's settings over its left and right sides, and the preview's tick boxes and quality in a panel under the hierarchy. Everything works there as on the page, keys and Ctrl+Z included, and picking another place in the list shows it. Esc, F11 or **Leave full screen** in its corner brings it back, as does going to another tab. It works for your own places and for the game's levels you extend.
- **An object's light and effect are parts of it to pick, like a character's Shadow and Blink.** Pick an object with a picture and a light, or with an effect, in the place preview, and chips over its handles choose what they are for. **Object** moves, turns and sizes the object itself; until now a light's handles were drawn over the object's, in the way of them. **Light** shows only the light's handles, its reach, how far it stays full and its cone, for the light as it shines from the object; clicking its sun in the picture picks it too. **Effect** draws a box round where the effect's particles start. Drag inside the box to move the effect away from the object (it still moves, turns and grows with the object), and drag a corner or a side to make it bigger or smaller: that is its **Scale**, and the particles go on playing at the new size while you drag. Until now an object's effect always played exactly at the object. A light on its own, with no picture or effect, keeps its handles together as before. Checked in the game: the effect stands where the box put it, at the size it was given.

### Fixed
- **An object's Reversed now does what it says in the game.** In a place with **Reverse back** ticked, an object drifting by a strength of its own drifted reversed in the game whether its **Reversed** was ticked or not, while the place preview drew it going the other way. Unticking it changed nothing, and a "not reversed" written into the pack by hand was removed the next time you saved. Now the tick shows how the object really drifts. In such a place an object you never changed it on shows it ticked, because that is what the game does, and unticking it makes the object drift forward in the game as well. Objects you leave alone drift in the game as they did. The same goes for a light's **Moves with: Own** and for characters set to drift, and picking **Back** now makes a light or a character drift exactly as the back picture does, its direction included.
- **The player's room's buttons show in the place preview.** A place with a copy of the game's room buttons (Sleep, Calendar, Skip to Friday and the PC's Sit down), as the Androids Bedroom has, showed none of them in its preview, and neither did the player's room itself when you extend it: they are not pictures but buttons on a canvas over the screen, which the preview never drew. It now draws them under the game's interface as the game does, moved and switched off as your place says, and a moved button's handles sit on it and move it in the canvas's own pixels. Checked against the game on a copy of the room's buttons. Other levels' buttons on such canvases (the shrine's, the car's, the barn's) are drawn the same way.
- **The place preview's side panels are one width for every place.** The hierarchy was as wide as the current place's longest name, so the picture and the settings beside it jumped about as you went from place to place. It is now as wide as the longest name in any of your places (or, for the game's levels you extend, in any of them), and stays put.
- **Zoomed in, characters show their own pixels.** In the place preview a character was drawn smoothed when you zoomed in, a soft blur of its art beside the crisp pixels of the place, and with Apply shaders on it could stay at the small size it had before the zoom for many seconds. Zoomed in past their own size, characters are now drawn at their art's full resolution, pixel for pixel, like the place's own art.
- **A place preview dragged bigger than 1920 pixels wide no longer cuts off its right and bottom.** Past that size the picture of the place slid right and down inside the preview, leaving a dark band at the left and top and cutting off the right of the interface and the navigator strip. It now fills the preview corner to corner at any size.
- **Wet NPCs' drops in the place preview look like the game's.** The preview drew a spray of round blue drops that had little to do with what the game shows. The game's towel effect is thin, faint streaks: eight drops a second in two sizes, each starting anywhere in a tall box round the NPC's Wet part and falling straight down, faster and faster, so only a dozen or so per NPC are on screen at a time. The preview now draws exactly that, as bright and as see-through as the game does, in front of everything in the place. As in the game, scaling the Wet part scales the box and the drops, turning it turns the box (the drops still fall straight down), and the NPC's own size changes neither. Checked against the game. They cost next to nothing: with nothing else moving, the place is drawn again sixty times a second for them and no more. The drops' box is bigger than the old marker suggested: the game makes it 3.21 by 8.45 units times the Wet part's own scale, however small the NPC is, and the old marker used the numbers of a different effect, shrunk with the NPC. Pick an NPC's Wet part and the preview now outlines the box its drops start in, and rings each of the towel's drops as it falls, so the faint streaks are easy to follow.
- **Characters in a new place stood where the game puts them.** A new place's NPCs object says its size is 1, and the game then keeps the size of the NPCs object it builds on (0.96), so every character was a little smaller in the game than in the preview, and a little nearer the middle. The preview now does what the game does. Places whose NPCs object you resized were already right. The wet drops also sat too far from the middle of the place, away from their NPC; they are now where the game draws them. Both checked against the game, with an NPC nested in groups as in a shower scene.
- **The game's interface over the place preview is the game's own.** It was a picture cut from a screenshot, slightly the wrong size, with the navigator's buttons drawn over it by hand. Now the money, the day, the mood and the navigator are drawn the way the game draws them, with your place's buttons in the navigator as the game makes them: the same size and spacing, the second row and its dark strip past six buttons, a button without words named after where it goes, and numbers past 9 as the game shows them. The small line under a button's words is gone (the game only shows it while the mouse is over the button), and the words are the game's size. Checked against the game.
- **Objects with no picture of their own drift in the game when their Parallax is on.** A group of objects, or an object you made a light, had nothing to drift with in the game and stayed with the level, while the preview drifted it. They now drift as the preview shows. On a game level you extend the same goes for any object of yours with a strength of its own.
- **The NPCs object's Parallax works in the game.** Ticking Parallax on a place's NPCs object drifted every character in it in the preview, but in the game they stayed with the level. They now drift together in the game as the preview shows.
- **Objects that "Drift with the level" drift by the right amount in the preview.** The game gives them the place's back picture's drift. The preview always used 0.75, so with the back picture set to anything else it showed them drifting by the wrong amount. On a game level with no back picture to drift with, they don't drift at all in the game, and the preview now shows that too.
- **Clicking in the place preview no longer scrolls the page.** Picking a row in the hierarchy, or grabbing an object's handles, scrolled the page whenever the preview stood partly out of sight.
- **The name over the panel beside the place preview keeps up with the object's name.** It showed the name from one change earlier: after renaming, or after an undo, until you clicked something else.
- **A character you picked in the place preview stays as it was when Ctrl+Z takes back its name or its NPC.** Rename a character standing in a place, or give one with no name of its own another NPC, then press Ctrl+Z: the character was unpicked, its settings and handles gone from beside the picture. It now stays picked, as it does for every other change. And with its **Reflection** part picked, choosing an NPC without a reflection showed the body instead, as it should, but Ctrl+Z left it on the body; the reflection now comes back with the NPC.
- **An object you had picked in the place preview comes back picked when Ctrl+Z puts it back.** Remove the picked object (or undo the + Light that made it) and then Ctrl+Z (or Ctrl+Y), and it is picked again, its settings beside the picture. It used to come back unpicked.
- **Picking one of two objects with the same name in the place preview picks that one.** With two objects of one name side by side (a "Lamp" picture and a "Lamp" light, say), clicking the second showed the first one's handles and settings. Renaming one of them could move the pick to the other, and removing the picked one picked the other instead of nothing.
- **Ctrl+Z and Ctrl+Y stay where you are when what they change is in front of you.** Undo something you dragged in the place preview and the page went down to the object's entry in the list under the preview, away from the picture you were working on. It only goes to the change now when you can't see it anywhere on screen. Undoing something you typed in an object's entry while that object is picked no longer jumps up to the panel beside the picture either, and changing what kind an action is and pressing Ctrl+Z with the list near the bottom of the window no longer scrolls the page down to its fields.
- **A place's mask moves the part of the picture you paint it on.** The mask editor showed a place's picture squashed into the middle of the canvas, with empty bands above and below, but the game spreads the mask over the whole picture. So what you painted moved a stretch nearly twice as tall and nearer the middle, and the top and bottom fifths of the picture could only be reached by painting the empty bands. The picture now fills the canvas at its real shape, and a stroke moves exactly what it covers, in the place preview and in the game. A mask the size of the picture made in another program, opened in the mask editor and saved, also kept its shape squeezed into the middle; it now stays where it was. This had been wrong since 1.1.0. Masks saved in those versions keep moving what they moved before, and the editor now shows where that is.
- **An NPC's or a bust's mask moves the part of the picture you paint it on when the picture is not square.** The mask editor showed a pose taller or wider than it is square squeezed into the middle of the canvas, with empty bands beside it, but the game spreads the mask over the whole pose. So a stroke moved a stretch nearer the middle, and a mask the size of the pose made in another program came back squeezed when saved. In the Busts tab's preview, a square mask on a bust that is not square was laid over the empty bands around the bust as well as the bust, so its edges moved the wrong rows. Both now put the mask where the game does. Square pictures are not affected.

## 1.10.0

Lights for your places, made and shaped in the place preview, lighting the place or your characters too, and darkness to turn a place from day to night. All checked against the game: the preview lights a place, and a character standing in it, as the game does. The place preview now keeps up with your screen with everything ticked, and dragging in it is smooth. Undo and redo were rebuilt: one history for the whole editor, quick on a large pack, and tried on every box, list, tick, slider and button of every tab.

### Before you update
- **The tutorials' practice art no longer goes into exported packs.** ModForge's plugin now carries the same pictures and sounds, and the game reads them from there. If your pack uses any of them unchanged (anything in its TutorialArt folder that you didn't edit), players need this version of the plugin to see them.
- **Ctrl+Z in a text box now takes back the editor's last change, as it does everywhere else.** Until now a text box answered Ctrl+Z itself, from its own memory of what had been typed in it, and the editor only found out afterwards. What you type is still taken back a part at a time: a run of letters, a run of deleting, each place you moved the caret to, each thing you pasted. See **Undo and redo, rebuilt** below.
- **Objects your pack adds to a place now show further from its middle in the preview, where the game has always put them.** The preview drew them too close to the middle, by up to a fifth of the way out. Nothing in your pack changes, and nothing changes in the game: only the preview now matches it. If you placed something by eye in the preview and then moved it to look right in the game, it was already right.

### New
- **A VFX tab: effects for your pack.** Make an effect from a template and change what you like on it. The templates are the game's own effects (a campfire, a candle, fireworks, a portal, rain and snow, fog, falling leaves, hearts rising, a burst of gift hearts, torn paper flying, a magic poof, bats in dark smoke, a golden aura, and more), each tried in the game away from where the game keeps it, or ModForge's own: **Burst**, **Rising**, **Falling** and **Drifting** take any picture of yours, and there are a **Flash** of the screen in any colour and **Floating words** ("+[PV:fans] Fans") in the game's pop-up font. You set the picture, colour, how many, size, speed, how long each lasts, how strongly they fall, where on the screen and whether they are drawn behind or in front of the characters. The preview plays it over the game's bedroom as you change it.
- **The social points a Starmaker photo gets.** The game's follower icons and pink hearts that burst out of a photo and fly off are templates too: make new ones with any picture, or tick **Change the game's own instead of adding one** to change every photo's - a picture of your own, a colour, three times as many, quicker. Checked in the game.
- **Effects play when you say.** A **Play effect** action plays one (on a line, a choice, a screen's button, a rule; it can say where on the screen this time), and **Stop effect** stops it. Or an effect watches the game by itself: every time its conditions become true, while they are true, or **every time something under an object turns on** - pick 4_CG_Manager-Photos and it plays for each Starmaker photo shown, even one right after another.
- **Effects in your places.** Click **+ Effect** over the place preview's object list (beside **+ Light**) and an object playing an effect appears in the middle of the view: fog to start with, ready to drag where you want it. Beside the picture you pick what it plays - the game's own level effects (a campfire, a stove's flame, a candle, light orbs, fog, chimney smoke, rain, snow, falling leaves, seagulls, bubbles, sparks, wandering souls, fireworks, a portal) or ModForge's, with a picture of yours - and its picture, colour, how many, size, speed and how long each lasts. The preview plays it at the object. In the game it shows whenever the place does, and since it is an object like any other, a dialogue or a condition can switch it on and off. Works on your own places and on the game's levels you extend. **Keep seagulls** stays as it was.
- **"Anything under it" for the object condition.** The condition asking whether an object is on can ask instead whether anything under it is: one of the game's photos under 4_CG_Manager-Photos, which is always on itself.
- **New checks:** an effect nothing plays, one that watches an object with none named or conditions it hasn't got, and a Play effect naming an effect the pack doesn't have.
- **A start page.** ModForge now opens on a page with two big buttons, **New pack** and **Open pack**, and the packs you opened lately below them. The tabs appear once a pack is open. A pack that has gone (moved, renamed, on a drive that isn't plugged in) stays on the list, greyed, with an **x** to take it off. The menus work as before.
- **Pages come in with a short movement.** Switching tabs, opening a pack and opening the mask studio fade the page in, quickly; in the EX looks it also rises a few pixels. Nothing waits for it. Turn it off under **Options ▸ Animations**; it is also off whenever Windows' own Animation effects are off.
- **New asks for the name and the folder first, and saves the pack there at once.** So a new pack always has somewhere to save, and Save never has to ask where. Pick a folder: if it's empty the pack goes right in it; if it already has anything in it, the pack gets a folder of its own name inside it, so nothing of yours is mixed in or written over. The window says where the pack will go before you press Create.
- **Lights for your places, made in the preview.** Click **+ Light** over the place preview's object list and a light appears in the middle of the view, ready to drag where you want it. Beside the picture you set its colour, strength, how it fades (Falloff) and its glow. In the picture you drag how far it reaches (the outer circle), how far it stays at full strength (the inner one) and, for a spotlight, its cone's edges. Turn the light with Rotate to point a cone. Add as many as you like. A light is an object like any other: dialogues and conditions switch it on and off, and any object can carry one (select it, then **Give it a light**), so a lamp picture can shine.
- **Lights of any shape.** Next to **Round**, a light can be a **Free shape**: a shape you draw point by point in the preview, with a soft edge around it. Drag a corner to move it, drag the **+** halfway along a side to add one, right-click a corner to take it away. Or a **Picture**: a picture of your pack becomes the light, its colours and see-through parts included, like sunlight falling through a window.
- **Lights can light your characters too.** Tick a light's **Lights the characters too** and it brightens and tints the characters in a dialogue where it reaches, with its glow over them. Left off, as for nearly all of the game's lamps, a light lights the place and the NPCs in it, and characters only catch it at their edges.
- **See a character standing in your place.** Choose one of your characters (or one of the game's) under **Character in the place**, beside the place preview. They stand where the game shows a character in a dialogue, lit by the place's lights and casting the starting light's shadow, as in the game. Move or change a light and you see it on them. Checked against the game: the character and the place around them match the game's own picture.
- **Darken a place, and turn day into night.** Click **+ Darkness** over the place preview's object list and the whole place gets darker, so its lights stand out: a night, a dim cellar. Beside the picture you choose its colour and how dark it is (1 is the place as the game has it). Darkness is an object like any other, so a dialogue or a condition can switch it on and off, turning the same place and art from day to night. Characters in a dialogue keep the game's full light unless you tick **Darkens the characters too**. In the game, a place takes over the base light only while it is shown and gives it back when you leave. Any light can also be made a **Whole place** light this way, to tint a place rather than darken it.
- **The light every place of yours starts with is now one of them.** Your places are built on one of the game's levels and keep its faint blue light. It is listed with your objects as **Light 2D**, so you can change it like your own lights, or remove it. Removed, it stays off in the game, and **Back to the game's** brings it back. Left alone, nothing about your place changes.
- **The game's own lamps can be changed in a vanilla extension.** Select a lamp in the preview's list to change its colour, strength or reach, or remove it.
- **A character's name you type in a line becomes their name by itself.** Type a name and carry on, and once the word is finished (the next word typed, the caret moved away, the box left) it turns into that character's name: shown in their colour, it follows a new name you or another pack give them, as the name button's names always have. Every character counts, yours and the game's, as long as only one of them goes by that name; a name like "the Cop" that describes somebody is left alone, and so is a stutter ("A-Anna"). To keep a name as plain words, click the small **x** on its top right corner: it goes back to the words you typed and stays so in that line. Ctrl+Z straight after gives you the word back as typed. Only what you type is changed: names already in your lines stay as they are until you type next to them, and Validate still lists those for you.
- **Preview parallax follows the mouse over your whole screen.** With **Preview parallax** ticked, the place drifts with the cursor wherever it is on the screen the editor is on, not only while it is over the picture. That screen stands for the game's: with the cursor in its middle the place is as you made it, and at its edge it has drifted as far as a full screen of the game takes it. On another monitor it drifts on further, as the game's does when it is full screen and the mouse is on a second monitor. Before, the drift stopped as soon as the mouse left the picture, and a small move inside the picture was a large one in the game.
- **New checks:** a free shape with fewer than three corners, or a picture light with no picture, is reported, as the game leaves such a light out. Two darkness objects always on in one place are reported too, as only the last one switched on is used.
- **The tutorials tell one story: "Dummy Days".** In an empty gallery downtown stands a mannequin who comes alive when nobody is looking and dreams of being a Starmaker model. Each tutorial adds a piece of her story to one mod - her gallery, her spotlight, her first words, her moving face, the other mannequins, her music, what she remembers, her portfolio, her quest, her first Starmaker photo and effects for her - and after each one the mod loads in the game. Start it from **Learn ModForge** on the start page.
- **Each tutorial still stands on its own.** One that carries on from earlier ones offers **Bring in what came before**: it opens your practice pack (or makes one, in Documents\ModForge Tutorials) and does the earlier tutorials' steps for you. Your own packs are never touched.
- **Shorter steps, the rest folded away.** Each step says what to do in a sentence or two; **Tell me more** opens the whole explanation under it.
- **Do it for me.** A step you'd rather not do by hand is done for you, the way it says - then look at what changed. Ctrl+Z takes it back.
- **A tutorial is a panel beside your work now, not a box over it.** It docks on the right while the tutorial runs, with the tutorial's picture and how far along you are at the top, the step on a card, the buttons always in reach, and every step listed below it - ticked when done, click one to go there. The ring still shows you the control a step is about, and the rest of the window is dimmed, but nothing covers what you are working on any more. Steps slide in gently; the bar fills as you go. Classic and EX each draw it their own way.
- **A step arrives with what it is about already chosen**: the line whose actions it talks about, the light whose colour it changes, her outfit open in the list. Before, many steps opened on an empty box and left you to find the line yourself.
- **Go to any step.** Click one in the coach's list. At the end, **Next tutorial** starts the following one.
- **The ModForge tab, reworked.** It was the editor's oldest page - two columns of fold-outs - and now has pages along its top: **Home** shows your pack (its name, version and language, and whether Validate found anything), the tutorial to take next with how far through them you are, and four steps from a new pack to playing it in the game. **Tutorials** lists them with a picture each. The **Guide** reads like a book: its topics down the side, one topic at a time, Previous and Next at the bottom. **Issues** has the list Validate makes. Classic and EX each have their own look.
- **Packs made in the tutorials are 13 MB lighter.** The tutorials still put their practice art in your pack's TutorialArt folder, so you pick and preview it as before, but Export leaves out every file there that is exactly as ModForge ships it: the plugin in the game has its own copy. A picture you painted over, or a file of your own in that folder, is yours and is exported as usual. The export's message says how many it left out.
- **Each tutorial says what it is about before you start it**: what it adds to your pack ("Adds: a light with a cone, and darkness over the rest of the place") and the tabs it works on. The Going deeper tutorials are listed under the tutorial they build on, indented, instead of in a group of their own. A tutorial that changed since you finished it now offers **Take the new version** (it said "What changed", and only started it again). Rest the mouse on a tutorial for half a second and its card opens to show how it begins and every step in a line; move away and it folds again.
- **One search on the ModForge tab, for the tutorials and the guide.** Type a word - a control's name, say - and the page shows what mentions it: the tutorial steps, each with **Start here**, the tutorials, and the guide's topics with what they say there and **Read the whole topic**.
- **New art for the tutorials and the ModForge tab**, drawn in the game's style for the Dummy Days story.
- **Going deeper: new tutorials** that practise what the first ones only touched, in the same story. **Actions, in depth** - her confidence climbs with each visit (Increment), her song plays when you're sweet (SwitchMusic), a Wait, her portfolio opens (a UI screen switched on), her quest's photo task is completed (Quest). **Conditions, in depth** - comparing numbers, looking in a list, a daily chance, Negate, an OR group on a new wallpaper, and her photo held back until she is sure of herself. **Rules, in depth** (the Integration tab) - a rule for each new day, her mood decided by one rule with else-if branches, hearts on a Timer while you are in her gallery, one rule for both mannequins with Repeat for each, and how to see what a rule decided. **Characters, in depth** - an outfit for each of her moods, a line that dresses her by her Mood, her name's colour. **Places, in depth** - a poster of her that goes up once she is braver, a gallery visitor (a second kind of NPC) who comes once she is sure of herself, and what is drawn over what. **Dialogues, in depth** - a line picked at random, a line that moves on by itself, a word in bold, menus the player returns to, the dialogue's own settings and the game's own conversations. **Scenes, in depth** - two more scenes of her, one a Starmaker photo with a kiss, shown by her mood; the pose she shows you unlocks her second wallpaper, and the story's pieces all work together.
- **Nine more Going deeper tutorials, so every kind of thing ModForge makes is made in one**, each listed under the tutorial it builds on:
  - **Rules that watch** — values that reset each day or each visit, a rule for when you leave, one that holds while it must, rainy days, a key you press.
  - **Her luck** — lines that play on a lucky day, at random, after three poses, with Negate, and a test conversation on the Talk button.
  - **Changing the game, gently** — the weather, the game's door buttons dimmed and put back, a camera flash, a walk to your room, a shortcut, and one line added to the game's own words.
  - **Her gallery moves** — move, spin, fade, swap and grow what is in a place.
  - **Lists, chance and other conversations** — pick, count, remove, empty, roll dice, play one conversation from another.
  - **More voices** — a voice with no bust, a character in one of the game's busts, one of the game's characters given a colour.
  - **Her numbers and the game's journal** — a screen always on, a counted task, and her confidence rising with the game's photographer quest.
  - **Places, more** — indoor weather, a blinking sign, flowers that change each visit, a copy of a game effect, lights of any shape, fades, and a visitor who comes in from the rain.
  - **Effects, more** — glow, effects that play by themselves (while you are in a place, on every Starmaker photo, the moment her confidence reaches 10), the game's own follower points turned pink, and a flash on one of the game's photos.
  New practice art comes with them.
- **The guide shows where things are, the way a beginner's tutorial does.** Every section of a guide page has a picture of the part of the editor it is about, with a red box round each control the section talks about, numbered - and the same red number sits beside that control's line in the text, so the words and the picture point at each other. Where there is one thing to click, a red arrow points at it; a menu is shown open, its item boxed. Every page has at least two pictures, in the look you use. The pictures of the tabs no longer come out squeezed sideways (Map Buttons' did).
- **Examples in the guide**: twelve step-by-step recipes after a Before you start page, each step with a picture that boxes what to click in the order you click it - a greeting once a day, a choice she remembers, a line for rainy days, music in a place, a button on screen with a sound, a visitor on rainy days, counting visits, a quest with two tasks, a wallpaper as a reward, a room that opens later, renaming one of the game's characters, a Starmaker photo of your own. Between them they use every tab, and each one is built in the editor and checked before a release, so the steps keep working. Every example was then followed word for word, by a check that drives the editor only by what the screen says: Before you start now has you name your character and your place and choose the character's pronouns, steps that need a conversation say how to make one, and names are written the way the lists show them.
- **A page of the words a beginner meets** opens the guide - the game's own (vanilla), place and level, bust, NPC, sprite, line and node, condition, action, variable, rule, runtime name. The guide also says now how to try a pack in the game the first time, what size pictures are drawn for, that a dialogue's first start condition is the place it plays in, and that a child object stands at its own X and Y.
- **The FX tab is now VFX**, beside SFX.
- **The number of issues stands out.** On the ModForge tab, beside Issues, and on the ModForge tab itself in Classic, the count is a notification pill: red when something will not work, amber when there are only warnings, muted when there are only notes, and gone when the pack is clean. EX's rail badge takes the same colours.
- **The guide has pictures, and a topic for the UI tab.** Each tab's topic shows the tab, in the look you use. The UI tab, which the guide never covered, has a topic of its own; the Places topic now covers lights, darkness, effects and picking in the preview; the ModForge tab's topic describes its new pages.
- **Her portfolio has a cover**: the screen tutorial gives the Cover object its picture.
- **A dropdown says what its word stands for.** Boxes like an action's Level or Target show the word the pack stores - place:TheGallery, vanilla:5_MyRoom, HerFirstPose - while their lists show names. Now the name is written faintly at the box's right: *The Gallery*, *Her first pose*. It steps aside while you type in the box, and never covers what is in it.
- **The guide covers what it missed**: the Options menu item by item under the menu's own headings, Load this pack on start, Publish for players and the start page, the list search box, Copy layers from in the mask painter, the game's characters (renaming them, their voice, colour and faces, Reset character), = default, Fade in, where an NPC's blink, shadow and wet sit, Active when and child objects in places, navigator button rows, dragging lines, a line's Sounds and its translation ticks, Edit the sound, PlayEffect and StopEffect, Where it's used for variables, the NPCs and UI targets, and the game's Mods list. It also stopped saying a few things that were no longer true: that dialogue condition debugging needs a developer build, that the player's typing voice can be changed, and that you must restart the game to see a new export (going back to the main menu and loading a save is enough).
- **Learn ModForge as a page and a PDF**: the whole Dummy Days story, every step with a picture of it, then the guide.
- **A quest action's Task row could stay hidden.** Filled in by a tutorial's Do it for me, an action set to complete a task showed no Task box, so it looked as if no task was chosen. It shows now.
- **The tutorials' story holds together now.** Her confidence used to stay at 1 for good, her portfolio never opened, her quest's second task could never be done, and the list of poses she showed you was never read; the new chapters make each of them work. Her gallery's Home button now gets the game's music, so her track no longer follows you home, and the mannequins called Left and Right stand on the left and the right.
- **The tutorials no longer leave Validate anything to complain about.** The new chapters' announcer and gallery owner had no pronouns (two errors), the hum's Random chance was written as 30 instead of 0.3, and the Starlight effect was never played. Each chapter now leaves a pack Validate passes, and that is checked.
- **The tutorials, read by beginners.** Three readers went through every tutorial and the whole guide as people new to modding, and what tripped them up is fixed: control names that did not match the screen (+ Root is + New Node, Mouth prefix, Weather is, Target alpha, Total time (s), To level, Return to original, Spinning, Followers and more), shorthand spelled out the first time, words explained where they first appear (level, node, sprite, vanilla), hints for the 30 steps of the new chapters that had none, and the guide's own contradictions (renaming, [PV:] in a line). In the new chapters, things that did not work as told now do: the poster's swap waits for its fade, her confidence runs to 10 everywhere (a line said "7 out of 5"), her portfolio closes as you leave, she says her favourite pose aloud, the gallery owner speaks, the confidence badge is a small badge in a corner instead of a panel across the screen, counted poses are kept once each, the line about visits is right the second time you come, and the visitor's fade-out is no longer undone by Active when at once. A second round of three readers went through every step again, against a picture of the screen at that step: about seventy more wordings put right - where to scroll, which of two boxes, names as the screen shows them, steps that assumed what they had not said - and the story's loose ends tied (her hummed line only after the hum, no favourite pose before the first).
- **Her gallery has a wall to hang things on.** It was a corridor running away from the viewer, with no wall facing you for her poster, the neon star or the moonlit window. It is now a room seen front-on, its walls bare for what the tutorials hang there, a tall window with the moon on the right. Her posters hang at a size that fits the wall, and the flower pots stand on the floor.
- **The Random condition's chance is 0 to 1**, and its tip and the guide say so: 0.3 passes three times in ten. The tip used to call it deprecated, though lines and a place's On enter and On exit, which ask once, are where it belongs.
- **Tutorial steps say what is really on screen**: the ... button (there is no Browse), the panel beside the place preview (not a toolbar on it), Switch Activate, a new scene's Sound starting as Silent, and the words to type in quotes.
- **The ModForge tab's Getting started was out of date.** It still said to save a new pack before anything else, and to put the exported file in BepInEx/plugins/SMSModForge/ModPacks. New saves the pack at once now, and the game loads packs from the Mods folder beside its .exe; it says so.
- **Small movements all over the editor, each look its own.** Buttons give a little as you press them, ticks and dots come on instead of just appearing, the arrows of trees and folds turn, what a fold opens comes in, the open tab's bar grows out, tooltips and right-click menus come in, menus open (Classic fades them in, EX slides them out), a list row you choose takes its colour in (with EX's accent bar growing down it), a tutorial card on Home rises a little (Classic) or brings its glow up (EX) as you point at it, and the issue count swells when it goes up. The mask painter slides in from the right as the tabs slide away, and back out the other way when you close it: Classic a short slide with a fade, EX the whole width across. Classic's are short and plain; EX's spring back a little further, slide where something appears, and bring their glow up as you point at them. Nothing waits for them - a click in the middle of one lands at once - and Options > Animations, or Windows' own animation effects, turns them all off. A tick or an arrow on a row a list reuses as you scroll stays still: only what you use moves.
- **Guide pictures open at full size.** Click a picture in the Guide (a magnifier shows when you point at it) and it opens over the window. The wheel zooms where the pointer is, a drag moves it, a double-click goes between fitting and its actual size; + and - zoom, 0 fits, 1 is actual size, and Esc or a click beside it closes it.
- **Guide pictures are sharp.** They now ship lossless and at the size they were taken. They were compressed JPGs cut down to 820 pixels wide, which smeared the red boxes and the editor's small words.
- **Everything new is translated.** The tutorials, the guide and every text new in 1.10.0 read in all eight of the editor's languages, machine-translated like the rest. In every language, the names you type in a tutorial stay in English, as the tutorial checks them.

### Undo and redo, rebuilt
- **One history, wherever the keyboard is.** Ctrl+Z takes back the last thing you did and Ctrl+Y (or Ctrl+Shift+Z) brings it back, whatever box the keyboard is in. Before, a text box took its own text back first, behind the editor's back. The editor then counted that as a new change, so the next Ctrl+Z put the text back again. If you had done other things since typing in a box and then clicked in it, Ctrl+Z undid the old typing instead of the last thing you did. And in a box with nothing of its own to take back (one you had only clicked in), Ctrl+Z did nothing at all: the box took the key and the editor never heard of it.
- **Every change is a step the moment you make it.** A tick, a pick from a list, a slider let go, a button pressed: each is its own step at once. Before, most became a step only when you next clicked somewhere else, so Ctrl+Z straight after the first change of a session did nothing, and two changes to the same control came back as one. Buttons that add, remove or move a row (**+ Add action**, **+ Add condition**, the ▲ ▼ arrows, **Paste**) were not a step at all until something else was edited, and were then undone together with it.
- **Undo takes you back to what it changed, and shows it.** The editor goes to the tab the change was made on and selects the character, place, dialogue and line, quest and task, screen and object, variable, rule, scene, NPC, sound or wallpaper it was made on, wherever you have been since. The box, list, tick or slider that changed glows for a moment, so you see what was taken back or brought back. Redo of something you added selects what was added, and undo of something you removed selects it again. Before, an undo changed things on a tab you were not looking at without a word, an undo on several tabs (Characters, Variables, Scenes, Music, Sound, Wallpapers, Integration, Map buttons) jumped to the first entry of the list, and taking back a rename lost the selection.
- **The previews are part of the same history, and as quick.** Moving, turning or resizing something in the place preview, shaping a light, moving or resizing an object in the UI preview, adding or removing an object: each is a step like any other, taken back where it is. The list beside the preview, what is selected and the picture stay as they are, and only the object that changed is put back. Before, anything but a move built the whole place, level or screen again.
- **The keyboard stays in the box you were typing in**, with the caret where the change was, also when the page had to be built again to put the step back.
- **Tried on every control.** A new check edits every box, list, tick, slider and button of every tab the way you would, presses Ctrl+Z and Ctrl+Y, and holds the pack and everything on the page to what they were. The fixes below marked *(found by that check)* come from it.

### Faster
- **A tab opens at once the first time.** The first visit to a tab waited while its page was built: a second for Dialogues, 0.8 s for Places, 0.7 s for the Tutorials page, 0.2 s for NPCs. The editor now builds every tab's page in the moments it sits idle after it opens and after a pack loads, one page at a time so it never holds up what you are doing. Measured on the first visit afterwards: Tutorials 11 ms, Dialogues about 30 ms, Places 0.1 to 0.25 s. The tutorials' pictures and the language lists are read in the background at start too.
- **Choosing a character, an NPC or a place no longer waits for its pictures.** A bust's pictures are read in the background, so the panel is there at once and the bust a moment later; the NPC preview's first look at a place is worked out away from the editor's own work; a place's art is kept once read (and read again when you save a new version of it), and read ahead as a pack opens. Measured on the tutorials' pack: choosing the player went from 0.3-0.8 s to about 0.05-0.1 s, the NPCs tab's first visit from 0.3-0.5 s to under 0.1 s.
- **Typing never waits for the spelling checker.** It checked each line on the editor's own thread as the line was drawn; it now checks on a thread of its own and the red underlines come in a moment later.
- **The character panel and big pictures open faster.** Whether a game character has changes is worked out once instead of on every redraw, a game picture enlarged from its thumbnail is kept instead of being enlarged again each time, and enlarging itself uses every core.
- **Undo, redo and everyday editing on a large pack.** Measured on a pack of 89 dialogues and 2,700 lines that changes a game screen of 1,650 objects: leaving a box used to cost 63 ms and every button twice that, because the whole pack was written out and compared each time. Now nothing is looked at unless something changed (13 ms then), and only what changed is kept. An undo used to read the whole pack and build every tab again, 1.1 to 1.4 s whatever it took back. Now only what the step changed is put back, where it is: a line's text or a line added in under 0.1 s, an object moved in a place in 0.03 s, an object added to one of the game's levels in under 0.1 s, an object moved on that large screen in 0.06 s. A step that goes back to another dialogue still takes as long as opening that dialogue does.
- **The place preview keeps up with your screen, with everything ticked.** With the game's look over a place (Bloom, Light streaks, Colour, Lights) and Shaders ticked, the preview drew about 30 pictures a second in a small view and far fewer in a large one, and the whole editor waited while it did: for every picture it photographed the place up to four times before the graphics card got to work. When the previews draw on your graphics card (Options, Draw previews on), the card now draws the place itself, as often as your screen shows a frame: measured at 164 a second on a 165 Hz screen in a view the size of a 1080p screen, at Ultra with every box ticked, while the rest of the editor is left alone. The picture is the same one, checked against the old way on all 146 of the game's places. On a PC whose previews draw on the processor, a still place no longer costs anything.
- **A conversation shows faster.** Picking a conversation of a hundred lines took two-thirds of a second, and so did an undo that went back to one: each line of the list was a full text box, with a spelling checker of its own. The lines are now drawn more simply, with the same words, colours and spelling marks: about 0.4 s for those hundred lines instead of 0.7 s (measured on a pack of 89 conversations).
- **No more stutter when the mouse moves again over a still place.** A place that had stopped moving was only looked at five times a second, so the first movement showed up to a fifth of a second late. The picture now follows the mouse at once.
- **Dragging an object or a light in a place is smooth.** Every step of a drag rebuilt the whole place two to four times over, reading its mask files again each time: about a tenth of a second for every move of the mouse with Shaders ticked. The place is now rebuilt once for each move, in a few thousandths of a second, and what did not change is kept.

### Fixed
- **Small things a careful read of every tutorial step turned up:** the tutorial panel keeps the step's own card in view (on a long tutorial's last steps the list of steps pushed its title out of sight); in Classic, with the tabs in two rows, the open tab's bar no longer reads as an underline on the tab above; the + Light, + Darkness and + Effect buttons above a place's object list wrap instead of scrolling the list sideways; the game conversations' list shows a long name shortened and its count of changes whole; a prefix box whose grey file names are cut off shows them whole in its tooltip; the bust preview calls the plain face Neutral, as the line editor does; a conversation's settings column is a little wider in a narrow window.
- **Some tutorial steps could not be done.** The tutorial dims the window and only what a step lights takes clicks, but several steps asked for things they left dark: naming a new rule, the Talk ticks of a conversation, a character's Bust source, + Vanilla for the game's quests, + Game scene, a place's Weather, and one step lit Actions on finish while asking for Actions on start. Each is lit now, and a check walks every step of every tutorial looking for any control a step names that it leaves dark.
- **A line's Outfit buttons show the outfits you just made.** An outfit added or renamed on the Characters tab was missing from the line's buttons until you chose another line.
- **A character of yours wearing one of the game's busts stays under THIS PACK'S CHARACTERS.** Setting her Bust source to a vanilla bust moved her into THE GAME'S CHARACTERS, among the game's 118.
- **[PV:...], {PC} and $names are no longer underlined as misspellings** - in the list of lines and in the line's own Text box. They are codes, not words, and the red line made them look wrong. A misspelled word beside them is still underlined.
- **The list lights what the page shows.** When something other than a click picked a record - an undo, a tutorial, a link from an issue - the page showed it while the list beside it still lit the one before, or nothing: Her secret open with "Test: her lines" lit. The row of the record shown is lit now, its folder opened, in the conversations' list, the Busts tree and every other tab's tree.
- **A record you just made opens at its top.** + Dialogue, + Rule, + Place and the others kept the scroll of the record shown before, so a new rule could open with its Display name above the view. Picking a record that was already there still keeps the page where it was.
- **The UI preview shows where the game's screen ends.** The screen is outlined and the space round it is darker, so whether something is off the edge can be seen in a tall preview too.
- **Removing or renaming an NPC or a place no longer empties what pointed at it.** Once you had looked at the Places or Map Buttons tab, removing or renaming an NPC left the places' placements of it with no NPC, and a place's map buttons and navigator buttons lost their target the same way - silently, and Undo could not put it back as it was. They keep the name they had, and Validate tells you which ones now point at nothing.
- **A dropdown holding a choice opened onto that choice alone.** A conversation's Required level, a navigator button's target, a default bust - any list that shows a name but stores a code - opened with every other option hidden until you typed, so a new conversation's level offered the Beach and none of your places. It opens onto the whole list now; typing still narrows it.
- **Reset character shows as soon as you change one of the game's characters.** After the first change (a new name, say), the banner saying what your pack changes and its Reset character button stayed hidden until you chose somebody else and came back.
- **A dialogue's "Set for condition debugging" did nothing in the game.** It promised a breakdown of the dialogue's start conditions on F12, but that key only ever worked in test builds of the plugin. Now, as a rule's debugging does, the game's log (BepInEx's console and LogOutput.log) gets the breakdown by itself whenever the conditions change between passing and failing, and once when the world loads - no key to press. The tick is now called **Set for condition debugging**.
- **Options > Version the pack automatically did nothing.** Unticked, Publish still moved the version. Now, unticked, the version is only ever what you type; ticked (as it starts), Publish moves it as before. Its tooltip also still said the version moved on every save - it moves when you publish.
- **Places: with Preview parallax ticked, switching places pulled the level's art away from everything else in it.** Every switch (and every change that redrew the place while it was drifted) moved the room's two pictures one more drift's worth, so a room's art and its objects, NPCs and lights no longer lined up, worse each time. They stay together now.
- **EX looks: buttons, cards and tutorials went soft while the mouse was over them.** The glow round what you point at blurred the words and pictures inside it. The glow is drawn behind them now, and they stay sharp.
- **The guide showed "More" under every point**, and on most of them it opened on a code like docs.modPack....more instead of words. It now shows only where there is more to read.
- **Exporting a pack while the game was running failed** with "being used by another process", once the game had loaded a save: the plugin kept every pack it loaded open, locked, until the game closed. Now an export replaces the file while the game runs. The game keeps what it loaded, and picks up the new export the next time a save loads, so testing a change is: export, go back to the game's main menu, load your save. Needs this version's plugin in the game.
- **EX: the tabs down the side were cut off in a window 900 pixels high or less.** Variables was cut in half and Integration was out of sight, and the only sign that the rail scrolled was a scroll bar three pixels wide. The tabs sit closer now, so all of them fit at 900 pixels, and when the window is too short for them the rail fades out at the end that goes on, with an arrow you can click to scroll it. A tab chosen from somewhere else (a tutorial, an issue) is scrolled into sight.
- **Places: Reverse back and Reverse front were swapped.** Each tick reversed the other layer's drift: Reverse back turned the front picture round, and Reverse front the back one. They are the right way round now. A place you set up by trial and error still drifts as it did; only the names on the ticks moved.
- **Places: the note under Display name (button) said it was the map button's label.** It is the name on the buttons that lead to the place (navigator buttons); a map button has a Label of its own.
- **A character's name box kept showing "New Character"** after the name was changed by Rename item or by a tutorial's Do it for me, while the list showed the new name. And a new character's first outfit kept the name it was made with ("NewCharacter"): it now follows the character's name until you name it yourself.
- **The dialogue preview said "No bust to show"** over a line spoken by a character with no Default outfit chosen. The game shows the first outfit then, and so does the preview.
- **A variable made a String kept "false" as its starting text** (a new variable starts as a Bool). It starts empty now.
- **An undo put a game screen you had changed back to the game's own.** In a pack that changes one of the game's screens (UI tab, **+ Vanilla**), any undo, of anything, anywhere in the pack, reset every object of that screen you had moved, resized, reworded, given another picture or hidden to the way the game has it. Nothing showed it until you looked at the screen, and the next save wrote the loss into the pack. A copy of such a screen (Duplicate) lost the changes the same way. Both keep them now. If a screen of yours lost changes this way, they have to be made again. *(found by that check)*
- **An undo put the objects of a game's level you had changed back to the game's own.** The same fault as for screens, on the Places tab (**+ Vanilla**): an undo or redo that built the level again put every object you had moved, hidden or given another drift back to the way the game has it. *(found by that check)*
- **Paste on an object of a place did nothing, or put the copy inside the wrong object.** Paste on an object's row only worked on a row added in the same session, and then the copy showed beside the row but was saved inside it. The copy now goes beside the row, on screen and in the pack. *(found by that check)*
- **The light panel beside the place preview went dead after an undo.** After Ctrl+Z on a light, the panel still showed the light as it was before the undo and its boxes and sliders changed nothing, until you selected the light again.
- **Ctrl+Z then Ctrl+Y in a box, and nothing came back.** A box that shows a number shorter than the pack holds it (two decimals of what a slider set), or a value the pack leaves unsaid (a volume of 1), wrote what it showed into the pack whenever it lost the keyboard or a shortcut was pressed in it, typed in or not. That counted as a new change, which throws away what there is to redo. It also marked the pack as changed when you had only clicked in a box and out again. A box now writes only what was typed. *(found by that check)*
- **+ Folder did nothing after an undo.** After taking back a new folder (Variables, Dialogues, Integration and the other lists with folders), the next **+ Folder** was added inside the folder that no longer existed, so nothing appeared. *(found by that check)*
- **A list kept showing a pick that had been undone.** Taking back a pick from an empty list (a character's default outfit, for one) left the pick on screen, as if the undo had not happened, though the pack was right. *(found by that check)*
- **The editor stopped with an error when an object on a screen was given a size below zero.** *(found by that check)*
- **Duplicate on the UI tab replaced what you had copied.** It went through the clipboard, so the object you copied before was gone, and Paste put the duplicated one in instead. *(found by that check)*
- **A condition went on hiding Negate after its kind was changed.** A row that had compared a yes/no variable kept Negate hidden whatever it was changed to, until the list was shown again. *(found by that check)*
- **The Source box of a game's level, and the Conversation box of a game's conversation, were sometimes empty.** Picking a level or a conversation you change while its tab was already showing left the box blank, though the pack was right. *(found by that check)*
- **Back to the game's, on a screen object, left its rows on screen.** Resetting an object of a game's screen took away what you had added to it (what a click does, when it shows, its components), but their rows stayed listed until the object was selected again.
- **Editing a place with Shaders ticked no longer fills up the graphics card's memory.** When the previews drew on the graphics card, every change to a place left another copy of its art on the card until the editor closed.
- **Objects your pack adds to a place were drawn too close to its middle in the preview.** See **Before you update**. The move handle was in the right place, so it sat beside the object rather than on it, and a drag moved the object less than the mouse. Both now sit where the game shows the object.
- **The move handle of the game's own objects and of NPCs sat beside them, further from the place's middle.** It now sits on what it moves, and a drag moves it with the mouse.
- **The game's spotlights were drawn as round lamps in the place previews.** 31 lamps in 23 of the game's places light a cone, not a full circle, and the preview lit all round them. They are now drawn as the game draws them.

## 1.9.2

The game's places, drawn as the game draws them. The place previews show the levels' own lamps and videos.
Along the way, many of the game's
levels turned out to be drawn too small, too high or with objects out of place, and each now matches the game's own
pictures. In the game, a picture a pack adds to one of the game's levels now always shows.

### New
- **Place previews show the places' own lamps.** Each of the game's levels is lit by its own lamps as the game lights it: a lamp brightens what is around it and adds its soft glow, before the bloom and the colour take over, so a bright lamp blooms as it does in the game. A pack's place is lit by the Beach's lamp, which every pack place keeps in the game. A **Lights** tick box, beside Bloom, Light streaks and Colour, turns them off. What the game draws without lighting (the own picture of 56 of its levels, Tasha's store among them) is not brightened, only glowed over, as in the game. Checked against the game's own pictures of five places: with their lamps, the PC's room and the Beach match the game as closely as a place with no lamps does. Lamps' shadows are not drawn yet; only five of the game's places have anything that casts one.
- **The game's videos show in place previews.** The Inside Car's sunset through its windows and the Japanese Subway Train's tunnel were black: the game plays a video behind them. A still from each video is now drawn where the game shows it.

### Fixed
- **The game's levels were drawn a little too high.** The game keeps its levels slightly below the middle of the screen, and some of them to one side, and the vanilla extension preview drew every level in the middle: 13 game pixels higher than the game shows it, and 13 to the side for the levels placed further left. Each is now drawn where the game shows it, checked against the game's own pictures of five places.
- **In the game, a picture a pack added to one of the game's levels could come out as something else.** ModForge made a pack's new picture from whatever came second in the level, taking it for the level's back picture. In most of the game's levels that is a light, a particle effect or a trigger, and on the Hiking Path's start, for one, the pack's picture became a particle effect and never showed; in a level with only one object, it was a copy of the whole level, with its triggers. A pack's new picture is now always a plain picture. Places of your own were not affected, and on levels whose second object is a picture nothing changes.
- **The bust preview's places were captured slightly off.** The game moves its places with the mouse, and the five places a bust can be previewed over had been captured with the mouse at the side: each sat 19 game pixels to the right of where it rests. On the Beach, the lamp a bust's shadow is cast from had been moved by one of ModForge's own checks, so the shadow came from the wrong place. All five were captured again, at rest and as the game has them.
- **Over the Beach, the bust preview put the lamp's glow on the bust.** The game draws a lamp's glow along with the last layer that lamp lights. The Beach's lamp lights only the place, so in the game its blue glow is part of the place behind the bust: the bust is not under it, and the soft light the bust's edge picks up from behind includes it. The preview drew the glow over the bust and left it out of what the edge picks up, so on the Beach a bust came out slightly blue with an edge a little too dim. It is now drawn as the game draws it. Checked in the game: the Beach's bust preview now matches within three levels at 99.9% of its pixels, against 98.1% before (its lit edge 99.3%, against 90.3%). The PC's room was already right, because its lamp also lights the room's photos and so its glow does come over the bust.
- **The game's levels drew some of their objects away from where the game draws them.** The vanilla extension preview put every one of a level's objects centred on its picture, but many of the game's are not: a picture cut out of a whole level keeps the level's centre as its own, so the Beach's sea hung across its sky. The preview now places each object as the game does (217 of the 512 pictures the levels use are placed this way).
- **Some of the game's levels were drawn too small in the vanilla extension preview.** A level's own picture was sized twice over: once for what it is, then fitted to the frame like a pack's picture. Most levels came out about 1% small, so their objects sat slightly off it, and seven came out at 70% of their size: the PC's room, the Kitchen, the Living Room, the Pool, the Park, the Basement and Charlotte's Halloween home. The Kitchen and the Park showed a black frame around the room. Each level's picture is now drawn at the size the game draws it, checked against the game's own frames of the PC's room and the Beach. The two levels whose picture sits slightly off centre in the game (the Winter Hot Spring and the Badlands Parking Lot) are placed as the game places them too.
- **A level's second object was drawn as a backdrop.** The vanilla extension preview drew whatever came second in a level as a picture filling the screen behind it. In 30 levels that is an object of its own (a guest, a potted plant, the Dining Room's table, which went missing behind the room), or a backdrop that the game moves, scales or flips. It is now drawn where it stands, like the level's other objects. The Bath, whose room is that object, is drawn instead of the "no sprites" note.

## 1.9.1

The previews now draw what the game draws. The bust and NPC previews show a
character in the PC's room or in four of the game's places, lit and finished
the way the game does it there, with the room's shadow, the glow at the edges
and the game's own fade-in. The place previews show the game's bloom, light
streaks and colour, and stay sharp when zoomed in. The previews can draw on
your graphics card, and preview quality now decides how much of the game's
look is drawn instead of capping the frame rate. In the game, pack busts cast
their own shadows and their movement is no longer cut off. The mask painter's
Soften brush works, and stops at the softness you set.

### Before you update

- **In the game, a pack bust's shadow takes its own shape.** Until now a pack
  bust threw the shadow of the game's bust it is built from. If your pack's
  look relied on that shadow, look at it again in the game after updating.
- **Preview quality means something else.** It no longer caps the frame rate:
  every level draws as often as your screen refreshes, and the level decides
  how much of the game's look is drawn (see below). The level you had stays
  chosen.
- **The Characters tab's Preview size field is gone.** Drag the preview's
  corner instead; your old size is where it starts.

### New

- **The bust preview fades a bust in the way the game does.** When a bust
  appears in the game, a dark silhouette of it fades in over a second, then
  the character fades in from that silhouette over three seconds, with its
  blink and expression coming on as it does. The preview now plays the same
  thing each time you pick an outfit. A **Fade in** tick box under the
  preview turns it off, and ticking it again plays it again. It starts on.

- **The bust preview shows the bust in the PC's room, as the game draws
  it there.** The room is behind it, the room's lamps light it, and its edges
  take on the glow the game gives them from what is behind them. It breathes
  over the room as in the game, and its lowest rows are off the bottom of the
  picture, as they are off the game's screen. The mask painter's preview does
  the same. **None**, in the **Backdrop** buttons at the bottom of the
  Characters tab or in **Options > Preview backdrop**, shows the bust alone,
  as before. At Low preview quality the edge glow is left out.

- **Four more of the game's places to see a bust in.** Besides the PC's
  room, the **Backdrop** buttons now offer the **Beach**, the **Forest**, the
  **Villa** (inside) and the **Temple** (inside), each as the game draws it
  behind a bust: its colours, its light and the glow it gives the bust's
  edges. They were picked from all of the game's places to be as different
  from each other as possible, and each was checked against the game itself.
  On the Beach, the bust also throws a shadow across the place, from its
  light behind the bust, as it does there in the game.

- **The NPC preview shows the NPC in the place, as the game draws it there.**
  With a backdrop chosen (the same **Backdrop** buttons, now under the NPC's
  preview too), the NPC stands in the middle of the place at the size the
  game draws it there. The place's lights fall on it: in the PC's room the
  lamp warms it, as it does in the game, where a bust is lit by the room's
  general light alone. Its edges take the place's glow, and the game's
  post-processing goes over it all. **None** shows the NPC alone, at the same
  size and in the same place, so switching backdrops never changes how big it
  looks. It was checked against an NPC in the game itself, pixel by pixel, on
  the processor and on the graphics card.
- **The NPC preview scales like the bust preview.** It fills the room it is
  given, no bigger than the size its corner is dragged to, and the divider
  beside it can be dragged to give it more or less of the tab (remembered).

- **The previews can draw on your graphics card.** A new setting,
  **Options > Draw previews on**, chooses what draws the live previews:
  **Automatic** (the default), **Graphics card** or **Processor**. Automatic
  times both when the editor starts and uses whichever is faster on your PC.
  On the graphics card, the bust preview over the room runs the game's own
  programs, the way the game draws it there: the bust's shader with its
  movement and edge glow, the room's lamp, the bloom, the light streaks and
  the colour. It was checked against the game itself, pixel by pixel, and
  matches it as closely as the processor's preview does.
  - On the PC it was measured on, a frame at Ultra takes about 5 ms on the
    graphics card, against about 45 ms on the processor, and leaves the
    processor almost free.
  - The first time, the editor takes a few seconds to prepare the graphics
    card's programs. After that they are kept and load at once.
  - The NPC preview, with a backdrop or none, the bust with no backdrop,
    and the place previews (their art's movement, their NPCs and the game's
    look over them) draw on the graphics card too.

- **See how fast the previews draw.** **Options > Show preview frame rate**
  puts the frame rate each live preview is drawing at in its corner. It
  starts off.

- **The mask painter's preview can change its backdrop too.** The same
  **Backdrop** buttons as under the Characters tab's preview, the same
  setting.

- **Place previews show the game's look.** The game puts a soft glow around
  the bright parts of every place (bloom), long upright light streaks from
  the brightest ones, and its own colour over all of it. The Places tab's
  preview and the scene extensions' preview now draw these too. Each has a
  tick box under the preview, **Bloom**, **Light streaks** and **Colour**,
  all on to start with. Untick one to see the place without it. What is drawn
  also depends on the preview quality (see below). Zoomed in, the look is
  worked out for every pixel of the view, so the place's art keeps its fine
  detail under it, and it carries on past the edge of the game's screen
  rather than stopping there.

- **The bust preview shows the bust's shadow on the room.** The room's lamp
  throws a bust's shadow across the wall behind it, and the preview now draws
  it where the game does, with its soft edges, moving as the bust breathes.
  The lamp's glow over the bust and the room in the shadow are drawn the way
  the game draws them too, and the edge glow picks up the darker room. It was
  checked against the game itself, pixel by pixel.

- **The bust preview over the room shows the game's colours exactly.** The
  preview used the textbook way of turning a picture's colours into light,
  and the game's graphics card does it very slightly differently, enough to
  put about half of a bust's pixels one shade off. It now uses the game's
  own.

### Fixed

- **The bust preview breathes with no backdrop too.** With **None**, the bust
  could stand still. It now breathes inside its picture, a whole pixel at a
  time, the way it does over the room and in the game.

- **Moving busts and NPCs showed scanlines in the previews.** On a
  1920x1080 screen or larger, the game draws a bust with twice as many rows
  as the preview did. The preview's Wave (green) movement therefore shifted
  its rows in steps twice the size of the game's, which read as scanlines,
  at every preview quality. The previews now work out the movement at the
  game's own density and show it at their size, so the steps are the game's.
  This was hidden in 1.9.0 at Ultra and High only.

- **The bust preview's breathing was shallower than the game's.** The Depth
  slider now starts at 5, the game's own depth (it was 4, set by eye).

- **The previews' Noise movement now matches the game's.** The bust and NPC
  previews drew the Noise (blue) channel with a different noise from the one
  the game uses: a smooth wobble where the game has a finer, layered
  shimmer, at a different speed (the game's also follows Bounce Speed), and
  without the sideways sway the game adds. In places the art sat up to about
  24 preview pixels from where the game draws it. The preview now does the
  game's own noise. It was checked pixel for pixel against the game's shader
  and against a bust drawn by the game itself. Bounce and Wave were already
  right.

- **In the game, a pack bust's or NPC's movement could pull in art from the
  opposite edge.** Where Bounce, Wave or Noise moved art that reaches the
  edge of its image, the game took the missing strip from the far side of
  the image, often an empty line, where its own busts repeat the edge. Pack
  art is now loaded the way the game's own bust art is, which is also how the
  editor's preview has always drawn it.

- **In the game, a pack bust cast the shadow of a different character.**
  The room's lamps throw a bust's shadow on the wall behind it. A pack bust
  threw the shadow of the game's bust it is built from (Anna's, for most),
  whatever its own art looked like, and a pack's art put over one of the
  game's busts kept that bust's old shadow. Each now casts the shadow of its
  own art, the same shape the editor's preview shows, and the game's own
  busts keep theirs.

- **In the game, a pack bust's or NPC's movement could be cut off.** The game
  drew pack art only inside a rough outline it makes around the art's
  visible pixels. Where Bounce, Wave or Noise moved the art out past that
  outline, the part outside vanished. The editor's previews draw the whole
  picture, so they never showed that cut. The game now draws the whole picture
  too, and the art moves the same in both. Nothing moves to a new place.

- **NPCs in the Places preview looked blurry when zoomed in.** An NPC with
  movement in a place was drawn as a small picture, no more than 384 pixels
  tall, and enlarged with the view, so zoomed in it lost the detail of its
  art. It is now drawn at the size it covers on your screen, up to its art's
  own, and redrawn at the new size a moment after you stop zooming.

- **A place's jiggle moved the wrong way in the preview.** The wave on a
  place's art was drawn upside down, so it travelled in the opposite
  direction from the game's. It now runs as in the game, checked against the
  game's shader.

- **The mask painter's Soften brush painted like the ordinary brush.** In
  1.9.0, a stroke made with the mouse added paint instead of smoothing. Only
  the button's highlight said Soften, and the stroke underneath was the plain
  brush at the same opacity. It now softens.

### Changed

- **The Characters tab's bust preview scales to fit, like the mask
  painter's.** The **Preview size** field is gone. The preview grows into the
  room the tab has for it, with its options directly under it as before, and
  no bigger than the size its corner is dragged to (your old Preview size is
  where it starts). A divider between
  the outfit's fields and the preview gives the preview more or less of the
  tab, and is remembered. In a small window nothing under the preview is
  pushed off the screen.

- **Preview quality now sets how much of the game's look the previews draw.
  At every level they run at your screen's refresh rate.** Before, each level
  capped the frame rate (Ultra 60 frames a second, High 30, Medium 20,
  Low 15). With a bust over the room, every level ended up about as slow as
  the others anyway. Now the previews draw as often as your screen refreshes,
  at any quality. The level decides how much work each picture takes:
  - **Ultra** draws all of the game's look: a bust's edge glow, the bloom,
    the light streaks and the game's colour, each worked out the way the game
    does it. Use it to compare with the game.
  - **High** leaves out the light streaks. On the processor it also works
    out the edge glow a little more roughly; on the graphics card it is
    exact.
  - **Medium** (the default) also leaves out the bloom.
  - **Low** draws the game's colour only, with no edge glow.
  - With no backdrop the levels save work too, for the bust and the NPC
    previews alike: **Ultra** draws them as finely as the game does on a
    large screen, **High** with half the work (the rows as fine as the
    game's), **Medium** with a quarter, and **Low** one point for every four
    pixels. Before, a preview with no backdrop asked the same of the computer
    at every level.
  - The work behind each picture is also much lighter. On the machine this
    was measured on, a 60 Hz screen, the bust preview over the room now draws
    about 53 pictures a second at Ultra and 55 at Low.
  - The place, NPC and mask painter previews follow the same levels.

- **The Soften brush softens edges to a softness you set, and never past it.**
  While Soften is the tool, an **Edge softness** slider takes Opacity's place.
  It sets how many pixels a hard edge the brush passes over fades across.
  Going over an edge again leaves it as it is, so you can work along an
  outline without blurring it away. To soften an edge further, raise the
  slider. To tighten an edge back up, lower the slider and go over the edge.
  Painting or erasing across a softened edge and softening it again gives the
  same softness, not more. Undo brings back the edge and what the brush had
  softened it from.

## 1.9.0

Every one of the game's variables now says what it does, and yours show their
descriptions the same way. You can change the game's own scenes and show or
hide any of them from your actions, give the game's characters new names that
follow them everywhere, and ModForge opens your last pack as it starts. The
mask painter now sits inside the main window beside a live preview, with a
soften brush, mirror painting and single-layer copying. Saving, Validate and
opening a pack are faster, and in the game packs load faster and use less
memory. The installer is about 1 MB now and downloads the rest.

### Before you update

- **Players need this version of the plugin for the game's scenes and
  characters' new names.** Changing one of the game's scenes, showing, hiding
  or asking about one from your actions and conditions, and giving one of the
  game's characters a new name all need the 1.9.0 plugin. An older one leaves
  the game's scene as it is (or, if you gave it a picture, adds it as a scene
  of the pack's own), does nothing for an action aimed at one, and shows the
  character's old name.

### New

- **Every one of the game's variables now says what it does.** Open the
  variable list on a condition, an action or a quest counter set to the game's
  variables, and under each name is a short note: what it means in the game,
  when the game changes it, and what must happen first. Type a few words of
  what you are after ("yacht", "first kiss", "money") and the list finds the
  variables whose note says so, not only those whose name does. Each note was
  researched in the game itself: every place that sets, reads or shows the
  variable was taken from the running game, a second pass tried to prove each
  note wrong, and what they disagreed on was settled from the game's files or
  measured in the game. A note worked out with a step the files do not show
  outright ends in "(inferred)". The 288 variables the game never uses say so.

- **Your own variables show their description the same way.** Whatever you
  wrote in a variable's Description on the Variables tab now appears under its
  name in every list of your variables: a Variable condition or action, a list
  to add to or pick from, "when it ends, set", a quest counter, a rule's
  "Repeat for each", a line's outfit. Typing a few of its words finds it, as
  with the game's. A variable with no description is just its name, and a
  description longer than a few lines is cut short in the list (the Variables
  tab keeps all of it). An edit shows up the next time a list opens.

- **ModForge opens your last pack as it starts.** "Load this pack on start",
  in the File menu and on the ModForge tab, is on once you have loaded a pack:
  the next time ModForge starts, it opens the pack you last loaded. Untick it
  and it stays off, for every pack, until you tick it again. It can only be
  changed with a pack open. If the pack has moved, ModForge says so and starts
  empty; and if ModForge ever closes while opening it at start, it does not
  try that pack again on the next start, so you are never locked out.

- **Hide the notes when you want a plain list.** Every open list of variables,
  the game's or yours, now starts with a "Show notes" tick box. Untick it and
  the list is names only, and typing matches names only; tick it again for
  the notes and their search. The editor remembers your choice for every list
  of variables.

- **The game's art says why it looks softer than yours.** Under the Scenes
  tab's preview of one of the game's scenes, the Characters tab's preview of
  one of the game's busts, and the preview of one of the game's levels, a
  small note says that the editor carries reduced copies of the game's art, at
  what resolution, and why: to keep its download small and not hand out the
  game's pictures. Your own art is always shown as it is.

- **Give one of the game's characters a new name.** The name box on the
  Characters tab now takes one for the game's characters too: type it over the
  game's name, and it is shown over every line they speak, in the game's own
  scenes and in every pack's, in their own name colour. Reset (or emptying the
  box) gives them the game's name back. A character of a pack's own who happens
  to share the name is somebody else, and keeps it. When two packs rename the
  same character, the one loaded later is shown, as with name colours. An
  editor or plugin older than this one ignores the new name and shows the
  game's. Checked in the game.

  The game's own words follow it too: its lines and choices, its quests'
  titles, paragraphs and tasks, and the texts on its screens, wherever they
  name that character (2,203 places across the game). Each place was found in
  the game's texts and tied to one character; a name that is also a word or
  another thing's (River, Clay, Ghost, Sakura...) was tied only where somebody
  read the line and said so, and a description standing in for a name ("Cool
  Guy") is never changed in a sentence. Only the full name is replaced:
  "Doctor Frost" becomes the new name, "Dr. Frost" stays, and a stutter
  follows it ("A-Anna" becomes "B-Bella"). A line a pack rewrote keeps the
  pack's words, and so does a line of the game's that a pack shows in its
  translation. A new name is the same in every language: the box is locked
  while you edit a translation. Checked in the game, and that switching the
  pack off on the main menu gives them the game's name back.

- **Name a character in a line so the name stays right.** The new button with
  a person on it, over a line's text, lists the characters (who speaks in this
  conversation first, then the pack's own, then the game's) and writes
  `{name:…}` for the one you pick. The player reads it as that character's name
  as it is now, including a new name any pack gives one of the game's
  characters - the key in any capitals, in the editor and the game alike. In
  the editor the line shows the name on a chip in their colour. A
  name you type out yourself stays exactly as typed, which is what keeps a
  pack's own Sakura from ever being renamed with the game's Sakura. Works in
  lines, choices, quests and the pack's other texts, and translations must keep
  it as it is (Validate says so when one does not). Checked in the game.

- **Validate lists the game's characters named in your own words.** Each name
  of one of the game's characters typed out in the pack's lines, choices, quests
  or screens is listed as a note, with the words around it, since it will not
  follow a new name. On a line, right-click the note and pick "Write {name:…}
  there instead" to switch that one mention to the token; Undo takes it back,
  and nothing changes unless you ask. A name two of your characters share (your
  own Sakura and the game's) is not listed, because it is nobody's in
  particular; nor is the game's name inside a longer name of your own ("Anna
  Smith"), nor a description the game uses for a character ("Cop"). A name
  said with a stutter is listed with no change offered, as the token cannot
  stutter. Validate also warns about a token that names nobody (the player
  would read it as typed) and about a new name another character already has.
  Two installed packs giving one character different names show on the game's
  main menu with the other clashes between packs.

- **Change the game's own scenes.** The Scenes tab has a second list, "The
  game's scenes". Add one, pick a scene from the list of the game's (each with a
  small picture; type part of its name, its group or a conversation that shows
  it), then give it your own picture, another frame or another sound, or make
  it count as a Starmaker photo. Whatever you leave empty stays the game's. The
  scene keeps its name and everything on it, and the game goes on showing it
  wherever it always did, now with your changes; your own actions can show it
  by its runtime name as well. A new picture for one of the game's photos
  shows in the Starmaker site's gallery too. Scenes the game never shows are
  not offered.
  Validate warns about one with no scene picked, counts the same scene
  changed twice as an error (Publish asks before going on), and notes one
  that changes nothing. Two installed packs
  changing the same part of one scene show on the game's main menu with the
  other clashes between packs; in the game the pack loaded later wins that
  part (its still picture replaces an earlier pack's GIF or video), and a
  scene both make a Starmaker photo is one photo, paid once and counted by
  both. Packs' own scenes are made in the game from one of the game's
  (Samanthabeach); a change to that scene changes it alone, never the own
  scenes of packs loaded after it. A plugin older than this one leaves the game's
  scenes as they are (a change with a picture becomes a scene of the pack's
  own there, which only the pack's own actions show), and its main menu says
  the pack needs this version. Checked in the game, with this version's plugin
  and with 1.8.3's.

- **Show, hide and ask about any of the game's scenes from your own actions.**
  The Scene category of Set active, Set sprite and the "is it showing"
  condition now lists every scene of the game after your own, under "The
  game's own: Sexy", "...: Photos" and so on, each with the conversation that
  shows it (type part of its name to find it). Under the target, the small
  preview draws the game's picture in the game's frame, or your change to that
  scene when you have one. Shown this way, a scene plays its own sound and,
  for one of the game's photos, takes the photo, as when the game shows it;
  "Hide every scene this pack showed" hides it again. Validate knows them, and
  now also checks the scene a condition asks about. Checked in the game.

- **A warning before an older ModForge loses a newer one's work.** Opening a
  pack that a newer ModForge saved last now says so: this version cannot show
  what the newer one added, and saving the pack here drops it for good. Saving
  such a pack asks first. Worth knowing if you share a pack with somebody: a
  ModForge older than this one gives no warning at all, and saving there
  quietly loses what is new in this version (a change to one of the game's
  scenes turns into a scene of the pack's own; a new name for one of the
  game's characters is gone).

- **The installer is now about 1 MB.** It used to carry the whole editor and
  plugin inside it (84 MB, too big to post on Discord as ModForge grows).
  Now it downloads them from this version's release on GitHub as it
  installs: only the plugin when you only want to play packs, the editor too
  when you want to make them (chosen already when the install it updates has
  the editor). Each download is checked against the size and checksum the
  release lists before anything is installed, and stopping it, losing the
  connection or GitHub not answering installs nothing; the setup then says
  what went wrong in your language and where to get the zips. What it
  downloaded is deleted once it is installed. It still finds an earlier
  install and updates it in place, and it still speaks your Windows language.
  Without the internet, put the two zips from the release beside it and it
  installs from them (only zips of its own version: an older plugin zip left
  in your Downloads folder is not installed by mistake).

- **The mask painter opens inside the main window now**, not in a window of
  its own. Every Edit Mask button (characters, the game's busts, NPCs and
  places) puts it in place of the tabs: the painter on the left and the live
  preview, shader running, on the right, so you no longer need two windows
  side by side to see what a stroke does. Drag the bar between them to share
  the room; ModForge remembers it. Back (or Esc) returns to the tabs, and
  strokes you have not saved are asked about first, also when you open
  another pack or close ModForge. New in it:
  - **Mirror** (M) paints both halves at once, about the middle.
  - **A Soften brush** (S) beside Brush and Eraser: it smooths what it passes
    over instead of painting, as far as Opacity says, for taking the hard
    edge off one place without repainting it.
  - **Invert** (I) swaps strong and faint on a layer, and **Soften all** blurs
    the whole layer a little. Both undo.
  - **Alt+click** takes the strength under the pointer as the opacity.
  - **Copy layers from** another outfit can now copy a single layer (say,
    only Wave) over the same layer here, leaving the others as they are.
  - Zoom buttons, a fit-to-view button, and the position and strength under
    the pointer at the bottom.
- **Quality buttons beside every live preview.** The Characters, NPCs and
  Places previews and the mask painter have Ultra / High / Medium / Low
  above them, the same setting as Options > Preview quality, so you can
  change it where you see the difference.

### Faster

Measured on a large pack (89 dialogues, 2,700 lines, 220 variables, eight
translations); everything it saves, exports and reports came out byte for
byte the same as before.

- **Saving and Validate take about 40% less time** (2.5 s to 1.4 s, and 2.6 s
  to 1.5 s). The checks wrote the whole pack out and read it back three times,
  and read every translation three times; once is enough.
- **Opening a pack is faster** (5.3 s to 4.4 s), and so is every undo and
  change of language: the lists of names were rebuilt once per record instead
  of once.
- **Picking a dialogue is quicker**, the slowest ones nearly twice as fast:
  each line in the list was drawn three or four times before it showed, and
  is drawn once.
- **Clicking a variable on the Variables tab** no longer writes the whole pack
  out to look for it in texts that do not mention it.
- **Typing in a line of a conversation that extends one of the game's** no
  longer recounts the whole conversation a dozen times per key, and moving
  lines around no longer makes it slower each time.
- **Long sessions use less memory**: each undo step is kept at half the size.
- **In the game, packs load faster and take less memory.** Each picture is
  read once per load however many outfits, mouths and places use it - a large
  pack read the same bust pictures nearly four times over - and masks are no
  longer kept twice. The game also does less every frame for a pack's
  conditions (game variables, lists, levels), which means fewer small
  stutters while it plays.

### Fixed

- **A tutorial's Next and Finish button was in English in every language.**
  It was translated, then written over in English as each step showed.

- **Reset on a game quest stayed lit in every language but English**, even
  with nothing changed: it read the quest's summary looking for the English
  word "unchanged".

- **In the game, reloading from the main menu kept every earlier load's copy
  of a pack in memory**, for packs that add to the game's own conversations:
  one more copy each time. Now the old ones are let go of.

- **The Places preview's shaders kept running after you left the Places
  tab**, using the processor for something nobody could see. They stop while
  the preview is hidden now, and they follow Preview quality's speed instead
  of always running at their own.

- **Set sprite aimed at a scene now changes its picture.** It changed another,
  invisible part of the scene instead, so the scene went on showing the
  picture it had - for your own scenes as much as the game's. It now paints
  the scene's picture, at the size every scene's picture is drawn, and stops a
  GIF or video that was playing there first. Checked in the game.

- **The main menu's clash warning now covers packs that change the game's
  screens.** A pack that changed one of the game's screens was never marked
  as clashing with anything, not even for the things it shares with another
  pack elsewhere (a character's colour, a level's object, a line of the game's
  conversations): reading its screen changes failed, and took every other
  change of the pack with it. Each part is now read on its own, and two packs
  changing the same object of the same screen are marked too.

- **A pack's translation of the game's own lines now shows in the game.** A
  pack that extends one of the game's conversations can translate the game's
  lines in it too, and a player who reads the pack in that language should
  see them translated. None ever was: the plugin compared each translation with
  a line's words before the line had been shown, when the game has not filled
  them in yet, so nothing matched and every line stayed in English, with no
  sign of it. It now reads the words the line is made from, and the player is
  told once, as the save loads, that the pack shows the game's lines in their
  language (the note never came either). Checked in the game.

- **Fewer wrong "this text is not in the pack's language" warnings.** Validate
  no longer counts words in quotation marks (a Spanish line quoting an English
  choice) or names written in the middle of a sentence ("Just Anna I") as
  evidence of a language. Text really typed in the wrong language is still
  caught as before.

- **The mask painter's brush now paints like Paint.NET's.** A stroke was a
  chain of round dabs, each moved to the nearest whole pixel: a soft stroke's
  edge came out beaded, and a thin one wobbled as you drew. It is now painted as
  one smooth band that follows the pointer between pixels, so its edge is as
  soft (or as crisp) along its whole length as the brush itself. Hardness still
  means the share of the brush painted fully, with a smooth fade outside it, but
  a 100% brush now has a clean antialiased edge instead of stair-steps, and a
  small brush at a high hardness keeps a soft rim instead of looking hard.

- **A face your line gave Chloe or Nadia no longer snaps back.** Some of the
  game's portraits show another character's face number: Chloe's follow
  Charlotte's, and Nadia's follow Emma's. ModForge set the speaker's own number
  (and Nadia has none), so the face lasted only until the next variable changed,
  which is almost at once. It now sets the number each portrait actually
  follows, as the game's own lines do, so Charlotte's or Emma's face changes
  with it when they are on screen too. Checked in the game.

- **Two of the game's variables could not be picked, and a name with the
  wrong capitals passed Validate.** The game tells capital letters apart:
  "Mei-corrupted" (her Japan star) and "mei-corrupted" (her first hotel visit)
  are two variables with two meanings, and so are "Black-Lingerie" and
  "black-lingerie". The variable list offered only one of each pair. And a
  condition on, say, "Anna-Saved" instead of "anna-saved" passed Validate but
  reached nothing in the game, so it never came true. The list now offers
  both, and Validate names the game's own spelling.

- **The list of the game's variables is now the game's own.** It offered 36
  "game" variables that were never the game's: affection values another mod
  made while it ran, which do not exist in a player's game, so a condition on
  one could never come true. And it was missing 38 that the game does have, such
  as the spirit fights, the pool invitations, the blackjack and lottery values,
  and the newer cooldowns. It is now read from the game itself.

## 1.8.3

The UI tab is rebuilt around what you came to do: a start page, a much bigger
preview, a control for every setting of an object, grouped by what it answers,
and an eye to hide an object while you work on what is behind it. That preview
now draws what the game draws, checked against the game's own screenshots.
Also new: a Travel action, copies of the game's objects in your places, objects
that keep moving, a pressed size on buttons, Stays upright, and grids only as
big as what they hold. Settings on the game's objects no longer vanish when a
pack is reopened, and changes deep inside the game's own screens now reach the
game.

### Before you update

- **A screen of your own is laid out over the whole screen now, as the
  editor shows it.** In the game, every screen a pack made itself was laid out
  in a small box in the middle of the screen, so an object anchored to a
  corner came out near the middle and one stretched to fill the screen was
  100 pixels wide. Objects anchored to the middle were right, and still are.
  If you moved a corner-anchored object around in the editor until it looked
  right in the game, it now sits where the editor shows it - check those
  screens once after updating.

### New

- **The UI tab says what to do.** With nothing selected it opens on a start
  page: change one of the game's UIs, add something that stays on screen, or
  make a window that opens, each one click away. After that it reads from left
  to right: pick a UI and an object, see it in a much bigger preview, change it
  on the right. A row above the tree goes back to the UI's own settings.
- **Every setting of a UI object now has a control, grouped by what it
  answers:** where it sits, how it looks, when it shows, what clicking does, how
  it moves, what's inside. A closed section says what is in it ("2 actions",
  "starts hidden"). New here, and until now only reachable by editing the pack
  by hand: a "Stick to" grid for which part of its parent an object stays with,
  filling the width or height with margins, exact anchors and pivot, turn, scale,
  how a picture is drawn (sliced, tiled, filled), keeping its shape, text
  alignment, wrapping, shrink to fit, line and letter gaps, opacity, shadow,
  outline, clipping and extra components.
- **An eye on every object in the UI tab's tree hides it in the preview only.**
  So an object in the way can stay where it is while you work on what is behind
  it: a hidden object is not drawn and clicks go through it, along with everything
  inside it, and whatever arranges it keeps its place so nothing else moves. It
  never touches the pack or the game. The editor remembers what you hid for that
  pack, and the preview says how many are hidden, with a Show all beside it.
- **The UI preview now shows what the game draws.** It was compared with the
  game's own screenshots, setting by setting, and put right wherever they
  differed: see-through colours and shadows were far too dark (the game mixes
  them in linear light), turned objects were drawn level, tiled pictures
  stretched, pictures that keep their shape squashed, and shrink to fit ignored.
  Unwrapped text stopped at the edge of its box where the game runs on. The
  game's outlined title fonts were thin and flat, its uppercase, small capitals,
  bold, italic and underlined labels were drawn plain, its buttons' resting
  colours were missing (so click areas the game leaves invisible were drawn
  solid), and trimmed pictures were drawn too large. Soft UI art is sharper too.
  Three things the preview cannot know are left alone: anything hidden by
  conditions is still drawn, so you can work on it; a slider stands at the
  player's own setting; and a picture a script fills as the game runs is drawn
  full.
- **A new action, Travel: take the player to a place, as its map or
  navigator button does.** One of your places or one of the game's, with the
  game's own journey and fade, the place's arrival conversation, and music if
  you name one. While the game is busy - a conversation on screen, another
  journey, a locked moment, and the few seconds after a conversation in which
  the game itself turns journeys down - it waits, so on a line the player goes
  once the dialogue ends; a button would simply refuse. If the game turns the
  journey down anyway (a screen of its own is open), it tries again a few
  times, as a player clicks again. If the player is already there, nothing
  happens. There is no option to skip the fade: it is part of the
  game's journey, and a journey without it would be a copy of the game's
  rather than the game's own.
- **An object in a place can be a copy of one of the game's.** "Copy of"
  takes the original's path in the game's scene - a room's button canvas for
  a room of your own, the TV, an effect - and the copy is made each time the
  game loads, in your place or, with "Beside the original", next to the
  original (for an effect your dialogues switch on by its path). It stays as
  the original was unless you override its place, its on/off or its parallax;
  children marked Bind reach its parts by name - to move one of its buttons -
  and new children are added to it. Until now a pack could make its own
  objects or change the game's, never copy one.
- **Particle colour** on a copied or bound object sets the colour its
  particles start in, so one of the game's effects can be recoloured; and
  **Override parallax** sets how far a copied or bound object drifts with the
  mouse.
- **An object of a screen can keep moving while it is shown.** "Keeps moving"
  (beside "When it opens" and "When it closes", which play once) repeats:
  a pulse of its fade or its size - a glow that breathes - or a sweep from
  one place to another and back to the start - a highlight under a parent
  that clips it becomes a shine running along a line. Each pass takes the
  seconds you give, with a pause between passes and a delay before the first,
  so copies of one object need not move in step. The editor's preview shows it
  still.
- **"Pressed" on a button: it shrinks while held down.** A button's own press
  is the game's, played on the click - once it is let go - so a button whose
  click closes its screen or starts a conversation never showed it. With
  "Pressed" set (0.95, say) it shrinks while the pointer is down, together
  with everything on it, and comes back on release; it then stands in for the
  game's press rather than adding a second one. It works alongside the game's
  hover bounce.
- **"Stays upright" on an object of a screen** keeps it level while what it
  hangs on turns: it moves and grows with its parent, never turns with it.
  The game swings some of its buttons round - a district's buttons on the
  world map turn as it opens - and anything a pack hung on them turned upside
  down for most of a second. It works on objects you add and on the game's
  own you bind.
- **A grid can be only as big as what it holds.** "Only as big as what it
  holds", on a grid with a set number of columns, makes it ask for only the
  columns its objects fill: a strip of faces that wraps at eight is as wide
  as the two showing, not always eight wide, so a Content Size Fitter on it
  grows and shrinks with its contents. The same goes for height with a set
  number of rows. Unity's own grid always asks for all its columns.

### Fixed

- **Settings on one of the game's objects could be lost.** Reopening a pack
  dropped a click, its conditions, a hover colour, an arrangement of children, a
  keeps-moving, a pressed size or stays-upright from any object of the game's,
  and the next save wrote the loss to the file. Saving alone dropped how it opens
  and closes and its click sound. Everything now comes back, and a check walks
  every saved setting so a new one cannot be forgotten. Anything already lost
  this way has to be set again.
- **Taking a picture or words off one of the game's objects only worked in the
  preview.** The game kept showing them. It now removes them too. Emptying an
  object's opacity goes back to the game's own value.
- A window started from the template now has a close button that closes it.
- The UI tab's "Which screen" came up blank for the world map, and a stray
  unlabelled dropdown sat on every UI of the pack's own.
- The world map said "2 changes refer to objects the game no longer has" about
  things hung on the pack's own map buttons. Those buttons are there when the game
  runs, so the warning was wrong: they now sit under their district in the tree.
- The dialogue preview drew its lines narrower than the game does: the game's
  dialogue font sets a letter spacing of its own, which the editor did not know
  about. Lines now run as long as they do in the game, so one that was only just
  fitting may show as too long.
- With a language or a pack that needs Windows' fonts, a letter the game's fonts
  lack - a curly apostrophe, in most of them - came from that font rather than the
  game's own last resort, leaving gaps in the game's English sentences.
- **Changes further down one of the game's screens were dropped in the
  game.** On a screen of the game's that your pack extends, anything two or
  more levels down - an object added inside a button inside a group, a label
  reworded inside a button - was looked for in the wrong place and skipped,
  with only a line in the game's log. Only the first level ever worked. The
  editor saved them correctly all along, so they now appear without
  re-saving.
- **A picture's edge no longer shows a line from its opposite edge.** Your
  pictures on screens were loaded to repeat, so wherever one landed between
  whole pixels - always, on the zoomed world map - each edge took in a little
  of the opposite one: a line along a picture's top also showed faintly along
  its bottom, and a cut corner had a strip of colour inside it. Pictures on
  screens and wallpapers now stop at their own edges. A picture you draw
  Tiled still repeats, as it must.
- **A place's arrival script no longer runs where you are when the game turns
  the journey down.** The game refuses a journey for a few seconds after a
  conversation, and while some of its screens are open; a navigator or World
  Map button to one of your places pressed then went nowhere, but a second
  later that place's arrival script ran in the room you were still in. It now
  runs only once you arrive.

## 1.8.2

Two new actions - PlayDialogue, to play another of your dialogues or one of
the game's own conversations, and TransformGameObject, to set where something
is and how big - and a box that has everyone who spoke leave the stage when a
dialogue ends. Validate now names a comparison nothing in your pack can make
true, and an outfit variable that can name no outfit. In the game, a pack
dialogue no longer starts on top of the game's own conversation when you
arrive somewhere; players are told so once, on the main menu. The copy buttons
under a list stay put as you reach for them, and Export leaves your backups
out. Opening a pack takes off settings that did nothing - see below. The
plugin changes too, so update both: a pack that uses the new actions or the
new box needs this version's plugin.

### Before you update

- **Opening a pack takes off settings that did nothing.** When an action or
  condition's type is changed, the settings of the type it was stayed in the
  pack - a Character focus still holding a Set active's target, an Increment
  variable still holding a Set variable's value, hundreds of focus actions
  still naming the one actor they used to focus. None of them did anything in
  the game, but they read like real settings to anyone looking at the pack,
  and a later version giving one of those names a meaning would have acted on
  it. They are taken off when the pack is opened; the list of what was brought
  up to date says how many, nothing is written until you save, and the first
  save keeps a copy of the original. Only a setting another type uses and this
  one does not is taken: anything ModForge does not recognise stays.

### New

- **Validate names a comparison nothing in your pack can make true.** When a
  condition compares a variable to a value that no action in the pack ever
  gives it - the variable's default and every Set variable, Random from list
  and Add to list counted, wherever they are - the issues list says so, with
  the values it can have. It is how a value renamed in one place and not the
  other shows up: placements still asking for `Swim` after every action was
  changed to set `AnisSwim`, or a flag that a screen's buttons were meant to
  set and never do. A variable the pack counts, rolls or copies from another
  is left alone, and if another mod sets the variable you can ignore it.
- **Validate names lines whose outfit follows a variable that can name no
  outfit.** A line can take its outfit from a variable (`$Name`); when that
  variable can hold a value that is none of the speaker's outfits - a default
  left as `Default`, say - the bust does not appear on those lines, and until
  now nothing said why. It is said once per conversation.
- **A new action, TransformGameObject: set something's position and size
  where it is.** The game frames its own pictures this way - a slide zoomed to
  2x onto one part, then back to 1x for the next - and until now a pack could
  only move things in the world and hold them there, never change their size.
  It sets an object's position under its parent (X, Y) and its scale, eased
  over the seconds you give or at once; leave a field empty to keep that one as
  it is. Nothing holds the object afterwards, and one a MoveGameObject was
  holding is let go.
- **A new action, PlayDialogue: play another dialogue, or one of the game's
  conversations.** Pick one of your pack's dialogues, or (Source: vanilla) one
  of the game's own conversations from the list. It plays as soon as nothing
  else is on screen and the game is not busy - put it on a line and it plays
  right after the dialogue that line is in. Its own start conditions are not
  asked. "When it ends, set" sets one of your variables once it has finished,
  so a start condition or a rule can follow it. A game conversation plays on
  its own, without the room's script around it, so switch its character's
  bust on yourself if the room would; its lines' own instructions still run,
  the game's story steps included. Renaming a dialogue updates the actions
  that play it, and Validate names one that plays something that does not
  exist.
- **"Leave the busts at the end", a new box on a dialogue.** When the
  dialogue ends, everyone who spoke in it leaves the stage, as a LeaveBust on
  each ending would have done - so an ending that forgot its LeaveBust no
  longer leaves a character standing over the level. On for every dialogue you
  make from now on; your existing dialogues are left as they were. Untick it
  where staying is the point, like a room where the bust is how the character
  stands there.
- **Changing an action's or a condition's type no longer leaves the old
  type's settings behind in the pack.** They are put aside while the editor is
  open, so switching back by mistake still brings them back.
- **A dialogue's level can be negated again.** The required level row at the
  top of a dialogue's start conditions has its Negate box back, so a
  dialogue can start anywhere *but* that level. The game always read it - packs
  that set it by hand worked - but the editor hid the box. With the level
  negated, "Prioritize this dialogue over vanilla" is unavailable: there is no
  room of that level to take priority over, and switched on it would act on
  the one room the dialogue never plays in.

### Fixed

- **The Copy all / Paste / Overwrite buttons under a list of conditions or
  actions vanished as you reached for them.** They show while the mouse is
  over the list, but the small gaps between the buttons did not count as over
  it: moving from "+ Add condition" towards them crossed a gap, they hid, and
  they could not be brought back without leaving the list and coming in again.
  Anywhere over the list - its rows, its buttons and the space between and
  beside them - now keeps them showing.
- **A pack dialogue could start on top of one of the game's own on
  arrival.** At a place where the game starts a conversation as you arrive
  (the gas station on a car-wash day, for one), a pack dialogue set to start
  there could begin first: its character came on screen, the game's
  conversation then played over it with the pack's character still standing,
  and the pack dialogue was lost until the next visit. ModForge used to wait a
  fixed moment after arriving and for the game's lock; the game lets its lock
  go while it is still leading in to its conversation, and the moment ran out
  before the conversation came. It now waits for the game itself to finish
  deciding: while the place's own arrival script runs, nothing of a pack's
  starts by itself. A dialogue with no box checked on the Dialogues tab then
  plays only if the game started nothing, as it always should have; one
  queued behind the game's follows it, never comes before it. Talk-button
  dialogues are unchanged.
- **Hiding a sound's words cut the wrong ones when one pattern is inside
  another.** With two sounds such as `*ding*` and `*ding**ding*`, a line with
  both played the right sounds, but hiding the lone `*ding*` took half of the
  `*ding**ding*` before it instead, and hiding both left stray characters. The
  editor's Game look showed the same. Each sound now hides exactly its own
  words, in translations too.
- **Export no longer ships your backups.** Copies of the manifest kept in the
  pack folder - `modpack.json.bak`, `modpack - Copy.json` and the like - went
  into the exported pack and to every player: in one pack they were over a
  third of the download. Export now leaves out files named as backups
  (ending in `.bak`, `.bak2`, `.bak-something`) and any `.json` beside the
  manifest that is a copy of it, and the "Export complete" message lists what
  it left out. The game reads only `modpack.json`, so nothing changes there.
- **A pack's places no longer carry a copy of one of the game's beach
  conversations.** Each pack's places shared a hidden copy of the beach's
  arrival script, emptied of its conversations - all but the first, by
  mistake. Nothing ever played it, but mods that read the game's scene (the
  Gallery) found it and counted it as one of the game's conversations.

## 1.8.1

A fix for anyone with variables: clicking from one variable to another could
change their default values without a word, and a pack saved that way keeps
the wrong ones - the first entry under Fixed says how to find them.
Also: a pack can be taken off File > Recent files, and the mask editor and
colour picker read properly in dark themes. Nothing changes in the game: the
plugin is the same apart from its version number.

### New

- **A pack can be taken off File > Recent files.** Each pack on the list has
  a small cross at its right; it takes that pack off the list, and nothing
  else - the pack's folder is not touched. The menu stays open, so several
  can go one after another. Until now the list only ever grew until older
  packs fell off the end, and a pack moved or deleted stayed on it.

### Fixed

- **A variable's default could change on its own.** On the Variables tab,
  clicking from one variable to another could switch a Bool's default from
  "Starts false" to "Starts true", and could write "true" over the default of
  an Int, Float, String or List variable - an Int's 0 became "true". Nothing
  showed it happening; the pack was saved with the wrong values. Moving
  between variables now leaves every one as it was. The true/false buttons on
  Variable conditions and Set variable actions are built the same way and
  have the same fix.

  **Check your variables after updating.** Validate names an Int or Float
  whose default is not a number (it starts at 0 in the game), and now a List
  whose default is not a list (it starts empty) - set those again. A Bool
  that should start false and a String default reading "true" cannot be told
  apart from one you meant, so look over those by eye.
- **The mask editor's Brush and Eraser were hard to read.** They, and the
  switches beside its layers, were drawn in Windows' own light grey and light
  blue whatever the theme, with the theme's text on top - light on light in
  every dark theme. The tools now look like the editor's other buttons, the
  one in use in the theme's accent; a layer's switch is its colour's dot in a
  ring, bright while the layer shows and faint while it is hidden.
- **The colour picker's "was" and "now"** were faint black over the colours
  being compared, and vanished on a dark one. Each is now black or white,
  whichever reads on its colour.
- **Sandstone's accent is a touch lighter**, so the words on its Save buttons
  and on a tool in use read properly - they fell just short.
- **A recent pack whose folder name has an underscore** showed without it -
  "Beach_Day" read "BeachDay" - because the menu took the underscore as a
  keyboard shortcut. The list shows the folder exactly as it is.

## 1.8.0

The EX update. A second way to draw the whole editor - EX, with the tabs in a
rail down the left, panels as tiles and the pack's name across the top - now
the editor's default, beside the reworked Classic themes and nine new ones,
and themes of your own. Each sound in a line now has its own
volume and start, can wait for the typewriter or keep its words out of the
line, and can be played from the line as the game will play it. A line's
outfit and face are buttons with pictures, empty dropdowns open with the likely
picks on top, every list has the same buttons with their keys, wrong values
show in red, and the save prompt says what changed in plain words. There is
an installer as well - for players, only the plugin, chosen already - and
ModForge's windows in the game now look like the game's own. Every new text is
in all eight languages. The plugin changes too, so update both.

### Before you update

**The editor opens in EX.** EX is the default look now, and the first time
you start this version, the theme you had moves to the EX theme most like it
- light for light, dark for dark, the nearest accent colour: Light becomes
Cobalt, Midnight Pulse, Carbon Aurum, and so on. It happens once, and a
message says so when it does. The old look is under Themes > Classic, every
theme as it was, and if you pick one there it stays picked.

**A sound's pattern written bigger now plays louder.** A line with
"<size=140%>*thud*" played its sound at the sound's own volume, whatever the
size: the game never read it. When a pack opens in this version, every
pattern written in a size other than the line's own is given that size as its
volume - 140% for the *thud* above, 80% for one written smaller - so these
sounds will be louder or quieter than your players heard them until now. The
size itself stays, so the line looks as it did. Lines with several sounds keep
their timing: a sound written twice still plays the second time when it did,
and two different sounds that started together still do. You are told how
many lines were changed when the pack opens, and the original is kept beside
the manifest on the first save. A pack saved with this version needs this
version's plugin to play its sounds this way, so update both; a pack nobody
has re-saved plays them the old way.

### New

- **An installer**, as well as the two zips. It asks first what to install:
  **Only the plugin**, to play mods other people made - chosen already, since
  that is what most players want - which puts nothing on the computer but the
  plugin in the game and the way to uninstall it (no editor, no shortcuts);
  or **Everything**, to make mods - the editor and the plugin. Running it
  again and choosing Everything adds the editor. Then it asks for the
  Starmaker Story folder - required, and checked to really be the game, with
  the folder found already when Steam or the editor knows it - and, for
  Everything, where the editor goes, with a folder of its own chosen already
  (`%LocalAppData%\Programs\SMSModForge`, where Windows puts a program
  installed for you alone). Choosing a folder inside the game is allowed but
  not recommended, and every word on the page turns yellow to say so. It
  puts the plugin in the game - the whole BepInEx bundle into a game without
  BepInEx, only ModForge's own files into one that has it - tells the editor
  where the game is, and adds Start menu and desktop shortcuts if you want
  them. It speaks the language Windows is set to. ModForge then appears in
  Settings > Apps, where it can be uninstalled; that takes the editor away
  and, if you tick it, its plugin from the game, and leaves your packs. With
  only the plugin installed, uninstalling takes the plugin out.
- **A screen can open by itself.** A screen of the pack's own has **Opens
  when** in its settings on the UI tab: conditions that open it the moment
  they are all met - as the game loads, if they already are - and again each
  time they are met anew. The player can still close it; it stays closed
  until they stop being met and are met again. With none, nothing changes: it
  opens only when an action opens it. Until now this needed a rule written
  for it.
- **A jump that lands under another line is warned about.** In this game a
  jump does not simply carry on from the line it lands on: once that line and
  the lines under it are done, the game says again the line it is under, then
  that line's lines. A jump that lands under the very line that jumps
  therefore goes round for ever - "I should go." jumping to "Bye!" under "Oh,
  one more thing..." plays I should go, Bye, Oh one more thing, Bye, I should
  go, and so on. Validate now warns about every jump that lands under another
  line, and says when the conversation would never end. A jump to a top-level
  line carries on as you would expect; the Jump help says so too.
- **Each sound in a line has its own volume and start.** When a line's words
  hold a sound's pattern, a Sounds box under its Text lists each one in the
  order it comes. Volume is a share of the sound's own, from silent to five
  times as loud. Starts says when, and the sentence under it says what the
  number means, so nobody has to guess what zero or a minus means: the first
  sound starts that many seconds after the line appears; each other one that
  many seconds after the one before it ends, or with a minus, that many
  seconds before it ends, playing over it. **When typed** starts it as the
  typewriter reaches it instead. **Show its words** off takes the pattern out
  of the line the player reads - in every language - and the sound still
  plays; the Game look shows the line without it. Play plays the line's
  sounds as the game will. Until now a line's sounds all started together,
  or a repeated one every 0.22 seconds per letter between them, always at the
  sound's own volume.
- **Players are told ModForge is there.** The first time the game starts with
  this version, a small window on the main menu says where packs go and where
  they are listed - in the player's language, which ModForge takes from the
  computer until they pick one. Players of an older ModForge get it too, with
  a line on what is new for them. After that it only comes back when an
  update changes something for players, as a line or two. The "?" on the pack
  list shows it again.

### Changed

- **ModForge in the game looks like the game.** Its windows - this notice,
  the warning before a save, the language warning - and the pack list and
  language flags on the main menu are drawn with the game's own rounded navy
  panels, fonts and white buttons, with the game's blur behind a window, and
  ModForge's amber and anvil marking what is ModForge's. Windows spring in and
  fade out instead of appearing. The dim behind a window really dims now: in
  the game's lighting it only ever took the menu half as dark as meant.
- **The game's colours in the plain node list.** A speaker's name is in
  their colour, words in brackets are in the game's blue, and a colour a line
  asks for shows - in the plain rows as well as the Game look. Each is made to
  read on your theme: a colour too dark for a dark theme, or too light for a
  light one, is moved in lightness only as far as it takes, keeping its hue.
  In a dark EX theme they glow, very slightly - and more faintly still in
  the Game look, where the game's colours are shown as the game shows them
  (on the game's own dark panel, so there in every theme). Before, the
  plain rows showed brackets and
  names in the ordinary text colour, and a line's own colours as they were,
  which on a dark theme could all but vanish.
- **Lists name things once.** A row shows the runtime name in brackets only
  when it says something the name does not - "Beach Day" rather than "Beach
  Day (BeachDay)" - so the end of a long name is no longer what gets cut off.
- **Places are listed by their runtime name**, spelled out as words: "Harbor
  Home Bedroom" rather than a "Bedroom" that could be any of several.
- **A place has one name for the game.** Its level's object is named by its
  runtime name and follows it when that changes, as the runtime name follows
  the display name. The Internal GO name box is gone - except for a place
  from an older pack whose object is already named otherwise, which keeps
  that name (another mod may find the level by it) until you press "Use the
  runtime name".
- **The issue count in the status bar opens the issue list** on the ModForge
  tab.
- **Copy all, Paste and Overwrite show while you are on their list.** Under
  every list of conditions or actions they sat greyed most of the time; they
  now appear when the mouse comes onto the list (or the keyboard does), in
  the room they always had, so nothing moves.
- **A quest's Tasks start with how the quest moves along.** It was a paragraph
  at the very end of the page, under everything; it is now the first line of
  the Tasks help.
- **SFX: Auto-trigger patterns are right under Audio**, before the sound's
  editor and effects - they are what a dialogue author knows the sound by.
- **A character's typing voice has sliders.** Frequency and the two ends of
  the pitch range each have a slider beside the number, wide enough for every
  voice the game has (25 to 45 characters a second, pitch 0.2 to 3) with room
  either side; you can still type a number past either end. Each has a Reset
  - to the character's own voice for one of the game's, and now to the
  default for a character of your own too.
- **Scenes: frame, sound and Starmaker share one panel.** Each was a panel of
  its own for one or two fields.
- **Fields stop at a sensible width** instead of running the window's width:
  a name, a number or a pick from a list at most 440 pixels wide, a slider
  420, a file path or a sentence a player reads 640 - in every tab and in
  every condition and action. A variable's 20-letter name no longer sits in a
  box 700 pixels long. A dialogue line's own text still takes the room.
- **Long panel help folds away.** The paragraphs of instructions at the top
  of a panel - the Nodes help, Repeat for each, Else-if branches, Level art,
  Auto-trigger patterns, speech expressions, a quest's Tasks and more - are
  read once and took the room of the fields under them on every visit after.
  In Classic each is now its first line with "more" after it; in EX it sits
  behind a "?" at the end of the panel's title band. Opened, it stays open
  for that panel until you fold it again.
- **EX's light themes no longer glow.** What is pointed at or typed in, the
  open tab and the chosen picture glowed in the theme's colour in every EX
  theme; on a light one the glow read as a blur round the words rather than
  as light. A light EX theme now shows those by their accent borders and
  lines alone, with the words sharp; the dark ones keep their glow.
- **EX: the rail counts.** Under each tab's name, how many records the pack
  has there, and on the ModForge tab's icon a badge with the number of
  issues.
- **EX: Save, Validate and Export beside the pack's name**, the three things
  the File menu is opened for most, with a dot and "unsaved" while there are
  changes not saved yet.
- **EX: the lists down the left are closer together**, a row 18 pixels high
  rather than 24, with a faint line in the theme's colour under each, from
  one side of the list to the other, so one row reads from the next; a row
  lights up the whole way across. A folder's contents hang from a line under
  its arrow, each on a short branch of it, so what is in which folder reads
  at a glance. They were noticeably further apart than Classic's.
- **Dialogues: Identity and Start conditions fold.** Click a panel's title to
  fold it, so the node list gets the room. It is remembered for each dialogue
  - folding one does not fold the others - and folded, the panel still says
  what it holds: the names, the priority and what is ticked, and each start
  condition in a word or two. A fold is the editor's to remember, never the
  pack's.
- **Dialogues: the preview is one line when there is no bust to show** - a
  line nobody with a bust speaks - instead of a large black square.
- **Previews are centred, and as large as you drag them.** Each preview -
  places, scenes, wallpapers, a line's bust, and the character and NPC busts -
  has a mark in its bottom-right corner: drag it to make the preview bigger or
  smaller, in proportion; double-click it for the preview's own size. The size
  is remembered, and a preview is never shown larger than the room there is.
  The previews sit in the middle of the page, except the character and NPC
  busts, which stay where they were. A character's bust snaps to the sizes
  its picker offers.
- **Previews are crisp when enlarged.** The game is pixel art, so a preview
  made bigger is drawn pixel by pixel; the character bust at 1.5× was
  smoothed, and looked out of focus. Made smaller than life, a picture is
  still smoothed, or whole rows of it would go missing.
- **Places: the hierarchy and the selected object sit beside the picture**,
  not over its corners. Both are as tall as the picture and scroll, the
  hierarchy as wide as its longest name; neither is resized by hand any more.
  A bigger picture shows more of each list rather than bigger words, and the
  lists only give way - narrower, scrolling sideways - once the picture is
  dragged wider than the room. With nothing selected, the panel on the right
  says how to select something.
- **Variables: where it's used.** Under a variable's definition, every place
  in the pack that uses it - what sets it, what reads it, what shows it in its
  words - each a link that takes you there. It is the question every rename
  and delete raises, and the list is the same one a rename follows.
- **The Themes menu stays open while you pick**, so you can try one theme
  after another without opening it again each time - from EX to Classic and
  back as well.
- **The menus, tidied.** Themes is under Options now (Options ▸ Themes): it is
  set once, and did not need a place on the menu bar. Options is in groups
  under headings - Look, Writing, Saving and exporting, The game, Updates -
  instead of one long list, with the theme, the dialogue rows' look and the
  preview quality together under Look, and the three update settings
  together. Edit has Undo and Redo at the top: Ctrl+Z and Ctrl+Y always
  worked, but nothing on screen said so.
- **The main menu's language in use reads better.** Its row is no longer
  orange with dark words on it: it is the game's navy, framed in amber, with
  its name and its flag's outline in amber.
- **The pack list on the main menu is clearer.** It is a panel as wide as its
  widest line, so nothing runs off its edge; the hint under it is one short
  line ("Drag to reorder: the lower pack wins a clash"), and a pack that
  clashes says "Clashes with" the other. A pack's name stays white whatever is
  said about it, its line lights up under the pointer, and a grip at its end
  shows it can be dragged. A folder button beside the "?" opens the Mods
  folder.

- **Variables and Integration are the last tabs**, after UI and Quests. They
  affect everything else in a pack, so they now come after it, in both
  looks.
- **EX themes: the same editor, laid out another way - and the default.**
  The Themes menu now has two lists: EX, which a first start opens in
  (Frost), and Classic, with every theme so far. An EX theme
  is a different take on the window you can tell from across the room: the
  tabs stand in a rail down the left with an icon over each name, the pack's
  name heads the page in large type, every panel is a tile with its title on
  a band of the theme's colour, and the status bar is the theme's colour too.
  Corners are cut at an angle, what you point at or type in glows, boxes to
  type in are lines, choices are diamonds, and the typeface is narrower. The
  tabs, their order and how you move between them are the same as in
  Classic, and everything works exactly as it does there. Thirteen of them:
  Frost, Volt, Cobalt, Mint and Solar (light); Neon, Pulse, Venom, Blaze,
  Phantom, Abyss, Aurum and Jade (dark). Switching takes effect at once.
- **Themes of your own.** Themes > Your themes… makes them: start from the
  theme you are using, pick EX or Classic, and set the six colours every
  theme is made of - background, bars, panels, boxes, text and accent - by
  typing them or with the colour picker. The editor shows each change as you
  make it and keeps it as you go; light or dark follows the background, the
  words are moved towards readable on every colour, and if your colours
  leave some hard to read anyway, the window says so. They are listed under
  Themes > Yours.
- **Dark themes have a dark title bar.** Every dark theme had Windows' white
  title bar over it. On Windows 11 an EX theme also gives the title bar its
  own colour and draws its accent round the window's edge.
- **Themes, reworked - and nine new ones.** Every theme is now a base and one
  accent, the way code editors' themes are made: the accent marks what is
  selected, focused or open, and everything else follows from the base.
  Themes > Classic lists them under Light and Dark: Light, Slate, Forest, Plum,
  Ocean, Sandstone, Rose and the new Paper and Daylight; Dark, Midnight,
  Carbon (now with gold), and the new Graphite, Nightshade, Fjord, Ember, Deep
  Sea, Moss and Wine. Buttons have a colour
  of their own instead of the panel's, every box you type in looks the same,
  check boxes, scroll bars, sliders and groups follow the theme, and the open
  tab sits under a bar in the accent's colour.
- **Every issue says where it is, and double-clicking it goes there.** The
  issues list showed the editor's own address for a problem, cut off after
  twenty letters - "dialogues[NagaDialogueSe..." - and double-clicking a
  problem in a line's actions only opened the dialogue, leaving you to hunt
  for the line. Now it reads like "Naga at the beach > "Huff! One more time"
  > on start: Set Active", and double-clicking selects that line. Issues on
  scenes, NPCs, wallpapers, rules, UI screens and the game's quests can be
  gone to as well; before, double-clicking them did nothing.
- **Problems in a translation are in the issues list, and open that
  translation.** A translated line that lost a {PC} or a tag the default text
  has - so its players read it without the name, or in the wrong colour to
  the end - is listed whatever language the editor is showing, and
  double-clicking it shows the line in that language. So does a line that is
  too long in one of the translations.

- **Play goes through a sound's variants in turn.** A sound with variants
  (Plap.ogg, Plap_1.ogg, Plap_2.ogg) plays the next one each time you press
  Play, and says which it played - in the SFX tab and from a dialogue line's
  sound menu alike. It only ever played the first, so the others could not
  be heard from the editor at all. In the game, Play SFX and the sound cues
  in a line already pick one at random each time.
- **The line that follows a playing sound moves smoothly, and shows where it
  ends.** It used to jump along in steps and, on a short sound, reach the end
  before the sound had played - then vanish before it finished. It now follows
  what you hear, every frame, and stays a moment where the sound ended.
- **The same buttons over every list, each with its key.** Every tab's list
  has Add, Folder, Rename, Duplicate and Remove, in that order, then what only
  that tab has; the key for each is written small under it - Ins, F2, Ctrl+D,
  Del - and the keyboard sign at the end lists the keys that have no button.
  Rename is back, and renames the selected item exactly as typing in its
  Display name box does. Buttons that don't fit go onto a second row rather
  than behind an arrow, where they could not be found. Ins is new: it adds to
  the list you are in. The NPCs list now answers the same keys as the others
  (it answered none), and so do the lists of the game's places, conversations
  and quests.
- **Wrong values show in red.** A name that is none of the things its box
  lists - an actor, a tag to jump to, a scene, a sound, a variable, a place,
  a bust or a face in an action or condition - a number box holding something
  that is not a number, and a file path with no file at it are drawn in red
  as you type, so they are caught before the game meets them. Only what is
  certainly wrong: an empty box, a {placeholder} a rule fills in and a
  $variable read in game are left alone. Actions and conditions that name a
  speaker show each one's face too.
- **A sound's pattern is marked in the line.** Where a line's words hold a
  sound's auto-trigger pattern (*plap*), it is underlined - in the Text box,
  in the Nodes list, and in the Game look.
- **The Game look colours text in brackets the way the game does**: light
  blue, read from the game's own setting, counting nested brackets and
  letting a colour of yours inside them win as it does in game. The "In
  dialogue text" panel says so, and that patterns play their sound.
- **The save prompt says what changed in plain words.** It used to list the
  file's own insides in three cut-off columns: a line's number
  ("2137553408"), lists of numbers twice over, "actionsOnStart › #1",
  "{ id: …, kind: Text, +4 nested }", field names like "defaultValue", and
  a "different order" row that was not true. Now each change sits under the
  thing it belongs to - "Dialogue · Chat at the cave" - and reads like the
  issues list does: "“Hi there” › Expression" with "Happy → Sad" under it,
  "New: “One more thing before you go.”", "Removed: “Maybe later.”", a
  sound's "Default volume" with "0.667 → 0.65". The values show only where
  they say something, a long line shows the part that changed, everything
  wraps to the window instead of running off it, and a translation's
  changes name the line instead of its key.
- **Smart suggestions: an empty dropdown opens with the likely picks on
  top.** What you are editing decides what is likely - everything in the
  dialogue, scene or rule you have up: the characters it names anywhere, the
  place it happens at, its variables, the folder it is in. In a dialogue
  where a character talks at one place, a Set Active action's scene list
  opens with that character's scenes there first; a Play Sound list, with the
  sounds the dialogue already plays; a variable list, with the variables
  about that character and that place. The ones moved up carry a small star
  and a line sets them apart; nothing is picked or hidden, other dropdowns
  showing the same list keep their own order, and a box that already holds a
  choice or a search opens as before. It follows your edits as you commit
  them, and the reading behind it happens in the background, so opening a
  list costs nothing you can feel even on packs many times the size of a
  big one. Options > Smart suggestions in dropdowns turns it off.
- **Outfit and Expression are buttons with pictures.** A line's outfit is
  picked from the speaker's outfits, each with a picture of the bust, or Keep
  to leave the character in what they are wearing (pointing at Keep says
  so); its expression from the speaker's faces, each with a small drawing of
  the face. Only what the speaker has can be picked, so a name that does not
  exist can no longer be typed in by accident - and a line that already holds
  one is shown it, in red, to be replaced. An outfit can still come from a
  variable: tick "From a variable" and pick it, and the outfit buttons fade
  and step aside while it is ticked. Which one the line has is plain in
  every theme. Outfit now comes before Expression.
- **Faces have pictures on the Characters tab.** Each of a character's faces
  has one of 26 drawings covering the common expressions, picked from its name
  when you name it and yours to change after; the game's faces get theirs from
  their names. The name suggests common ones as you type and is set when you
  leave the box, so a suggestion opening never decides it. The bust child is
  read-only and follows the name; the ✎ beside it sets one of your own.
- **New lines start on neutral** when somebody speaks them - the face every
  character has, which looks the same in game as no face.
- **Names in the node editor start with a capital letter** - in the Actor
  box, its list, the line rows and the face buttons ("anis" reads "Anis").
  Only how they are shown: the pack keeps every name exactly as it is, and
  the Actor box shows what is really there once you click into it.
- **The Actor picker shows each character's face** beside their name, cut
  from their bust, and is as wide as the longest name rather than the whole
  panel. A line nobody speaks says so and hides Expression and Outfit, the way
  the player's lines do; if it still sets a face or an outfit, the issues
  list asks whether its actor was forgotten - the game ignores both on a line
  nobody speaks. Neutral doesn't count: it is no face.
- **A line's other fields, tidied.** Its ID and children show when you hover
  over the line in the Nodes list, in either look, instead of taking two
  rows; Kind, Then and Duration are rows of radio buttons; Tag is behind a
  tick box, with its field beside it. "Then" was called "Jump" - one of its
  own choices is Jump too. Duration's "UntilInteraction" reads "Until
  interaction".
- **Boxes that do not need the whole width no longer take it.** Numbers,
  names and tags stop at a sensible width, and a preview's box is as wide as
  the preview rather than the window; the Places preview grows with the
  window up to its normal size.
- **The buttons under a conversation's lines.** "+ Root" is gone and
  "+ Sibling" comes before "+ Child"; a conversation with no lines yet shows
  "+ New Node" alone. Each + now moves the selection and the keyboard to the
  line it made, instead of leaving them on the one it came from. + Sibling and
  + Child take only the speaker - actor, expression and outfit - where they
  copied the whole line, conditions, actions and jump included, which then had
  to be cleared out by hand. To copy a whole line there is the new
  **Duplicate** (Ctrl+D), which copies it with everything under it, and copy
  and paste.
- **Keys go to what you clicked last.** In Dialogues, Ctrl+D copies the line
  when you were last in the lines - even typing in its Text - and the
  dialogue when you were last in the dialogue list; the key caps of the side
  the keys go to are drawn in the heading colour. The objects of a
  UI screen and a quest's tasks have the same bar over them, with their keys
  under the buttons.
- **A runtime name follows its display name.** Rename a duplicated scene
  and its runtime name loses the "_copy" instead of keeping it for good. The
  same goes for places, NPCs, dialogues, music, sounds, wallpapers and rules
  whose runtime name is just their display name run together, and everything
  in the pack that used the old name follows, as renames have since 1.7.0. A
  runtime name you typed yourself stays as it is, and so does a Starmaker
  photo's scene, since players' saves remember those by name. The runtime
  name is still read-only; the ✎ beside it sets one of your own, which from
  then on stays. Rename (F2) now renames the display name, exactly as typing
  in the box does.
- **Scenes you duplicated before this version** still say "_copy": select
  one, press F2 and Enter, and its runtime name catches up with its name.
- **A runtime name left behind by an older rename catches up too.** A
  conversation renamed from ...Story05 to ...Story06 before this version kept
  the runtime name ...Story05. Rub its name back into the part the two share -
  delete the 6 - or type over that part, and the runtime name follows the name
  again from there. A runtime name you typed with the ✎ is never taken back
  this way.
- **A new scene starts with the "sexy" frame and the kiss sound**, the way
  the game's own scenes beside it are made, instead of the photo frame and no
  sound. Scenes you already have are not changed.

### Fixed

- **An action's Level could be wiped when its row was drawn again.** A Set
  Active (or Fade, Move, Spin) action set to a level could lose that level
  when you came back to its line: the Level box, being built, picked the
  saved level and for a moment had no text, and that empty moment was
  written over your choice. The boxes of the other kinds of action, hidden
  in the same row, could do the same. Typed text in those boxes now counts
  when you leave the box (picking from the list counts at once, as before),
  and a hidden box changes nothing.
- **Two packs colouring the same character: the wrong one could win.** When
  two packs both give one of the game's characters a name colour, the pack
  lower in the main menu's list is meant to win, and the menu says so. It did
  until one of the other pack's own lines played; from then on that pack's
  colour showed for the rest of the session, in the game's conversations too.
  The pack lower in the list now always wins. Found by playing it in the game.
- **The actions on a choice's options never ran.** In this game, picking an
  option goes straight on to the lines under it - the option itself is never
  played as a line - so its actions, on finish (which the editor suggests) or
  on start (in older packs), did nothing, with no message anywhere. They now
  run the moment the player picks that option, and the note on an option and
  the warning about an option's actions on start say so. Found by ModForge's
  new in-game checks, which pick choices in a real game.
- **A pack variable in a line, written [PV:name], showed the value it had when
  the game was loaded**, not the one it has when the line is said - so "You
  have [PV:Coins] coins" kept showing the number from when the save was
  loaded, however many were won since. The line now reads the variable as it
  is said, as the editor's help always described.
- **A tutorial could stop you at a step whose control was not on screen
  yet.** A step lights the control it is about and dims the rest, and the
  dim takes no clicks. The controls were looked for only as the step began,
  so one that appears during it - a line's editor, once you choose the line -
  stayed dimmed, and the step could not be done. They are looked for until
  they appear now, and scrolled to when they do. "Put the same person in
  twice" also lights the place list, so you can choose the room. Every
  tutorial was followed through in the real window, in Classic and in EX:
  both light the same controls at every step.
- **A misspelled variable in a condition went unnoticed.** Since 1.2.0, when
  the variable checks became one Variable condition, Validate had not looked
  at them at all: a variable the pack does not have, one of the game's that
  does not exist, a comparison the game does not know, or "greater than"
  nothing passed without a word, and in the game the condition quietly read
  the default. Validate checks them again - a `$name` in the value too.
- **Pasting or duplicating a line that has a tag** gave the conversation two
  lines with that tag, so a jump aimed at it could land on either. The copy's
  tag is renamed now, and the copy's own jumps follow it.

- **Unreadable text in the dark themes.** A selected dialogue in Midnight was
  white on white; the dropdowns you can type in - Actor, Expression, Outfit, a
  condition's Level - were white boxes with pale text, so they looked greyed
  out; the windows that open from the editor (the save prompt, translations,
  the colour picker) stayed white whatever the theme; hints in grey fell short
  on dark panels. Every theme now passes a readability check on every tab, run
  by the tests on each change.
- **Dropdowns with words already in them opened onto every option.** A box
  that still held something typed earlier - half a name, a path - showed the
  whole list when opened, as though it were empty. It now shows what those
  words find. A box holding a choice still opens onto the whole list, since
  that is how you pick another, and words that find nothing do too.
- **A warning about a scene an action doesn't use.** Changing an action's
  type keeps what you had typed for the old one, in case the change was a
  slip - and the check went on reading it. A Character Focus that used to be
  a Set Active was warned that its "scene" did not exist, about a value the
  action never uses and the editor never shows. Only what an action's type
  actually reads is checked now. The two warnings about a missing scene or
  sound also no longer claim that renaming leaves references behind: since
  1.7.0 they follow the rename.

- **A new record could jump out of its folder.** Make a folder, add a scene
  (or a place, NPC, wallpaper, track, sound, dialogue or rule) inside it, give
  it a name - and the next undo, or anything else that rebuilt the list, put
  it back at the top level, outside the folder. Naming a new record also
  renames it behind the scenes, and the folder went on looking for it by the
  old name. Folders now keep up with the name as it changes.

## 1.7.0

A release mostly about sound and about finding your way round the editor.
Sounds and music tracks can be cut and given effects in the editor, and are
written over their own files when you save; the Play buttons sound as loud as
the game does, and a volume can go up to five times the game's own level. A
Transitions action covers the screen while a scene changes behind it, and Wait
finally waits. The game's main menu lets you put your packs in order and warns
when two of them change the same thing. Renames follow everywhere they are
used, dropdowns are under headings with a short note beside each item, and
dialogue lines show the game's name tokens in each character's colour. The
plugin changes too, so update both.

### Before you update

**SetSpriteFocus is called CharacterFocus now.** Same action, same Focused
setting, a name that says what it does. A pack that uses it is given the new
name when it opens, you are told how many were renamed, and the original is
kept beside the manifest on the first save. The game answers to both names, so
a pack nobody has re-saved still works - but a pack saved with this version
needs this version's plugin, so update both.

**No pack recolours the player any more.** The game writes "You" above every
player line in its own grey, and a pack holding a colour for the player - one
edited by hand, or saved before the player was fixed - repainted it in every
scene of the game for as long as it was loaded. The editor has never let you
choose one, so most packs have nothing to change. One that does has the colour
removed when it opens, is told so, and keeps its original beside the manifest
on the first save; and the game now ignores a colour for the player, or for any
pack character shown as "You", in packs nobody has re-saved.

**A track's Volume reads differently, and sounds the same.** The Music tab's
Volume now reads 1.0 for the game's own level for a track, like the SFX tab's,
where it used to show the number the game is given - half of that. A track you
had at 0.8 shows 1.6, and plays exactly as loud as it did.

**Wait waits now.** In the game it never did: the actions after a Wait ran at
the same moment as the ones before it, whatever its Seconds said. They now run
that many seconds later, as the editor always claimed. A pack with a Wait at
the end of a list, or with nothing after it, plays exactly as before; one with
actions after a Wait will now pause there, so play through anything you set up
around one.

### New

- **A Transitions action.** Pick a transition and how long the screen stays
  covered, and the game plays both halves: it goes in, holds, and comes back
  out on its own. It used to take one signal to go in and another to come out,
  timed by hand with a Wait that did not wait. The choices are the game's
  own fade to black, its blink, its white flash and its travel fade, and a
  black screen with text - the game's "A few minutes later..." screen with
  any words you like on it, translated with your pack's other texts. The
  actions after a Transitions wait until the screen is covered, so whatever
  they change - a scene, a place, who is standing there - happens out of
  sight. It draws over everything, the dialogue box included, and the screen
  cannot be clicked through while it is up. The blink and the flash run the
  game's own course, so they have no time to set.
- **Sound cues over a dialogue line.** A new ♪ button beside the formatting
  buttons lists the pack's auto-trigger patterns - *door*, *smooch*, whatever
  your sounds answer to - with the sound each one plays. Pick one and it is
  written into the line where the cursor is, spaced off the words around it;
  press the ▶ beside it to hear the sound first, as edited if you edited it.
  No more trips to the SFX tab to remember whether it was *kiss* or *smooch*.
- **Dropdowns under headings.** Every list that holds both the pack's things
  and the game's now says which is which, with the pack's own on top: the
  busts, levels, GameObjects and NPCs of Set-Active and the actions and
  conditions like it, bust fields, expressions, the outfits on a line, where a
  navigator or map button goes, and a screen's button sound. Lists of the
  pack's own things follow your folders, as the variable pickers already did:
  scenes, sound effects and list variables. A screen's objects are under the
  screen they are in, and the action and condition types are under what they
  do - Characters, Objects and scenes, Places and the screen, Sound,
  Variables, Timing and chance, Quests. Components are under made for packs,
  the game's scripts and Unity's own; the game's conversations under the
  folder each is in; the game's screens under the object each belongs to.
- **Notes beside dropdown items**, the way Direct Path has them: what each
  action and condition does, what each target category reaches, what each of
  the game's signals sets off (flash is a camera flash with its sound, drink a
  blink with a drinking sound), what each Variable operation and component
  does, a game quest's journal line, a game screen's contents - and in
  Set-Active's Bust list, whose bust each is: "Anna" beside one of hers, "Elf -
  party" beside an outfit you made. The note shows only in the open list, and
  typing searches it too, so "camera" finds flash.
- **Name tokens in their character's colour.** In the dialogue lines, {M} is
  drawn in Anna's name colour, {D} in Josef's, {B} and {S} in Adrian's, {DA}
  in Emma's and {PC} in the player's light grey - your pack's colours for
  them, if you changed any - still on their dark chip, in both the Game look
  and the plain rows. {F}, the family as a whole, keeps the blue it had.
- **Volume sliders, and louder than the game.** A sound's default volume and
  a track's volume each have a slider from 0 to 5 beside a short box, and
  both start at 1.0 - the game's own level. Past 1 is louder than the game
  goes by itself: up to five times for a track, and for a sound the same
  squared curve the game uses below 1, so 2 is four times as loud. The game
  plays it that loud too. Before, anything past 1 did nothing, in the game and
  in the editor - which is why changing the volume above 1 made no difference
  to Play.
- **Play sounds as loud as the game.** The Play buttons in the SFX and Music
  tabs and in the sound editor now play at the level the game does, read from
  the game's own files: everything it plays goes through one mixer that holds
  it about 10 dB down, and a sound effect's volume is squared on the way out
  (0.5 is a quarter). They used to play at the volume as typed, and music at
  full when it had none - all of it louder than the game would ever play it.
- **Edit the sound in the Music tab too.** The same strip, cuts and effects as
  the SFX tab, for a music track. "▶ Play" on a long track takes a moment to
  get ready the first time it plays from a spot; it says so while it does.
- **Arrange your packs on the game's main menu.** Drag a pack up or down the
  list there; a line shows where it will go. The list is the order packs load
  in, and where two packs change the same thing of the game's, the one lower
  in the list wins. Until you move one, the list is alphabetical - Z over A,
  which is how packs have always loaded - and a pack you install later goes to
  the bottom. Which dialogue starts when two packs' dialogues could start at
  the same priority does not depend on the list: that stays alphabetical, so
  moving a pack never changes which conversation plays.
- **The pack list warns when two packs change the same thing.** Both get an
  amber "Changes the same as ..." beside them, and the game's log says what
  they both change and whose change shows. Only the game's own things can
  clash - its characters, the objects in its levels and screens a pack
  overrides, the lines of its conversations a pack rewrites or takes out, and
  the tasks of its quests; a pack's own scenes, places and sounds are its own.
  Finding them reuses the packs the menu already reads, so the menu does not
  take any longer to appear.
- **Edit a sound in the SFX tab.** A new "Edit the sound" box shows the sound
  as a strip, like Audacity's but one channel tall. The mouse wheel zooms in
  and out around the pointer (Shift and the wheel scroll), a click picks a
  spot and highlights the piece it is in, "✂ Cut here" splits that piece in
  two - as many times as you like - and "Delete piece" takes the highlighted
  one out, the rest closing up behind it. Under the strip are Pitch (in
  semitones, keeping the length), Speed (keeping the pitch), Echo and Reverb.
  "▶ Play" plays the sound as edited from the spot picked, with a line
  following it. A sound with variants (Plap_1.ogg beside Plap.ogg) lets you
  pick which file is on the strip; each is cut on its own, and the effects
  apply to all. When you save, the edited sound is written over its file -
  same name, same format; an MP3 becomes an OGG of the same name, since
  nothing here can write MP3 - so the pack holds one file per sound and ships
  one. The recording is kept in a .originals folder in the pack, which the
  export leaves out: every later edit is made from it again rather than from
  the last edit, and Reset gives it back on the next save. A recording another
  sound also plays is not written over - that would change the other sound
  too - and gets a file of its own in an "edited" folder instead. Undo works
  on every cut and every slider.
- **A rename is followed everywhere, as soon as you are done with the box.**
  Rename anything in its own box - a character, an outfit, a face, a variable,
  a place, a dialogue, a scene, an NPC, a music track, a sound, a quest or a
  task, an object in a place, an NPC placed in one - and when you leave the
  box or press Enter, everything in the pack that uses the name follows it. A
  small notice in the corner says how many places changed; it used to be a
  dialog, which got in the way of the click that left the box.
- **Folders are renamed where they stand.** The Rename buttons on the tabs'
  toolbars are gone: they only ever renamed folders, and with a record
  selected they did nothing at all. Right-click a folder and pick Rename, or
  select it and press F2; Enter keeps the new name, Escape puts the old one
  back.
- **A Pack folder button**, right of Edit… at the top: it opens your pack's
  folder in File Explorer, where its art, sounds and translations are and
  where a file has to be for players to get it. It is greyed out until the
  pack has a folder - a new pack gets one when it is saved.

### Changed

- **EmitSignal no longer offers DialogueStart, DialogueEnd or the
  transitions.** A dialogue sends DialogueStart and DialogueEnd itself, so
  sending them by hand only ever doubled them; the fades, the blink and the
  white flash are what the Transitions action is for. A row that already
  sends one still sends it, and you can still type any signal into the box.

### Fixed

- **The player's name was the wrong colour.** The editor wrote it in white,
  as though the game gave the player no colour. The game writes "You" above
  every player line in a light grey, #B0B0B0 - read from the game's own files
  - and the editor now does too, on the dialogue rows and in the Characters
  tab.
- **8 and 9 were missing from the Game look.** Lines drawn the way the game
  draws them showed nothing where an 8 or a 9 was, and the same for + @ # < =
  > { | }. The game itself always showed them: its dialogue font fills in
  characters as lines need them, from the Curse Casual font file, and the copy
  that comes with ModForge was taken before anything had needed those. The
  preview now fills them in from the same font file the same way, so they look
  as they do in the game - and the note under the Text box, which measures
  whether a line fits, counts them at their real width instead of none.
- **Two group headings were in English in every language.** The variable
  pickers filed loose variables under "Ungrouped" and the game's variables
  without a list under "Other", whatever language the editor was in.
- **Opening a recent pack could throw away what you had just typed.** With a
  box still being typed in, File > Recent opened the other pack without asking
  whether to save, because the box had not handed its text over yet. What is
  being typed counts as a change now, there and everywhere else the editor
  asks before closing a pack.
- **Renames left references behind in a lot of places.** Typed params were
  followed, and little else. An action or condition aimed at a scene, a bust,
  a place, an object or an NPC through its Category and Target was left
  pointing at the old name, and so were Level fields, the music a button
  changes to, the sound a screen's buttons make, rows inside a dice roll,
  a screen button's actions and conditions, and an object's own conditions.
  All of them follow now. A face renamed on one character follows only that
  character's lines - everybody has a Happy - and an object renamed in a place
  follows only the rows that lead through that object, not another of the
  same name.
- **A new record's references were lost when you renamed it while its key
  still followed its name.** A scene, place, dialogue, NPC, track or sound you
  have just added takes its runtime name from what you type as its name, and
  anything made to point at it in between was left pointing at the old one.
- **Size labels no longer say art has to be 256x256.** The bust sprite fields
  and the Sprites and expressions box said "256x256" as though nothing else
  worked, when art of any size is scaled to fit - 256x256 is the size whose
  pixels land exactly as drawn, and the fields now say so. The front mask of a
  place said 256x143, which was never true: a mask can be any size, and
  ModForge's own mask painter writes 256x256 ones. It now says it is optional,
  like the back mask beside it.
- **Your pack's translations follow a rename.** They file each line under
  the name of what it belongs to - a dialogue's key, a character's - so
  renaming one moved its translated lines to "not used" and the game showed
  them in English. They move with the rename now, written with the next save,
  and an undone rename leaves them where they were.
- **Borrowed faces match your bust in the preview.** On an outfit you added to
  one of the game's characters, "= default" borrows the game's own blink,
  mouth and faces - and the preview drew them from the smaller copies that
  come with ModForge, stretched back up, so they never matched the sharp
  256x256 bust you drew. ModForge now comes with those faces at the game's
  full size too, and a borrowed blink, mouth or face is drawn from them. The
  game's own outfits still preview from the smaller copies, as before, and the
  game itself always used its own full-size art.

## 1.6.3

A release of smaller improvements. Actions and conditions show a picture of
what they are aimed at, and SetSprite shows it before and after; NPCs get a
category of their own; Direct Path suggests the game's own objects worth
knowing; a file from outside the pack's folder is caught before it goes
missing for players; outfits added to the game's characters can borrow the
game's own art; and a line can be kept from being translated. The plugin
changes too, so update both: the game's pack list marks a pack made with this
version for a player whose plugin is older.

### Before you update

**Targets still in "Extra GameObjects" move to GameObjects.** That was the
GameObjects category's name until July, and neither the editor nor the game
learned the old name when it changed: such an action or condition showed an
empty Category and no Level, and in the game its object was looked up by name
anywhere instead of inside its level - right while the name was unique, the
wrong object when it was not. Opening the pack moves them over, the list of
what was brought up to date says how many, and the first save keeps a copy of
the original. The game also reads the old name correctly now, for a pack that
has not been saved since.

### New

- **An NPCs category.** SetGameObjectActive, SetSprite and the
  GameObjectActive condition can now aim at an NPC directly: pick the level,
  then the NPC. The list holds the NPCs you placed there and, in a level of the
  game's you extend, the game's own NPCs too - the crowds, the convention
  guests, the stage audience. FadeSprite, MoveGameObject and SpinGameObject
  have it too. The NPCs no longer crowd the GameObjects list, which keeps the
  groups they stand in and the level's other objects. Actions you already
  aimed at an NPC through GameObjects keep working as they are.

  Where several NPCs share a name - most of the game's are called "NPC" -
  each is listed by its place in the level instead, so every entry finds
  exactly one.

  SetSprite on an NPC also swaps its floor reflection to the new pose. Its
  blink still uses the eyes-closed picture it had.
- **Direct Path suggests paths into the game worth knowing.** Its list used to
  offer your pack's own object and bust names - which the GameObjects, NPCs
  and Bust categories reach properly, and which aren't written the way a
  Direct Path is. Now it offers the game's own objects a pack most often
  switches, taken from what the game's own dialogues switch most, grouped
  under Game actions, Player's stats, The game's interface, Screen effects,
  and Music and sounds. Each has a short note beside it saying what it is,
  with an example: "Disable_All_CG - Hides the CG on screen. e.g. Activate as
  a scene ends". Picking one writes its full path, slashes and the game's
  own spelling included. You can still type any path.
- **See what an action or a condition is aimed at.** SetGameObjectActive,
  SetSprite and the GameObjectActive condition now show a small picture of
  their target right under it, so you pick it by how it looks as well as by its
  name:
  - **Scene:** the scene, frame and all - a still, a GIF playing, or a
    video's first frame, as the Scenes tab shows it.
  - **Bust:** the bust, as your pack draws it or as the game does.
  - **GameObjects:** the object's picture, in the level chosen above.
  - **NPCs:** the NPC's pose.
  - **Places** (SetSprite only): the layer the Layer picker names.

  **SetSprite shows before and after**, side by side with an arrow between:
  what the target looks like, and what your sprite turns it into. A scene
  keeps its frame in both, as in the game. "Before" is how your pack sets it
  up; something that ran earlier may have changed it since.

  A picture appears only once the target names something the game will find,
  the way the game looks it up (capitals included, except for a bust's name,
  which the game doesn't mind), so one that
  doesn't show is also a sign the name is off. A UI or a Direct Path shows
  none: those only exist in the running game. The pictures are built only for
  rows that show one and are shrunk to the size they are drawn, so a node with
  a lot of actions still opens quickly.
- **Two ticks under a line's text, for words that must not be translated.**
  - **Same in every language:** the line reads exactly as written for every
    player. It is never offered for translating, and the game never shows a
    translation of it, not even one left over in an older translation file.
  - **Keep the words, change only the letters:** for a made-up language, a
    spell or a chant. Translating never turns it into real words it happens
    to look like. In languages written in the same letters it stays as
    written; in Russian, Japanese, Korean and Chinese each word is spelled
    out in their letters, so players can read it aloud. A translation file
    written for translating by hand says so above the line.

  They are offered on any line whose words are your pack's, not on a line of
  the game's that you left as the game wrote it.
- **"= default" on outfits you add to the game's own characters.** The mask,
  blink, mouth and expressions buttons now fill in the character's own default
  bust from the game, where before they were hidden for those outfits. The game
  uses its own art for them, at full size; the copies that come with ModForge
  are smaller and only for the preview. A field filled in this way shows in
  italics, set apart, with a note saying where the art comes from, and typing
  a path or choosing a file puts your own art back in its place.
- **Start a mask from one of the game's.** For an outfit added to one of the
  game's characters, the mask painter's "copy from" list now offers the masks
  of that character's own busts in the game, and painting over a borrowed mask
  starts from it. Saving makes a mask file of your own.
- **The game's own characters and busts can no longer be removed by
  mistake.** - Character is greyed out for the game's characters and the
  player, and - Outfit for the game's own busts. An outfit you added to one of
  the game's characters can still be removed.
- **Files outside your pack's folder can't slip through.** Players get only
  what is inside the pack's folder, so a file anywhere else works on your
  computer and nowhere else. A file field that leads outside the folder now has
  a red border, and hovering it says why and what to do. Choosing a file from
  outside with the … button no longer just refuses: it offers to copy the file
  into your pack, beside the file the field named before or in an "Imported"
  folder, and uses the copy.

### Fixed

- **SetSprite on a bust changes the bust.** Aimed at a bust through the Bust
  category, it changed nothing and left only a line in the game's log: it
  looked for the picture on the bust itself, where the game keeps it one level
  down. It now finds it where the game keeps it, and a Mask set on the same
  action goes on that picture too.
- **The pack's translations window opens without the wait.** It worked out
  how far along every language was each time it opened, which took a couple of
  seconds on a large pack. Now that is worked out in the background when the
  pack opens, and the window only works out again the translations whose file
  changed since, or all of them after you change the pack's own texts.
- **The character preview never pushes its options out of the window.** A
  large preview size in a short or narrow window used to push the options under
  it, the size picker included, off the edge, with no way back to a smaller
  size. Now, when the size you picked doesn't fit, the preview is shown at the
  largest size that does, a line under the picker says so, and it goes back to
  your size as soon as the window has room for it. The picker keeps your
  choice.

## 1.6.2

A fix for the dialogue editor: a line given to the player could not be given
back to anybody else. Nothing changes in the game: the plugin is the same apart
from its version number.

### Fixed

- **A line spoken by the player can be given to somebody else again.** Picking
  the player as a node's actor hid the Actor field along with Expression and
  Outfit, so there was nothing left to change it back with. Expression and
  Outfit still hide for the player, whose bust is the game's; the Actor field
  now stays, and only an option of a Choice, which is a button rather than a
  line, goes without one.

## 1.6.1

A smaller release, about translating packs. A pack's translations get a
window of their own, where one can be copied over another language or deleted;
the Translate window shows how far along a run is and says when it is over; and
a translation can now actually be finished - a line of the pack that began or
ended with a space kept every language short of 100%. Nothing changes in the
game: the plugin is the same apart from its version number.

### New

- **The pack's translations, in a window of their own.** An **Edit…** button
  beside Editing in lists every translation the pack has, how much of each is
  translated (as a percentage, and in texts), how many texts have gone out of
  date since, and the file each one is in. Pick one and:
  - **Transfer** copies its texts, word for word, over another language. For a
    translation typed in while the wrong language was picked in Editing in: the
    work moves to the language it was really written in, instead of being
    typed again. Only the texts that were actually translated are copied; the
    other language keeps whatever it had where the first one had nothing, and
    the first one keeps all of its own.
  - **Delete** removes the translation and every text in it. The file goes to
    the Recycle Bin, so a wrong pick can be put back.

  Both ask first, saying exactly what is about to happen.

- **Translate the pack shows how far along it is.** Two bars instead of one
  line of text: one for the whole run across every language picked, counted in
  texts, and one for the language being translated now. Before, the window said
  which language it was on and how far into it, and nothing about how much of
  the whole was still to come.

### Fixed

- **A translation could never be finished if any line of the pack started or
  ended with a space.** Translation files don't keep spaces at the ends of a
  line, so every line like that looked changed again after each run. The
  Translate window offered the same lines every time, the pack check listed
  them as out of date, a later save marked some of them "changed from" for
  review, and no language reached 100%. Nothing about how they showed in the
  game was wrong. Those lines now count as done, the stray review marks go
  away the next time the translation is saved, and one more Translate run
  finishes the few lines the machine gave back exactly as they were.
- **The Translate window waits for you.** Saying Yes to the offer before an
  export or a publish opened it already translating, with no chance to change
  the languages or look at the names first. Now it opens with those languages
  ticked and starts when you press Translate; the export goes ahead when you
  close it. Pressing Enter in the window, for instance after typing a name's
  spelling, no longer starts a run either.
- **The Translate window says when a run is over.** Once a run ended, the
  bars stayed full under "...translated...", which looked like a run still
  going. Now the language bar goes away and the other one says "Translation
  finished." or that it stopped before the end, with what each language got
  listed below as before. If the window isn't in front when the run ends, its
  taskbar button flashes until you come back to it.
- **The pack check no longer calls lines with nothing to translate
  untranslated**, such as `<size=60%>...`, `{PC}...` or `[PV:Money]`. The
  letters inside the markup were being read as words.

## 1.6.0

ModForge in your language, and your pack in your players'. The editor comes in
eight languages besides English and switches between them without a restart;
every pack can be translated - by hand, from a file anyone can edit, or by
machine in one go - and edited in any of its languages; and the game plays a
pack in the player's language, chosen from flags on its main menu. Characters
have pronouns so translations get "ready" and "lista" right. Packs can be
switched on and off from the main menu without losing their progress, and the
Places tab's undo is fast again.

### Before you update

**Players whose Windows is set to another language see ModForge's words in
it.** The plugin's language setting starts at `auto`, which follows Windows:
a player with Windows in Spanish gets the pack list and the save warning in
Spanish, and every pack that has a Spanish translation in Spanish. Setting
`Language = en` in the plugin's config file keeps everything as it was.

**Players with XUnity.AutoTranslator: ModForge follows its language.** With the
setting at `auto`, ModForge shows its words and plays packs in the language
XUnity.AutoTranslator translates the game into, rather than Windows'. Choosing a
language on the main menu afterwards changes only the mods' language; XUnity
and the game's language are left exactly as its own settings have them.

**Every character of your pack's own needs its pronouns chosen.** The
Characters tab has a new Pronouns box - Male, Female or Neutral - and a pack
made before it has none chosen, so after updating the validation list shows an
error for each of your characters until you pick one, and Publish asks before
going ahead with them. Nothing else about the pack changes, and the game's
characters and the player need nothing: theirs come from the game.

**The player types the same way in every pack.** "You" now always types at
45 characters a second, pitch 0.4-0.7, and a pack can no longer change it: the
typing voice fields are shown greyed for the player. A pack that set a voice
for "You" has it removed when it is opened - the list of what was brought up
to date says so, and the first save keeps a copy of the original - and the game
no longer uses one found in a pack that has not been saved since. Before, such
a setting only changed "You" on that pack's own lines, while the game's own
"You" kept its voice, so the player typed two ways.

### Characters' pronouns, and translations that use them

**A Pronouns box on the Characters tab, right under the name.** English says
"I'm ready" whoever says it; Russian, Spanish, French, Portuguese and German do
not - "Я готов" or "Я готова", "listo" or "lista" - and nor do the words that
describe somebody named in a line ("mi mejor amigo" or "mi mejor amiga"). A
machine translator is told neither, and it guesses male. Choosing Male, Female
or Neutral tells the Translate window who each line's speaker is and who each
name in it is, and it passes that on. Neutral leaves the language to speak of
them the way it speaks of anybody it has not been told about.

**The game's characters and the player already have theirs,** read out of the
game itself: what its lines say about each of them, and for the ones too minor
to be talked about, how the game draws them. The player is male, as the game
speaks of them. They show greyed out, like the game's names.

**A voice-only character's settings show when you select them.** A character
with no bust - a voice, like John Dick - has no outfit to select, and the panel
with the name, pronouns, colour and typing voice only showed with an outfit
selected, so none of those could be changed for them. It shows for any
character now, with the outfit's own panels left out when there is none, and
so are the default outfit and the speech expressions, which do nothing for a
character nothing is drawn for. A voice-only character you add is selected
straight away, too; before, the selection stayed on whoever it was on.

**How it reaches the translator.** Each line goes to Google with a short note
in front of it - "She says:" - and each name with "(she)" or "(he)" after it,
each in markers of its own, and the notes are cut off what comes back before
anything is written. Tried on Google Translate before it was built: Russian
came back "Я устала, но я готова" and "уже ушла домой?", Spanish "Estoy
cansada, pero estoy lista" and "mi mejor amiga", where every one of them was
masculine without. It is not perfect - a line of two sentences can lose the
speaker by the second - but it is right far more often than a guess. A line
whose notes come back out of place is asked for again without them, so a note
never ends up in your translation. Japanese, Korean and Chinese get no notes:
their words do not change this way.

### The editor in your language

**A Language menu.** It lists every language the editor has a translation
for, and starts in Windows' own language when there is one, English when there
is not. **Picking a language switches the editor to it at once** - no restart,
and your pack stays open exactly as it was: unsaved changes, what is selected,
the undo history, and a translation of your pack you are in the middle of
editing. The choice is remembered; "Windows' language" at the top goes back to
following Windows.

**Spanish, Portuguese (Brazil), Chinese (Simplified), German, French,
Russian, Japanese and Korean come with the editor.** All eight were translated
by a machine and nobody has checked them yet, which the Language menu says
beside each one. Corrections are welcome and easy: fix the file in any text
editor and pass it on, or send it in.

**Correct the editor's words right where they are: Language ▸ Edit texts on
screen.** In any language but English, it puts a small pencil beside every
text on screen. Click one to see the English and write your own words - a text
that says a number shows each form your language uses - and they are on screen
at once, everywhere. A {gap} the sentence needs is checked, so a correction
cannot leave a hole where a pack's name goes. The texts in menus, messages and
tooltips have no pencil; **Language ▸ Find a text to edit** searches every
text of the editor by its words, in English or in your language. Back to
ModForge's words takes a correction out again.

**Your corrections sit on top of the translation that came with the editor,
rather than replacing it.** They are saved in your languages folder, holding
only what you changed, and the rest comes from the editor's own translation -
so updating the editor still brings its new and improved texts. The same is
now true of a whole translation of your own in that folder: its words show
wherever it has some for the English as it is now, and the editor's own
translation fills in the rest, where before one missing line showed in
English.

**Three lists beside a dialogue line are in your language now.** Kind (Text,
Choice, Random) and Jump (Continue, Exit, Jump) showed their English names
whatever language the editor was in, and so did "(none)" beside a line with no
children, in the save window's list of changes, and in an action's details.

**Translations are plain text files anyone can make, fix and pass on.** One
line per text, `key = text`, with the English it was translated from written
above it. Open one in Notepad, Notepad++ or anything else, write your language
after each `=`, and save. Leave a line empty and that one text shows in English
until somebody translates it. Put a file you were given in your languages
folder (Language ▸ Open my languages folder), named for its language - `es.txt`,
`pt-BR.txt`, `zh-Hans.txt` - and it appears in the menu. A translation of your
own wins over one that came with the editor, and updating the editor never
touches your folder.

- **Language ▸ New or update a translation…** writes a file to start from,
  every text in it, in English, ready to be written over. Run it again after an
  update and it brings your file up to date without losing a word of yours:
  new texts arrive in English, and a text whose English changed is marked
  `# changed from:` with the old English, so you can find what to look at. The
  file it replaces is kept beside it as `es.txt.bak`, so a merge that does
  something you did not expect is one rename away from undone.
- **Language ▸ Check a translation…** reads a file and says, line by line,
  what is missing, what is broken and what is out of date: a `{name}` gap or a
  `<b>` tag lost in translation, a count missing one of the forms the
  language needs, a line the editor cannot read. A Replace All that went too
  far and changed keys as well as words is spotted and can be put right with
  one button; the file as it was is kept beside it.

Plurals are the language's own: "1 issue, 2 issues" in English, one form in
Japanese, three in Russian, and the file says which forms each text needs.

One file translates all of ModForge: the editor, and under `[In the game]`, the
few words the game plugin shows players. Put the same file in the game's
`BepInEx\plugins\SMSModForge\Languages` folder and players see those in it too.

### Your pack in your players' language

**File ▸ Pack translations ▸ New or update a translation…** writes a file into
the pack's new `translations` folder - `translations/es.txt` for Spanish - with
every text a player reads in it: dialogue lines and choices, character names,
quest titles, descriptions and tasks, navigator and world map buttons, and the
texts on your screens. Each has a note saying where it appears and who says it,
and the default text above it. Translate it by hand, or give the whole file
to an AI tool and ask it to translate only the text after each `=`; the notes
at the top of the file say how. Run it again after changing the pack and it
brings the file up to date without losing anything already translated.

**Edit your pack in any of its languages.** The **Editing in** list, at the
right end of the row of tabs, switches every text a player reads - lines,
names, quest and task wording, button labels, the text on your screens - to
one of the pack's translations,
everywhere at once. Type over a line and you are correcting the translation;
the default text is untouched. A bar across the window says so for as long
as a translation is up, because typing into the wrong language is easy to do
and slow to notice.

Everything the game finds things by - keys, the names of objects, the targets
of buttons - is shared by every language and never changes when you switch.
Anything you change that is not a text is changed for every language.

**A line you add while looking at a translation is that translation's.** Add a
line while looking at the Spanish and the line is added to the pack, for every
language - but its words are the Spanish you typed, and its default text
stays empty. The same goes for a text that was empty, such as a task with
no journal paragraph. Your pack's check lists each one as an error until you
write its default text, because until then a player of any
other language reads it in Spanish. Nobody reads a blank line in the meantime:
the game shows the player's language if it has the line, otherwise the first
translation that does.

**Tell ModForge what language your pack is written in.** The ModForge tab has a
new **Written in** list under the pack's version. It is English unless you say
otherwise, and an English pack's file does not change. A pack written in
another language gets: its default text for players reading that language even
when it carries a close translation (a Brazilian pack is read in Brazilian
Portuguese by a Brazilian, not in its Portugal Portuguese file); the right
language in the menu's "not in your language" note; and the machine translator
told what it is translating from. Packs that do not say are sent to the machine
as "work it out", so an older pack written in another language is not
translated as though it were English. A grey line under the list says what it
is for, and the First steps tutorial now has a step about it, beside exporting
and publishing.

**The pack's check notices a line in the wrong language.** A line of your
pack's default text that is plainly in another language - Spanish typed into an
English pack while you meant to type it into the Spanish translation - gets a
warning that double-clicks straight to it. Another alphabet (Japanese, Korean,
Chinese, Cyrillic and others) is always noticed. Between English, Spanish and
Portuguese only a line that is plainly the other language is, and short lines
are left alone: a warning on a correct line is worse than a missed one. It was
run over the game's own 19,000 lines of English and over ModForge's own
translations without a single false alarm. When most of a pack reads as
another language, you get one warning suggesting you change **Written in**,
rather than one per line. A character who speaks another language on purpose
can be silenced line by line like any other warning.

**The editor says when a line is too long for the game's dialogue box.** The
game shrinks a long line, down to a limit, so that it fits; a line longer still
runs out of the box and over whatever is below it. A short note under a line's
Text box now says so as you type - the size it is shrunk to, in grey, or in red
when it runs out of the box - for whichever language you are editing, since a
translation is often longer than the line it came from. The pack's check warns
about every line that runs out of the box, in the default text and in each
translation. It is measured with the game's own font and box, so it is a close
prediction rather than the game itself: a line right at the edge is worth a
look in game. Lines in Chinese, Japanese and Korean are drawn by a Windows
font the editor cannot measure the same way, so they are not checked.

A translation is saved like everything else. Switching away from a language
keeps what you changed in it for the next save, so a switch never loses a word
and never writes one either; the list of changes shown before a save lists
each translation's changes under its language - the line, what it said and
what it says now - and closing the pack without saving asks first, as it does
for any other change. Undo works across both: it takes back a correction to
the Spanish and a change to the pack alike, and it puts you back in the
language you were in when you made it.

**Fixed: the list of changes before a save said nothing about a translation you
had edited.** Switching back to the default text wrote the translation there
and then, so by the time you saved there was nothing left to list - and a save
made while the translation was still up listed only the pack's own changes.

**File ▸ Pack translations ▸ Translate the pack…** translates everything a
player reads into the languages you tick, by machine, and writes it into the
pack. The window says what it is about to do before it does it: your pack's
text goes to Google's free translation service, which is not an official one -
it can refuse, it can block your connection for a while if asked too often, and
it can change without warning. Nothing is ever sent while somebody is playing;
the translation is written into your pack and stays there.

Some things it takes care of on your behalf:

- **A line you translated yourself is never touched.** What is still to do is
  read from the file, so anything that already says something is left exactly
  as you wrote it. The same goes for a line you correct afterwards.
- **A run that stops keeps everything it got.** Rate limits are expected rather
  than exceptional, so results are written as they arrive. Run it again and it
  carries on from where it stopped, doing only what is left.
- **A line that came back damaged is not written.** Machine translators drop a
  {gap}, reorder a tag or put a space inside a [PV:token]; those are taken out
  of the text before it is sent and put back afterwards, and a line that did not
  survive the trip keeps your own words and is counted in the report.
- **A line you have since changed is translated again**, rather than being left
  as a translation of something your pack no longer says.
- **A line that reads the same in another language stays done.** A name the
  machine gives back unchanged is marked `# same in this language` in the file,
  so it is not offered again, and you can mark a line that way yourself. A line
  with nothing in it to translate ("...", "?!") is not counted as waiting
  either, except in Chinese and Japanese, which write those their own way.

**Character names are kept out of the machine translation.** A translator
reads a name as the word it looks like - a character called Hope comes back as
Hoffnung in German, in every line that mentions her. So unless you tick
**Translate character names too** in the Translate window (it starts unticked,
and is remembered per pack), names are never sent:

- Each character's name stays exactly as you wrote it, in the name itself and
  wherever a line mentions it with a capital letter - "Hope" and "Hope's", but
  not "hope". Your player, "You", is not a name and is translated as usual.
- The game's own characters' names are kept too when your lines mention them,
  whether or not the box is ticked - by their names only: "Anna", "Nina" of
  Nurse Nina, "Frost" of Doctor Frost. The descriptions the game files some
  characters under ("Technician", "Park Woman", "Mobster 1") are words, and are
  translated. ModForge knows how the game's names are written in Russian,
  Japanese, Korean and Chinese and uses that - "Anna" is Анна, アンナ, 안나,
  安娜 - so they never appear in the list of names to check. Every one of them
  has a spelling in all four; where the game does not settle it, the commonest
  form is used - a Japanese name in Chinese in the characters it is most often
  written with (Chihiro 千寻, Sakura 樱), Master Zhen as 甄.
- In a line, each name is taken out before the line is sent and put back
  afterwards, the same way a {gap} or a tag is. A line the translator hands back
  without one of its names, or with one twice, is not written - so a name can
  only ever be where the translator put it.
- Languages written in another alphabet - Russian, Japanese, Korean, Chinese -
  need each name spelled in it. The Translate window lists the names for them;
  type how each is written, or press Translate and Google suggests spellings
  first, shown in italics, for you to check before anything is translated,
  because a suggestion can still come back as a word. A name left empty stays
  in Latin letters. The window opens wide enough for every column of that list,
  always shows every language in full, and gives any extra height to the list
  of names. Chinese suggestions used to come back empty, because Google's
  Chinese answer carried one percent sign too many beside the place the name
  goes, and a Chinese or Japanese answer could be punctuated differently from
  one sentence to the next ("！" in one, "!" in the other). Both are read now.
  And a name Google's free service leaves in Latin letters - in Chinese it
  left every one - is asked of the Google Translate website before the column
  is left empty; the website spells nearly all of them. The website is also
  asked when the free service refuses outright ("too many requests") with no
  check for you to answer - before, the names step simply stopped there. When
  a language still gets no suggestion, the window shows what Google actually
  answered, so you can see why.
- Google's suggestions, and any spelling you type, are kept when you close the
  Translate window. Before, closing it before pressing Translate a second time
  threw the suggestions away, and the next press asked Google all over again -
  more traffic towards a block. A name Google was asked about once is not asked
  again; one it could not spell stays in Latin letters unless you type it.

Two things to know. A name is put back in its base form: in Russian it does not
change its ending with its place in the sentence, and a word that agrees with
the character - a Russian verb, a French or Spanish adjective - may come out
masculine, because the translator never saw who it was about. And names already
translated by an earlier run stay as they are: correct those with **Editing in**.

**Export and Publish offer to translate first.** When some of what players
read is not yet in every language ModForge has, exporting or publishing asks
whether to translate it by machine before the pack is written. Only lines that
are still untranslated are sent: nothing you or the machine already translated
is touched. Yes translates in the same window as above, where you can watch it
or stop it, and the export goes ahead when you close it, with whatever it got.
No exports the pack as it is. Options ▸ Offer to translate before exporting
turns the question off for the pack you have open.

**When Google's free service stops working, the Google Translate website takes
over.** Before sending any of your pack, ModForge translates a test sentence.
If that comes back wrong - because the service changed, as it did in September -
ModForge now tries the Google Translate website instead, in a small window you
can watch, and uses it if the same test passes there. The report says which
one did the work. The page in that window is locked while ModForge works it, so
a stray click cannot land in the text being translated; it unlocks the moment
Google asks you something there. Chinese through the website works too: the
page writes the markers ModForge puts in each line with full-width percent
signs (％), which lost every line of a Chinese batch and every Chinese spelling
of a name; they are now read either way. The window needs Microsoft Edge
WebView2, which comes with Windows 11.

**When Google stops to check that a person is asking, the check appears in the
Translate window.** Google's free service can decide your connection is sending
automated queries and refuse it until somebody answers a captcha. Until now that
simply ended the run, with no captcha anywhere to answer, and waiting was the
only way out. Now its check is shown over the list of languages - at the start
of a run, in the middle of one, and when asking for the spellings of names.
Answer it and the translation carries on by itself; ModForge never answers it
for you, and Stop ends the run with everything so far kept. If the free service
still refuses once you have passed it, the Google Translate website carries on
instead, in the same browser, which knows you passed. Refused again straight
after a check, the run stops rather than asking you over and over.

Often the free service's refusal has no check on it at all - only "try again
later". ModForge sees that within a few seconds, says so, and carries on
through the Google Translate website instead, which does show a check you can
answer when it wants one; the languages after it go on there too.

**File ▸ Pack translations ▸ Check the pack's translations** reads every file
in the folder and lists what is missing, what lost a `{gap}`, a tag or a
`[PV:...]` token on the way, and what the pack has changed since it was
translated. Keys a Replace All damaged can be put back with one button.

The folder is exported with the pack. **Players get the pack in their language
by themselves** when it has a translation for it; nobody picks anything.

**A translation never shows a line the pack no longer says.** Each translated
line remembers the words it was translated from. Change a line in the pack
and players see your new words, in the pack's language, until the translation
is brought up to date - not an old translation of something else.

Sound cues that listen for words in a line still hear its default text,
whatever language the player reads the line in.

**Translate the game's own lines in the conversations you extend.** When your
pack adds to or changes one of the game's conversations, the game's lines you
left alone are now in your translation files too, under a heading of their
own, and you can write their translation while a translation is up, the same
as your own lines. The machine translates them too. Your pack's default text
never changes: those lines stay the game's own words. Rewrite one of them in
your pack and its old translation is marked out of date. Players see these
translations only if they choose to (see below).

**Fixed: typing over one of the game's lines while a translation was up
rewrote the line for everyone.** The Spanish went into your pack as your
version of the game's line, and every player saw it, whatever language they
played in. It now goes into the translation.

### In the game

**{PC} and the game's other words in braces work everywhere, not just in
dialogue.** The player's name, what they call their family, and every other
word the game fills in inside a line of dialogue are now filled in wherever
your pack's words are shown: quest titles, descriptions and tasks, speakers'
names, navigator and map button labels, and the text on your screens. They
are filled in the way the game fills them in a line - from the same list, bold
or coloured where the game makes them so - and they follow the save that is
loaded. `[PV:name]` works in all of those places too, and keeps up as the
variable changes. Before, a quest called "{PC}'s first day" showed the braces.

**Fixed: `[PV:name]` in a line you add to one of the game's conversations
showed as typed.** Lines in your own conversations had their variables filled
in; a line added to, or rewritten in, one of the game's did not. Both do now.

**Renaming a variable renames it in every text.** A rename rewrote `[PV:name]`
in dialogue lines and button labels only, so the same token in a character's
name, a quest's text or one of your screens went on naming a variable that no
longer existed. It now reaches every text a player reads, and the list of
where a variable is used includes them.

**Switch packs on and off on the main menu.** Each pack in the mod list has a
box beside its name: untick it and the pack is not loaded the next time a game
starts or loads, and its row turns grey. Nothing needs moving out of the Mods
folder any more to play without a pack. The choice is the same for every save,
and the language menu offers only the languages of the packs that are on.

**A pack that is off keeps its progress.** Until now, a save only ever carried
the data of the packs that were running: play an evening without a pack, save,
and that pack's progress was gone from the new save. Now the data of every pack
the loaded save has - switched off, missing or broken - goes into each save you
make from it, unchanged, and is all there again when the pack runs. And a slot
you save over no longer keeps the pack data of the save that was there, which a
pack switched back on would have read as yours.

**The save warning says why a pack is not running.** A save with data for a
pack that is not running lists it under one of three headings: switched off
(one tick away on the main menu), installed but could not be loaded (the log
says why), or not installed - with the version the save was made with, when the
save knows it. The warning used to say only that the pack was not installed,
and that its data would not be kept; now it is kept.

**Each save records what it was made with.** `SMSModForge-save.json`, beside
the game's own files in the save's folder: the ModForge version, the game
build, when it was saved, the language, XUnity.AutoTranslator's language when
it is installed, and every pack the save has data for, with its version and
whether it was running, switched off, not loaded or not installed. It is what
lets the warning above name a missing pack's version, and it is readable in any
text editor when somebody is sorting out a save.

**ModForge and XUnity.AutoTranslator work together.** Most translations of the
game are made with XUnity.AutoTranslator. When it is installed:

- "Computer's language" in the Language list becomes "Same as
  XUnity.AutoTranslator", and follows its language.
- Its language is offered in the list even when neither ModForge nor a pack is
  in it: ModForge's own words stay in English, the game is translated by
  XUnity, and packs that have that language are played in it.
- The language chosen on the menu is the mods' alone. XUnity reads its own
  language once, when the game starts, and cannot change it while the game
  runs, so ModForge does not touch it: the game is in XUnity's language, the
  mods in yours.
- XUnity is told to leave alone what ModForge has already translated - a
  pack's translation, a pack written in another language, ModForge's own words
  in yours - so nothing is translated twice, and a line a pack wrote in German
  is not read as English and translated again.

**A pack's lines type for as long as they take to show.** The game times its
typing sound by the length of the line as written, formatting included, and
reveals only the letters you can see - so on a line with a colour or bold in
it, the sound went on after the text had finished, and a click in that time
only stopped the sound instead of moving on. A pack's lines, and the game's
lines a pack shows in its translation, are now timed by what shows of them;
everything else keeps the game's own timing.

**Packs show the game's own lines in their translations too.** In the
conversations your pack extends, the game's lines are shown in your
translation of them, whenever it has them in the player's language, so a
conversation reads in one language from start to end. Mods exist that
translate the whole game, and a pack's words for the game's lines would mix
with theirs, so the first time it matters the player is told so, once: inside
the warning about their save when there is one, on its own otherwise.

**The pack list on the main menu and the save warning speak the player's
language** where ModForge has a translation for it. The plugin's new
`Language` setting, in `BepInEx\config\treboy.starmakerstory.smsmodforge.packplugin.cfg`,
decides it for ModForge's words and for every pack: `auto` (the first time)
follows the language Windows is set to; `en`, `es`, `ja` and so on pick one.
The game itself stays in English.

**Choose the language on the game's main menu.** Click a flag above the Exit
button, bottom-right; each flag has the language's name beside it, and "Mod
language" over them says they are the mods' language, not the game's. The one
in use stands out in orange. The choice is saved and
ModForge's words switch at once. Packs are played in it from the next game you
start or load. If some packs are less than half translated into the language
you picked, a window lists them with how much of each is translated, and lets
you keep the language or go back to the one you had. To follow Windows' language
again, or to pick a language only one of your packs comes in, set `Language` in
the plugin's settings file.

**No more stop when a line in Chinese, Japanese or Korean first appears.** The
fonts that draw those letters made each one the first time a line showed it -
opening the Windows font file and drawing it inside that frame - so the game
paused for a moment on every line with letters it had not shown yet. Every
letter a pack uses is now made while the pack loads, behind the loading screen,
and the log says how many and how long it took.

**ModForge says so when the game is a different build from the one it is made
for.** The ModForge line at the top of the menu's mod list turns red and reads
"made for the game's 1.8E" when the game says it is another build. Until now
only the packs were checked against the game, so after a game update every pack
went red while ModForge itself - which needs updating first - said nothing.

**A pack that is not in your language says so on the menu.** When ModForge is
being read in something other than the pack's default language, a pack that carries translations
but not one for your language is marked - its default text will show while
everything around them is in yours. A pack with no translations at all is not
marked, because that is simply a pack in its own language and a note under
every pack installed would teach people to stop reading them.

**Letters the game's fonts do not have show in the game.** Its fonts draw
Latin, so the plugin puts Windows' own fonts behind them and only the letters
the game lacks come from Windows'. Two things decide which fonts: the language
ModForge is set to, which brings Segoe UI and with it Cyrillic and Greek; and
the pack's own text, which is read as the player will see it and asks for a
Chinese, Japanese, Korean or Thai font when it finds one of those. A pack with
nothing but European letters in it loads no extra fonts.

### Your scenes

**A scene can count as a Starmaker photo.** Tick "Treat as a Starmaker photo"
on a scene and showing it pays out exactly as taking one of the game's own
photos does: Anna's mood decides whether the shot came out, and a bad one pays
nothing; the photography traits, the camera and lens the player owns, any
adverts running and the skill tree all apply, along with the day's income and
the photo counts. None of that is copied into ModForge - the scene is handed to
the game's own payout - so it keeps working when the game changes any of it.

**...and it appears in the Starmaker gallery.** The photo is added to the end
of the gallery row once the player has seen it, and stays there — remembered in
your pack's own save data, so it survives a reload and does not touch any of
the game's own gallery flags. There is nothing to set up: ticking the box is
all of it.

Off unless you turn it on, and per scene, because most scenes are story art
rather than photographs.

### Your quests

**Choose what happens when you add tasks to a quest players have already
finished.** Until now, a player who had finished one of your quests never saw
tasks you added to it in a later version. The Quests tab now asks, per quest:

- **Leave it finished** - as before, and still the default.
- **Reopen at the new steps** - the next time they load their save, the quest
  is in progress again: every task they did stays done, and the first new one
  starts. Only when the new tasks all come after the ones the quest had, and
  the player had done all of those. Anything else stays finished, so nobody is
  stuck behind a task they cannot reach.
- **Start it over** - their progress is cleared and the quest goes back to not
  started, so it starts again the way it always does.

ModForge remembers which tasks each quest had the last time a save was played
with your pack. For saves from before this version, the tasks a player never
did that come after the last one they did count as new. Only your own quests:
a game quest you add tasks to stays as the player left it.

### Fixes

**Undo and redo on the Places tab are much quicker.** Undoing an object you
had moved, turned or resized in the preview rebuilt the whole editor - every
tab, every place - and then the Places tab's panels, about a second and a half
on a large pack. An undo that changed one place now puts back just that place,
and one that only moved things moves them back where they are on screen, the
way dragging moved them: about a tenth of a second on the same pack. Anything
bigger - a change across several places, or to other tabs - is undone the way
it always was.

**The middle square of the scale gizmo is no longer touchier than the arms.**
Dragging the square that scales both ways changed the size many times faster
than dragging an arm the same distance, because it measured from the centre of
the object, right next to where you grab it. It now scales at the same rate as
the arms: up and to the right to grow, down and to the left to shrink.

**A save tells you about a missing pack once, not every time you load it.** A
save with data from a pack that isn't running - switched off, or not installed
- said so every time it was loaded, so a player who had switched a pack off on
purpose read the same notice every evening. Now each save says it once for each
pack, and remembers that the way it remembers everything else: when the game
autosaves as you sleep. Quit before sleeping and you are told again next time.
A save from the save menu keeps what the last autosave remembered, like the
rest of what it copies. A different pack going missing later is still news, and
is said. If the pack runs with the save again and you sleep with it, the save
forgets it was told, so taking the pack away after that says it once more.
Warnings about packs that change the game's own quests or conversations are
not affected.

**NPCs placed on the game's own levels now appear in packs that have no places
of their own.** A pack that added nothing to the map but put an NPC on one of
the game's levels - Downtown, say, under its NPCs object - had that NPC left out
in the game, with a warning in the log saying the level "has NPC placements ...
but this build path has no NPC context". The game plugin got ready for a pack's
NPCs only while building the pack's own places, and skipped that for a pack
without any. It now does it for every pack. The pack needs no changes; players
need this version of the plugin. The same fix also clears a pack's "show when"
conditions properly each time a save is loaded, for packs with no places.

**An NPC's conditions now decide when it shows.** The conditions you set on an
NPC placement were saved but never used in the game: the NPC showed or not by
its "Start active" box alone. They now work the way a GameObject's do, checked
all the time. This matters most on some of the game's own levels: Downtown
switches off everything under its NPCs object each time the player arrives, so
an NPC you placed there vanished on arrival. Give it a condition - "Always
true" keeps it there - and it comes straight back. An NPC with no conditions is
still left to the level.

**Setting a yes/no variable uses True and False buttons, like checking one.**
The Variable action set a Bool with a single tick labelled "set to true", so
an unticked box could be read as "not set" just as easily as "set to false" -
while the Variable condition for the same variable already asked with a True
and a False button. The action now has the same pair. Nothing about your
actions changes: the value each one sets is exactly what it was.

**The Editing in box no longer goes blank.** Switching the editor's own
language, from the Language menu, left the Editing in box empty, as if the
pack were not being edited in any language at all. It now keeps showing the
language you are editing, renamed into the editor's new language, and only
picking another one from its list changes it.

**Something you add is named in your pack's language, not the editor's.** A
new SFX, character, quest, dialogue, folder or any other record was named in
the language the editor was shown in, so an author reading the editor in
Portuguese got "Novo SFX" in a pack written in English. New records are now
named in the pack's default language. While Editing in a translation, a name
that is translated - a character's name, a quest's title - starts in that
language, since that is what you are typing; one that is the same in every
language, like an SFX's, stays in the default language. A new screen on the
UI tab works the same way: its title and buttons start in the language being
edited rather than the editor's.

**Spelling is checked in the language a line is written in.** The node Text
box and the node list were always spell-checked as US English, so a pack
written in another language, or a line edited in a translation with Editing
in, was underlined from one end to the other. They are now checked in the
pack's own language, or in the language being edited. A language Windows has
no spelling dictionary for is not underlined at all; Windows' language
settings can add one.

**A wallpaper named by a full path now appears.** A wallpaper whose image was
typed or pasted as a full path on your computer - "Z:\Modding\MyPack\Wallpapers\Elf.png"
rather than "Wallpapers/Elf.png" - was skipped in the game, and its button
never appeared: the game plugin looked for that exact path inside the pack. It
now finds the file by the end of the path when the file is in the pack, for
wallpapers and for every other file a pack names, and a wallpaper also tries
its External path when the Sprite path finds nothing. Opening such a pack in
the editor names its wallpapers from the pack's folder instead (the list of
what was brought up to date says so).

**The validation list says when a file won't reach players.** A full path on
your computer was checked against your own disk, where the file is, and passed
- while players, who only get the pack's files, had nothing. It is reported
now, with what to do, and so is a wallpaper with no image at all.

**Every mod's wallpaper buttons stay on the wallpaper screen.** The game's
list has room for twenty buttons and no scrolling, and its own fourteen come
first - so the seventh wallpaper added by mods, from ModForge or any other
mod, was drawn below the panel, where nobody could click it. When there are
more buttons than fit, they are now made smaller until all of them do, and
back to their usual size when there is room again.

**One failing system in the game no longer stops the others.** Everything the
game plugin does each frame - level refreshes, buttons, weather, wallpapers,
dialogues - ran in one piece, and one of them failing ended the rest for that
frame, every frame: a wallpaper's button, shown only from further down, never
appeared. Each now runs on its own, and a failure is written to the log once.

**Sound cues play in lines your pack adds to the game's own conversations.**
A cue like "*slap*" in a line your pack added to one of the game's
conversations, or in a line of the game's whose words you rewrote, never played
- the line had its speaker and actions, and no sound. It plays now, the same
way it does in your pack's own conversations.

**Sound cues play whatever language the line is read in.** They are listened
for in your pack's own words, so a line a player reads translated - "*шлёп*"
where you wrote "*slap*" - plays the same sound. A line that has words only in
a translation, and none in your pack's own language, is listened to in the
words it has; before, its cues were silent.

**A quest step that counts now shows its number when it sits under another
step.** The journal builds the counter under a subtask row - the brackets, the
current number, the separator, the maximum, all bound to the right fields - and
then never switches it on; only a top-level step names that counter in the one
field that enables it. So a counting subtask drew as a plain line while its
number ticked up out of sight, which looks exactly like a quest that is broken.
The counter was hidden three ways over — never switched on, its pieces
switched off, and every one of them painted transparent — and ModForge now
undoes all three, so the game fills the number in as it does for any other
step. It is done only when a pack you have
installed actually counts on a subtask, and the editor no longer warns that
such a count will not be seen, because it will be.

**A pack's Chinese, Japanese and Korean showed as empty boxes.** The fonts
were chosen from the language the PLAYER had set, which says nothing about
what a pack is written in - a Portuguese player opening a Chinese pack got a
font with no Chinese in it. Cyrillic and Greek worked, which made it look like
the mechanism was fine and the art was wrong. The pack's own text is what is
read now, so a Japanese name in an English line brings a Japanese font with it.

**...and then a few Chinese characters still did.** A pack is read as a whole,
so a pack containing any Japanese was read as Japanese and never asked for a
Chinese font at all. That half worked, which is why it took a second look: a
Japanese font has Chinese characters in it, so most of the text drew and only
the simplified-only forms came out as boxes, scattered through lines that were
otherwise perfect. Every writing system found in a pack now gets its own font.

**A translated name lost its colour.** The game colours a speaker's name by
matching the name as it is drawn, so a player reading ModForge in Portuguese
saw "Você" in plain white where "You" is grey — which reads as the character
being set up wrong rather than as anything to do with language. A name that is
translated now carries its colour across with it.

**Every pack called the player by the ModForge language, including packs with
no translation.** A player with ModForge in Portuguese saw "Você" where an
English pack says "You", in the middle of lines that were still in English.
The player's name follows the pack: it is translated for a pack that carries a
translation the player is reading it in, and left as the author wrote it
otherwise.

**A pack on the menu no longer shows two version numbers at once.** A pack
built for another version of the game was listed as "Incompatible game version
(1.7A) - Built for ModForge 1.5.0", which is two numbers and one problem:
rebuilding the pack fixes the game version and restamps the ModForge one with
it. Only the game version is shown when it does not match. The log still says
everything.

**Two tasks of a quest with the same name can be told apart.** The game's own
quests do it - a step and the step under it named the same thing - and the
closed box shows only the name, so which of them a row pointed at was a coin
toss with nothing left behind to find it by. Where a name is shared, the task's
id is shown beside it. Names nothing else shares are left alone.

**A face you give a character now shows everywhere a face is picked or
shown.** Four places each kept their own list, and none quite matched the game:
the picture on the Busts tab showed only the game's four faces, so one you
invented was missing beside it; a line's Expression list only caught up when you
changed the line's speaker; the list an action picks an expression from never
offered a face of your own at all; and the picture beside a line looked for the
expression's name instead of the face it is set to show, so a face named
differently from its expression showed nothing there while it worked in the
game. All four now follow the character's expression list the way the game
does, and pick up a face the moment you add it. The picture beside a line also
shows a texture you replaced on one of the game's busts - its base or a face -
instead of the game's own.

**The bust preview's expression list is the faces that bust actually has.** It
was four fixed entries, offered even on a bust with its expressions switched
off or one of the game's that has none, where picking one did nothing. It now
lists what the preview loaded. This also fixes the expressions doing nothing in
any language but English: the list was handing the preview the translated word,
so it looked for "Feliz" among sprites named "Happy" and quietly found none.

**Misspelled words are underlined in the node list's game-look rows too.**
They were only marked in the editing box below the list, because a row drawn
the way the game draws it is the game's own glyph atlas rather than a text box,
and WPF only underlines inside one. The marks are drawn now — but the words
still come from Windows' speller and your own dictionary, so a name you added
there stops being underlined in both places at once. Follows the same Options
switch as the box.

**Undo and redo no longer wait for the pack to be checked.** Putting a change
back re-validated the entire pack before the editor would redraw — about 640ms
on a two-megabyte pack, against 43ms to read the undo snapshot itself. That was
most of the pause, and none of it was anything an author pressing Ctrl+Z was
waiting for. Undo no longer checks the pack at all, which is what every other
edit in the editor already did: the issue list is built when you open, save or
publish a pack, and typing a line or deleting a character has never touched it.
Validate (F5) rebuilds it whenever you want it rebuilt.

**Undo and redo in a long conversation are much quicker.** Every undo draws
the conversation's list again, and every row asked Windows' spell checker about
its words again - about forty milliseconds a line, so a hundred-line
conversation stood still for four seconds after moving one line. A line's
spelling is now remembered until you add a word to the dictionary, and a row
that has not been checked yet shows at once and gets its underlines a moment
later. On a two-megabyte pack, undoing a moved line went from about four
seconds to about one and a half.

**The bust preview can be made bigger or smaller.** A size picker under it
offers ½×, 1×, 1½× and 2×, and the panel it sits in grows with it — the
preview is pinned to its own size, so before this there was no way to give it
more room without giving the backdrop the room instead. 1× and 2× land one of
the preview's pixels on one of the screen's and stay pixel-sharp; the half
steps have to be resampled and look slightly softer. Your choice is remembered.

**+ Root now takes the speaker from the line it follows** — actor, expression
and outfit — and nothing else. A root starts a fresh strand, so it still does
not inherit the conditions, actions, jump and timings that + Child and
+ Sibling carry: those were written about somewhere else. Picking those three
again was all a new strand ever cost.

**Naming an actor who has already spoken in a conversation brings back the
expression and outfit they last wore.** The line was keeping the *previous*
speaker's, whose keys mean nothing to the new one — so every switch of speaker
meant re-picking both, and forgetting to left a line asking for a face that
character does not have. The last look, not the first, so an actor who changed
partway through stays changed.

**Clicking False did nothing, wherever a True/False pair is offered.** On a
Variable condition, on a Set-variable action, and anywhere a row names a Bool
variable, picking the button that was not already picked left the value exactly
as it was. The converter behind the False button inverted on the way in and not
on the way out, so it wrote *true* — and because the outgoing button writes
first and the incoming one second, the wrong answer landed last. Copy and paste
made it easiest to notice, but it was every such pair, every time. Both
directions are now pinned by a test.

**A Bool variable's default value is asked with two buttons** — Starts true /
Starts false — rather than one tick box. An unticked box reads as "not set"
just as easily as "starts false", and a default is the one thing worth stating
outright. Same pair, same words, as the condition and action rows.

**The place preview's tooltip showed � between its hints.** It now reads
"Scroll to zoom (toward the pointer) · middle-drag to pan · double middle-click
to reset".

**Checking a translation read as a contradiction**: "4005 of 4005 texts
translated (100%)" followed by "162 texts are still the same as the English".
They count different things — the first is how many texts the file has a line
for, the second how many of those lines are still word for word the English,
which is right for a name, a shortcut or a type the editor writes out. The
report now says "filled in" for the first and explains the second.

**Language ▸ New or update a translation asked for "the language's code" and
left you to guess.** It now lists the codes with the languages they mean, says
any code Windows knows will do, names the translations already installed —
typing one of those updates it rather than starting over — and says where the
file is written.

## 1.5.0

The game's own quests, yours to change: add and take out their tasks, change
what starts and resets them, and see everything the game does with them. The
game now warns a player whose save might not suit a pack, and the dialogue Text
box gets proper undo and a colour picker.

### Before you update

**Every character of your pack now has the neutral expression, and older packs
gain it when they open.** A character that has none gets `neutral` as its first
expression, with an empty child name, which is how the runtime spells no face
showing. One that already has a neutral row keeps it exactly as it is. The
editor says how many characters it gave one when the pack loads, nothing is
written until you save, and the first save keeps a copy of the original beside
the manifest, as with every migration. The game's own characters are not
touched: the game gives them theirs.

**A line of the game's with every condition taken out now plays without
them.** In earlier versions the game went on checking its own conditions for
such a line, whatever your pack said. If a pack of yours took every condition
off one of the game's lines, that line now does what the pack asked once
players update the plugin.

### Change the game's own quests

**+ Vanilla on the Quests tab.** An entry about one of the game's quests, in a
**Vanilla quests** list of its own under your quests - the same split the
Places and Dialogues tabs make, with each entry showing how much it changes
(tasks added, tasks taken out, other changes). Pick the quest by name, and your
pack can

- **replace the paragraph the journal shows under its title**, with the game's
  own shown beside the box so you can see what you are replacing;
- **change that paragraph as its tasks are done** - every task in the list
  gets a "Quest description once done", and the journal shows the one from the
  task furthest down the list that is finished;
- **run actions when the game completes one of its tasks**, whether its own
  dialogue finished it or a Quest action of yours did;
- **hide any of its tasks** from the journal - always, or (for a subtask) until
  it starts;
- **add tasks and subtasks of your own** anywhere in it: before or after the
  game's, as new top-level steps, or as subtasks of one of the game's steps -
  including a step that had none. They have everything a task of your own quest
  has: text, completion conditions and actions, a counter, "Hidden until it
  starts" and "Quest description once done". Quest actions and conditions name
  them on the Vanilla side, by the game's quest and your task's runtime name;
- **take any of its tasks out** of the quest;
- **start the quest early**, when conditions of yours pass, if the game has not
  started it already.

The Tasks list shows the game's tasks in the game's order with yours among
them, marked "yours". + Task and + Subtask add a task just after the selected
row, ▲ and ▼ move yours past the game's, and − Remove deletes one of yours or
takes one of the game's out (its panel puts it back).

**Adding and taking out tasks can break a save that is already under way, and
so can removing your pack later** - the game waits for every task in its order,
including yours, and a quest part-way through when its list changes can be
left with nothing it can finish. That is your call to make; the editor says so
in orange on any entry that does it, and the game warns the player (see
below). Two things keep it from being worse than it has to be:

- **A task taken out is not deleted.** The game's own scenes still name it, and
  the game's quest code fails on a task that is not there. It stays, hidden,
  and the quest moves past it the moment it starts, as though the game had
  completed it. Under a task that finishes with any one of its subtasks it is
  left alone instead, so it cannot finish that task for the player.
- **A save that lost tasks is put back on its feet.** When a save has progress
  on tasks a game quest no longer has - your pack added them and is gone, or
  now adds different ones - the plugin does, once, what the game would have
  done with the quest as it is now: completes a step whose subtasks are all
  done, starts the next step of an in-order list that has nothing in progress,
  and completes the quest when all of its steps are. Only for those quests.

Everything your pack changes on the game's quests goes back to the game's own
whenever a save is loaded, and only the packs installed then put theirs on
again - so removing a pack and loading a save, without restarting the game,
leaves nothing of it behind.

The game's tasks are named by the id the game files them under rather than by
their text, so a patch that rewrites a line does not detach what you hung on
it. Your added tasks are filed under an id made from your pack, the game's
quest and the task's runtime name, so renaming the entry itself is safe but
renaming one of those tasks after release starts it over for players.

Needs this version of the plugin. An older one does not know what an entry
like this is: it reads it as a quest of your pack's own, with no title and no
tasks, and would put a blank quest in the journal if anything started it.

### See what the game does with its own quests

**What the game does with it**, on an entry about one of the game's quests and
on each of its tasks: every place the game starts the quest or puts it back to
not started, and every place it completes, counts, fails or abandons a task.
Each one says

- **where it is**: which conversation, and which line - as the line appears, or
  once it is done - with the lines the player passes through to get there; or
  which script on which object (a room, a trigger, a button), what sets it
  off, whether that object starts switched off, and which conversations the
  script plays just before and just after it;
- **what has to be true for it to happen**: the conditions on the way there,
  and what the conversation needs before it plays, as read-only condition rows
  like the ones a changed conversation already shows. A condition ModForge has
  no equivalent for is still listed, in the game's own words, so nothing looks
  less guarded than it is;
- for a counter, how much it adds.

**Open conversation** takes you to that conversation on the Dialogues tab, with
the line selected. If your pack does not change that conversation yet, you are
asked before it is added to your vanilla conversations; it changes nothing
until you edit it.

**Every task now says when it starts**, the game's and yours alike. Nothing in
the game starts a task directly - it follows the quest's order - so the panel
works it out from where the task sits: when the quest starts, once the task
before it is done, when its own task starts, or never, under a task completed
by an action. A task your pack takes out is counted as skipped on the way.

This covers everything the game does to its quests: all 180 of its quest steps,
in its conversations and in every script of the scene they are all in.

**"Old Friends (Charlotte)" is now in the list of the game's quests.** It was
missing because the list only counted quests started from a line of a
conversation, and this one is started by the script that plays the
conversation.

### Change what starts and resets the game's quests

The places under **Started by** and **Put back to not started by** can now be
changed. Each of the game's conditions around the step is listed where it
lives - a line on the way, the line itself, the place that plays the
conversation, or the script - and

- **Take out** removes one of them from the game. It stays on screen, struck
  through, with **Put back** beside it. A condition that belongs to a line is
  taken out of your version of that conversation; one that belongs to a
  place is taken out of that place's script. Either way, whatever else it
  guards happens without it too - the line is offered, or the room plays the
  conversation - because that is where the game keeps it. The Dialogues tab
  shows the same change.
- **And only if (yours)** adds conditions of your own.
- **Where it is** is a row too, and it can be taken out: the game's script
  sits on one room, so the quest only ever starts there until you say
  otherwise.

**Once you change anything at a place, that place is yours, and what is left of
it starts the quest by itself.** The game's conditions you kept, the room
(unless you took it out) and your own are asked over and over; the moment they
all pass, the quest starts - whether or not the conversation ever plays or the
script ever runs. So "take out everything, add Insert" means: press Insert and
the quest starts. Take the room out as well and it starts anywhere. A place you
have not touched is untouched: the game reaches it and starts the quest, as
always. Putting everything back gives the place back to the game.

The same for **Put back to not started by**, with the quest going back to not
started. A quest never starts on a frame where something of yours would put it
back.

A changed place is marked with a dot, and its reset arrow puts it back the way
the game has it.

**What the game tries first is shown, and stops mattering once a place is
yours.** A room plays the first of its conversations that can play, so the ones
before yours come first - at the bar, Claudia's, Zuri's, Kate's and Toni's come
before Anna and Liz's first one. Every place says which those are ("The game
tries these first: ClaudiaBar, ZuriBar, ..."), on the Quests tab and on the
Dialogues tab, and it is worth knowing for the conversation itself; your rule
for the place does not wait for any of them. The game's log also says, each time
a room picks a conversation from a list you changed, which one it played.

A place in a room says which room in its heading ("... plays from
8_Room_Talk/Bar (Conditions), in Bar"). The room is not listed as a condition:
it isn't one of the game's - the script runs there because that is where it
lives - so there is nothing to take out or change. To have a quest start
somewhere else as well, use a LevelActive condition in your own Starts when.
The Dialogues tab still shows the room as the first row of "The game plays this
when", headed as where the game's script is. The game's conditions are no
longer headed "Required - this dialogue only starts in this level", which
belongs only to the level row of a dialogue of your own (still required, still
editable).

**Resets when**, beside Starts when, on your quests and on the game's: when all
of its conditions pass, a quest that has started - in progress, completed or
failed - goes back to not started, with its tasks and counts, so it can be done
again. While they pass, the start conditions wait, so a quest can't start and
reset in turn.

**What an entry about one of the game's quests changes is now shown, and each
change can be undone,** the way a changed conversation's lines are: a
**Changes** line with **Reset all** (which puts everything above back,
including the conditions taken out), a **Reset** beside the description, and a
dot on every task of the game's that you change, whose panel lists each change
with its own button and a **Reset** for the whole task.

**On the Dialogues tab, "The game plays this when" can be changed too**: one
group per place that plays the conversation, each of the game's conditions
with Take out and Put back, and the same change shown on the Quests tab. The
conversation's Reset all puts them back as well.

Taking a condition out of the game's conversations or scripts is a change to
when a conversation happens, so a player loading a save already under way is
warned about it, as for a changed line. A place that only gains conditions of
yours is not warned about: it can only make the quest start somewhere the game
would not have started it.

If a game update moves a condition you took out, the game checks it as before
and the Issues list says so, rather than something else being taken out in
its place.

Needs this version of the plugin. An older one does not take conditions out of
the game's scripts, decide a place for itself, or reset a quest by its Resets
when list. **Packs written during the previous test builds need one save**: what
a changed place comes to is worked out when the pack is saved, and the plugin
says so in the log if it finds a place without it.

**Fixed: a line of the game's with every condition taken out still checked
them all.** Taking the last condition off one of the game's lines, in your
version of a conversation, reached the game as "nothing said about this line's
conditions", so the game went on checking its own. Now an empty list means what
it says. A pack made with an earlier version that did this gets the change it
asked for once players update the plugin: the line is offered without those
conditions.

**Fixed: "This entry changes nothing about ..." on an entry that does.** An
entry about one of the game's quests whose only changes were to what starts or
resets it - Resets when, your conditions at one of its places, or the game's
conditions taken out there - was listed in Issues as doing nothing.

**Fixed: a conversation line's conditions and actions went stale after a
reset.** Putting a line's conditions or actions back the way the game has them
left the panel showing the old list, and editing one of those rows changed
nothing that was saved.

### The game warns before a save meets a pack it might not suit

When a player loads a save, a window now stops them as soon as it has loaded -
before your pack's rules and quests move - when either of these is true, and
says both when both are:

- **A pack changes the game's own quests or conversations in ways this save has
  not been played with.** That is: tasks added to or taken out of one of the
  game's quests; lines of one of the game's conversations changed, added or
  taken out, or one of the game's conditions taken out of what plays one. They
  are told what the pack actually changed, in plain words: **the game's own
  quests** when it adds or takes out tasks; **when the game's own quests
  start** when it takes conditions out where one of them starts (on the
  Quests tab); **when the game's own conversations play** when it takes
  conditions out anywhere else; **the game's own conversations** only when it
  rewrites lines. A pack that only changed a quest is no longer said to change
  conversations. They are told it as a list when there are several packs, that
  progress could break - now, or if the pack is removed later - and that a new
  game is safest. **The game's quests the pack changes are listed** - tasks
  changed, or where they start taken over - **each with where this save is in
  it**: not started yet; in progress
  and should be fine; in progress with a new task added before where the
  player is, which can leave it stuck (the game only completes a task once
  every task before it is done); or already finished, so the changes won't be
  seen. It is kept short on purpose: the warning itself is one sentence, and the lists - what each pack changes, and which quests - sit in a box of their own under it, in smaller text, which scrolls (with a scrollbar and the mouse wheel) once there is more than fits, so the window never outgrows the screen however many packs a player has. **Saves from before this version** carry no
  record of what they were played with. For the one change a pack could already
  make then - rewritten conversations - such a save is told ModForge can't tell
  whether it has been played with it, rather than that it hasn't; everything
  else is new in this version, so an older save is told plainly. Once they continue, the save the
  game writes when they next sleep remembers it and is not asked again (a save
  made before sleeping is a copy of the one they loaded, so it still asks). A
  pack update that starts changing something new asks again, about that. New
  games are never asked: they start with the changes in place. Changes to the
  game's places and screens, and to its characters' art, voice or name colour,
  are not asked about.
- **The save has data from a pack that is not installed.** The pack is named,
  and they are told its data won't be kept in the saves they make from then
  on - which was already true, and nothing said it.

**Continue** goes on with the save, and the window folds away the way the
game's own panels close. **Return to Main Menu** leaves the save for the title
screen the way the game itself goes back to it after an ending, with the same
reset of the game's state. Both buttons make the game's own button click. Only the packs that are installed
have their data read, as before.

The Quests and Dialogues tabs say the same thing in orange where you make one
of those changes, so it is not a surprise when a player reports the window.

### Hide a task until your conditions pass

**Hidden until conditions pass**, on any task of yours and - as a choice under
"In the journal" - on any of the game's tasks in a quest you extend: the task
stays out of the journal until conditions of yours all pass. Once they have, it
stays in the journal for the rest of that save; tick **Hide it again when they
stop passing** to show it only while they pass instead. It uses the same
condition list as everything else, and changes nothing about when the task or
its quest finishes. Neither the game nor ModForge could do this before: the
game's journal only knows a task as hidden or not.

Needs this version of the plugin. An older one ignores it and shows the task.

### A quest's journal entry keeps up with the player

**Quest description once done**, on every task: what the quest's description
becomes in the journal once that task is done. Several tasks can have one; the
journal shows the one from the task furthest down the list that is done, and
the quest's own description before any of them is. It is worked out from where
the player is each time rather than remembered, so it cannot come back wrong
after a load.

**Hidden until it starts**, on a subtask: keeps it out of the journal until it
starts. The game's journal lists every subtask of a task it shows, including
the ones that have not started yet, so a quest with steps in order gave the
later steps away. Under a task whose subtasks run in order, a hidden one appears
once the subtask before it is done. A top-level task has no tick because it
does not need one: the game's journal already leaves out a top-level task until
it starts.

Both need this version of the plugin. An older one ignores them.

### Undo in the dialogue Text box

**Ctrl+Z and Ctrl+Y step through a line one change at a time.** Using a
formatting button broke both: every other Ctrl+Z appeared to do nothing, and
anything undone could not be redone. The box redraws the line whenever its
formatting changes, and each redraw was being recorded as an edit of its own.
Now a run of typing is one step, each formatting button is one step, and redo
works until you make a new edit. With nothing of the line's left to undo,
Ctrl+Z goes on to the editor's own undo, as it does from any other field.

### Text colours match the game

**The Text box and the game-look row now agree about colours, and both agree
with the game.** They read `<color=…>` differently: the formatting button's own
`#f66` was red in the Text box and white on the row. Neither was right about
everything. Read off the game's own text code, the game accepts:

- `#RGB`, `#RGBA`, `#RRGGBB` and `#RRGGBBAA`. The short forms do work in game:
  `#f66` is `#ff6666`.
- Ten names, in any case: red, lightblue, blue, grey, black, green, white,
  orange, purple, yellow.

The row only understood six and eight digits, so it showed short codes and
names as white. The Text box used Windows' colour reader, which knows about a
hundred and forty names the game prints as nothing, and reads four hex digits
as ARGB where the game reads RGBA. Both now read exactly what the game reads,
and anything else keeps the ordinary colour in both.

### A colour picker on the Colour button

**The Colour button and Ctrl+Shift+C open the colour picker** and wrap the
selected words in the colour you choose, written as six hex digits (eight if
you give it transparency). It opens on the colour you used last, so a run of
lines in one colour is one pick each. Cancelling writes nothing.

### The token legend is spelled out again

The panel under the dialogue list no longer lists the formatting tags: the
buttons over the Text box write those. The room went back to the tokens, one
per line, each with what it stands for written beside it ("what they call
Anna") instead of a single word with the meaning on a hover.

### The Actor picker is grouped

**The Actor field on a dialogue node, and on the LeaveBust action, lists
speakers under "This pack" and "The game's own"**, the way the music pickers
do. The pack's heading holds the characters your pack made; the game's holds
its cast, the player, and the speakers of the game's conversation you have
open.

**A character you add is offered there straight away**, and so is a new
runtime name. The list was only rebuilt when the whole pack was loaded into the
editor again - opening it, or an undo - so a character just added could not be
picked as a speaker.

### The Default outfit follows a rename

**Renaming a character's default outfit renames the default with it, at
once.** The Default outfit box went on naming the old outfit until you selected
something else, and until then it named an outfit that no longer existed.
Clearing the name to retype it does not hand the default to another outfit
along the way.

**Typing another outfit's name no longer takes the default with it.** An
outfit whose name starts like the default's - "AnnaDaySwim" beside "AnnaDay" -
passed through the default's exact name while it was being typed, and the next
keystroke was taken for the default being renamed: the Default outfit box
switched to the outfit being typed. The default now stays with the outfit that
has it, whatever the others are called along the way.

### Long dropdowns with headings scroll normally

**The InputKey condition's Key list, and the music track lists, now scroll a
few lines at a time.** They jumped a whole heading's worth per turn of the
wheel, and the letters section is taller than the list, so most letters could
not be reached by scrolling.

### Mask editor

**Left and Right move a mask editor slider by one of the units it shows**: one
pixel of brush size, or one percent of hardness or opacity. Hold Shift for
five. They moved by five percent on the two percentage sliders, and Shift did
nothing.

**Hold the middle mouse button (the wheel) and drag anywhere on the canvas
area to pan.** It only worked with the pointer over the mask itself, so on the
dark area around it - most of the window when zoomed out or panned away -
nothing happened. The help list now says "Middle-drag" rather than
"wheel-drag".

**"Copy layers from" is readable on the light themes.** It was drawn in a
fixed light grey, which vanished into the light panels; it now uses the
theme's text colour like the labels around it.

### The "= default" outfit buttons show up

**The "= default" buttons beside an outfit's Mask, Blink, Mouth prefix and
Expr. prefix now appear on every outfit that is not the default.** 1.4.0 added
them, and on many packs they never appeared on any outfit of any character.

A character's default outfit is saved under the outfit's GameObject name, which
is what the Default outfit dropdown lists and what the game finds the bust by.
The buttons looked it up by the outfit's key instead. An outfit added in the
editor gets a key equal to its name, so a pack built entirely in the editor had
the buttons; a pack whose outfit keys are not their names had none. They now
find the default outfit the same way picking the character does.

Also:

- A character with no default outfit set counts its first outfit as the
  default, the way the game does, so its other outfits have the buttons too.
- Renaming the default outfit keeps the buttons on the others.
- A bust you add to one of the game's characters does not offer them. Its
  default is the game's own bust, which has no paths, and copying from it would
  only empty the field.

## 1.4.0

Quests of your own, in the game's journal, that run themselves from a tab of
their own. And dialogue lines that show their formatting while you write them.

### Quests

**A Quests tab, and quests of your own in the game's journal.** Give a quest a
title, a description and its tasks, and it is listed beside the game's quests,
with the same popups when it starts and when it is finished. The game saves
the player's progress on it with the rest of their save, and completing one
gives the same reward the game's own quests give.

**A quest runs itself from that tab.** It starts when its start conditions
pass, and each task completes when its completion conditions pass, running its
completion actions as it does. They are the same conditions and actions as
everywhere else in the editor, so no dialogue or rule is needed to move a quest
along. A quest with no start conditions waits to be started by an action, so
one you have only begun writing never turns up in a player's journal.

Tasks work the way the game's do. The top-level ones run in order: starting the
quest starts the first, and finishing the last completes it. A task can have
subtasks, finished in order, in any order, or when any one is done, and a task
can count up to a target and complete itself when it gets there. The count is
set by Quest actions, or follows a number variable, the pack's or the game's,
picked the way a Variable condition picks one. Only a task without subtasks has
completion conditions and actions; one with subtasks finishes through them.

**The Quest action** lets a dialogue, a rule or a button take part too: start a
quest, complete a task, set or add to a counter, fail a task, mark it in the
journal, or reset it. A task's completion actions run however it was finished,
including by this. **QuestState and QuestCounter** ask where a player has got
to, so a conversation can wait until a quest is in progress. All three can point
at the game's own quests as well as yours.

The game only completes a task that is in progress, and it does so quietly: a
task completed before the player reaches it simply stays unfinished. So
Validate now says when nothing starts a quest, when nothing can ever finish one
of its tasks, when a task with subtasks still carries conditions it no longer
uses, and when a counter action points at a task that does not count. A quest
that can never be fully completed gets a warning of its own on the ModForge
tab, naming the task it stops at, so a quest that is stuck is visible without
reading every task's warning to work that out.
In game, a refused action says why in the log.

Three things to know:

- **Keep a quest's runtime name once players have it.** Their progress is saved
  under it, and under each task's. Renaming either after release starts that
  quest over for everyone. Rename freely while writing; every action and
  condition naming it follows.
- **The journal never ticks a subtask**, finished or not, and only shows a count
  beside a top-level task. That is how the game draws its own quests too.
- **Players need this version of the plugin.** An older plugin does not know
  quests: the quests are not added, quest actions do nothing, and quest
  conditions never pass.

### Searching a dropdown finds what it shows

**Every dropdown you can type into can now be searched, including the ones that
appear later.** Typing narrows a dropdown to the entries containing what you
typed, but that only ever worked on dropdowns already showing when their tab
opened. A dropdown on a row that appears once you choose something - an
action switched to Set Active, a condition switched to QuestState, a quest
counter set to follow a variable - never got the search, and simply listed
everything. It does now.

Typing in a dropdown whose entries show a friendly name over a runtime one -
places, screens, levels, the game's quests - only ever searched the runtime
one, so a word read straight off the list found nothing and the search looked
broken. It now searches both.

The quest task lists can be typed into and searched as well. The box shows the
task's name, never the id a game task is stored by, and leaving a search
without picking puts the chosen task back.

**The first letter you type is kept.** Since the search arrived, the first
letter typed into a dropdown vanished as soon as the second one went in, so it
had to be typed twice. The search opens the list after the first letter, and
opening the list selected everything in the box, which the next letter then
replaced. What you typed now stays where it is.

### A pack with no dialogues runs its rules

A pack with no dialogues of its own never ran its integration rules or its
places' enter and exit actions, never showed its wallpapers, and never switched
its condition-gated objects on or off: the plugin only started all of that
alongside a pack's conversations. It runs now whether the pack has dialogues or
not.

### Dialogue text shows its own formatting

**A line is drawn the way the player will read it, while you write it.** Markup
is set apart in a colour of its own, and the words it wraps turn bold, italic,
coloured or resized in the box itself. Tags stack, so <b><i>both</i></b> shows
as both.

Before this a line was flat, which made `<b>` and `<bold>` look identical — one
of which the game acts on, and the other of which it prints at the player, four
characters and all. The only way to tell them apart was to build the pack and
read the line in game.

**The node list shows the same thing, with the tags gone entirely.**
That row is for scanning what a conversation says, and in a column that narrow
four characters of `<b>` cost real words — so the formatting is applied and the
markup simply is not drawn. The line itself is untouched: the row is a display,
and what it shows being shorter than what the pack stores is exactly why.

A tag the editor does not know is deliberately left looking like words, in both
places, because that is what the player will get. Same for a `<color>` with
nothing after it, and for a colour value nothing can make sense of: the text
keeps its ordinary colour rather than being painted a guess.

Spell checking still works in both, and a `<size=40>` is now measured against
the size the game really draws dialogue at rather than a guess.

### Node rows can look like the game

**Options ▸ Dialogue rows look like the game.** Off by default; turn it on and
every line in the node list is drawn the way the game draws it — the game’s own
font, its black outline and drop shadow, the speaker’s name at the head of the
line in their own colour, on a dark panel. The same in every editor theme,
because the game does not have a light mode.

Not a styled approximation: the editor already ships the game’s TextMeshPro
atlases for the UI tab, so this is the game’s actual glyphs with its actual
metrics and kerning.

Two things to know before turning it on:

- **No spelling squiggles on those rows.** Windows underlines misspellings in a
  text box, and this is a picture of text. They are still in the Text box below
  the list, which is where you fix them.
- **A line does not break where the game breaks it.** Each line is capped at
  28px so a conversation still fits on screen, and sizing to that height means
  giving up the game’s size-to-width ratio. A line too long for the column wraps
  where the column runs out, and the row takes a second line to show the rest of
  it — so a long line is a taller row rather than half a sentence.

### Reading a dialogue row

- **Long lines wrap instead of being cut off.** A line too long for the node
  list used to stop at the column edge, and the only way to read the rest was to
  click it. It now takes a second line, and the row grows to hold it.
- **Rows have a margin before the text**, whatever comes first — the speaker, the
  line, or the “(no text)” an empty node shows. Text hard against the panel edge
  reads as clipped even when it is whole.
- **Formatting tags are legible on every theme.** They were one flat grey on all
  ten, which is the colour that fails at both ends: washed out on the light
  themes, half-gone on the dark ones. A tag now takes a colour mixed from the
  theme’s own text colour, on a faint chip that shows where it starts and stops.

### Formatting shows on both kinds of row

**The game-look rows now apply the markup too**, the way the plain rows always
did: `<b>` is drawn at the heavier weight the font’s own material carries,
`<i>` leans, `<color=…>` paints, and `<size=…>` really does change how big
the words are — a bigger run makes its line taller, and a smaller one fits more
on the line before it wraps.

One thing to know: the italic lean is the single number in that preview that is
not the game’s own. TextMeshPro keeps its italic angle on the font asset rather
than on the material, and the asset export does not carry it — so that one is a
likeness, and everything else is a copy.

**The game’s tokens are marked in place, on both kinds of row.** `{PC}`, `{M}`,
`{D}`, `{B}`, `{S}`, `{DA}` and `{F}` now show in a colour of their own on a
faint chip, where they sit in the sentence, so it is obvious at a glance that a
name goes there and those are not characters the player reads. They keep the
formatting around them, because what the game puts in their place will be bold
or coloured along with the rest of the line.

A brace pair that is **not** on that list — `{PCC}`, say — is deliberately left
looking like ordinary words, for the same reason an unknown tag is: the editor
cannot promise the game does anything with it, and dressing up a typo as a
working token is how the typo reaches the player.

### Writing the markup

**Formatting buttons over the Text box**, with the shortcuts beside them: bold
(Ctrl+B), italic (Ctrl+I), colour (Ctrl+Shift+C) and size (Ctrl+Shift+S). Each
one wraps whatever is selected — select a word, press the button, and the tag
opens in front of it and closes after it. With nothing selected you get an empty
pair with the cursor already between them.

If the cursor is nowhere — the box has not been clicked into yet — the pair
goes at the END of the line rather than at the start, which is where Windows
reports a cursor that was never placed.

Ctrl+B and Ctrl+I used to do something else entirely and quietly: a rich text
box answers them itself by applying Windows' own bold to what is on screen,
which is not markup, never reaches the pack, and disappears the next time the
line is redrawn. Those two keys are now the editor's.

### Seeing the shape of a conversation

**Every row shows what it is inside.** A line per node above it, down the left,
with the nearest one drawn stronger and carrying a short arm into the row — so
"this hangs off that" is readable without counting indents. On both kinds of
row: the plain rows follow your theme, and the game-look rows use their own
palette, because a theme colour on a fixed dark panel is a coin toss.

Depth used to be said only by how far in a row started, which reads at one level
and stops reading at three.

**A switch for the row style, over the node list.** It is the same setting as
Options ▸ Dialogue rows look like the game, in the place you are looking when
you want it.

**The root node ids are gone from that spot.** They were internal numbers nobody
acts on, and the roots are the rows at the left edge of the list anyway.

### The outfit panel

- **"= default" buttons** on Mask, Blink, Mouth prefix and Expr. prefix take
  the value straight from the character's default outfit. Hidden on the default
  outfit itself, and on a character with only one.
  - The **mask** one asks first, and it is worth reading: it points both
    outfits at the same FILE, not at a copy, so editing either one's mask
    afterwards changes the other's. Save it under a new name from the mask
    editor to break them apart.
- **File pickers open where you last picked**, rather than at the pack root.
  An outfit's art sits in one folder, so after the first pick the rest are one
  click away. A field that already points somewhere still opens there, and a
  folder from a different pack is never offered.

### Starting a mask from another outfit

**Copy layers from**, in the mask editor's Layers panel: pick another outfit of
the same character and load its mask over the one on the canvas. Most outfits
are the same bust in different clothes, and a jiggle mask is three intensity
planes painted by hand — a long way to come twice.

It asks before replacing anything, and says so when there are unsaved changes
on the canvas. Ctrl+Z puts it back.

Deliberately absent on a **place**: its two masks are the background and the
foreground of one scene rather than versions of each other, so pasting one over
the other would only ever be a mistake.

### Conditions and actions say what a variable holds

**A small note beside the variable name** — "yes/no", "number", "text",
"list" — in the words you would use rather than the ones a compiler would.
Whole and fractional numbers are both just a number; they compare and increment
the same way.

**The game's own variables are now included in that**, which is the half that
was missing. A comparison runs on strings at runtime, so "True" against a
variable holding "true" never matches and nothing says why. Pack variables have
had a tick box instead of a text field for a while; the game's 1,386 yes/no
variables did not, because nothing asked the game's catalogue. They do now,
and so do its numbers and its text.

Nothing is claimed where nothing is known: a name the editor does not recognise
gets no note and keeps the text field, so an unexpected text box reads as
"this name is not one I know" rather than as "this variable is text".

**The note shows on every row that names a variable**, not only on a Variable
condition: the list actions, the dice actions and the several conditions that
take a variable as one of their settings all carry it too. Those pickers are
editable — a List picker offers only lists and still accepts whatever you type
— so the note is what catches a scalar typed into one.

### The dialogue cheatsheet

- **Three more of the game’s address tokens**, confirmed to resolve: `{S}` (what
  Anna and Josef call the player and Adrian), `{DA}` (what Mario calls Emma) and
  `{F}` (the family). `{B}` now says what it actually does — the player and
  Adrian use it for **each other**, not just the player for Adrian.
- **Markup is listed beside them**: `<b>`, `<i>`, `<color=…>` and `<size=…>`.
  Only `<size>` appears anywhere in the game’s own 19,636 lines, so the other
  three say so on hover rather than being presented as proven.
- **It no longer crowds the dialogue tree.** Two columns instead of one, a
  height ceiling it cannot grow past, and a fold that is remembered between
  sessions. It had taken 167px of a column where the tree only gets about 214.
- The in-app reference had gone stale against it, still saying to treat anything
  beyond four tokens as unsupported. Both lists are now checked against one
  another so they cannot drift again.

## 1.3.2

The preview tells the truth about one of the game's busts. Three things it was
not showing, and one place where it and the game disagreed on purpose.

### The preview shows what your pack replaces

**Tick a texture, choose your PNG, and the bust beside the panel changes.** It
did not before. The preview of a borrowed bust loaded the game's art and stopped
there, with no notion that a pack could paint over any of it — so the replacement
you had just chosen was applied by the runtime, saved in your manifest, and
invisible in the editor. Base, mask, blink, the four mouth frames and every face,
each landing where it belongs.

The same rule the runtime has always followed holds here: a slot you have not
ticked is left exactly as the game drew it, and a slot ticked with no art chosen
yet keeps the game's texture rather than going blank.

It also no longer needs the vanilla art extraction to be present. Your own
replacement is your own file, and it should appear whether or not the editor has
the game's art beside it to draw underneath.

**And it updates as you type.** Pointing a row at a different file repaints the
bust. The row and the outfit are different things as far as change notifications
go, and the preview was listening to the outfit — so the picture stayed on
whatever it had loaded when you selected the bust.

### The mask painter works on a borrowed bust

**Strokes appear as you paint**, the way they always have on a bust the pack
draws. Edit Mask on one of the game's busts opened the painter perfectly well
and published every stroke into the override row it was opened from — which
nothing was reading. The preview reads the outfit.

Painting does not reload the bust's textures from disk on every stroke, which is
the trap next door to this one: the brush publishes several times a second, and
choosing new art is what should reread files, not moving a brush.

### One set of jiggle numbers, everywhere

**Every bust in the preview now moves by the pack's jiggle settings, the game's
own included** — and the runtime does the same to the busts your pack changes, so
what you author is what plays.

The preview used to run a borrowed bust on the uniforms the extractor read off
the game's own material. That is right only if this shader IS the game's, and it
is not: it is an approximation of it. The game's numbers through a different
shader do not reproduce the game, they produce a third thing — and one that
disagreed with every pack bust beside it, for reasons an author could not see and
could not change, since the jiggle sliders are hidden on a borrowed bust. It read
as the game's characters being mysteriously sluggish, which some of them are:
Adrian's own frequency is 1 where a pack bust starts at 4.

**This is a deliberate, visible divergence from the game.** A bust your pack
changes will move differently from the same bust untouched beside it. That is the
cost of the preview being honest about what it can reproduce, and it is the trade
this release makes.

Only busts your pack actually changes. Every other bust in the game keeps its own
motion, and an outfit whose art all failed to load is not re-jiggled either.

The defaults themselves lived in **three** copies — the editor's, and two
separate fallbacks in the runtime, one of them driving the blink, mouth and
expression overlays. They agreed only because nobody had yet changed one. There
is one copy now, compiled into both projects, so they cannot drift.

### Not changed

The jiggle sliders are still hidden on one of the game's busts, so a pack sets
those numbers without being able to tune them. Breathing is unchanged and has
never differed between a pack bust and a borrowed one: it is one speed and one
depth for whatever is on screen, because in game the offset is applied to the
busts' shared parent rather than to any bust.

## 1.3.1

**Updating itself works.** It never had.

The editor downloaded an update, unpacked it, and handed over to the new build
to copy itself into place — and that build died on its first line, every time,
before copying anything. What an author saw was the progress prompt, the editor
closing itself, and the same version still there when they opened it again.

The line was one that reads as obviously correct:

```csharp
StartupUri = null;   // so WPF does not open a window
```

WPF rejects null outright, so it threw. The process that threw was the one
started to do the replacing, after the editor had already been told to close —
so there was no window left anywhere to report it, and nothing to distinguish it
from an update that had simply decided to do nothing.

Every release that has shipped the updater is affected: 1.2.0, which introduced
it, and 1.3.0. **Installing this one fixes updating from any of them**, because
the build doing the copying is the NEW one — so a 1.2.0 or 1.3.0 editor pointed
at this release hands over to a version that can finish the job.

The window is now opened by the editor itself rather than by `StartupUri`, so
there is nothing for the applier to suppress. And the hand-over is tested by
starting a real editor with the real switch and looking at what lands on disk,
which is the only way to see it: the fault was in code that only runs in a second
process, and every test around it passed while the step they exist to reach had
never once run.

Nothing else changed. The plugin is rebuilt only so the pair stay in step — a
pack saved by a 1.3.1 editor names 1.3.1 as the runtime it wants, and a 1.3.0
plugin would report that as an error on a pack that is perfectly fine.

## 1.3.0

One release, one subject: **the game's own cast**. A pack could always put words
in their mouths. It can now change how they look, how they sound, what faces
they can pull and what colour their name is written in — and all of it holds in
the game's own scenes, not only in the pack's conversations.

### Before you update

**Every pack written before 1.3.0 has the game's characters put back the way the
game has them.** Those characters were being *stored* — names, colours, voices,
whole wardrobes — and none of it was wired to anything, so what a pack carried
was a record of edits that never happened. Rather than carry that forward into a
release where those fields finally mean something, they are reset.

**Busts your pack drew are kept.** An outfit with art of its own is your work,
not a copy of the game's, and it survives with everything on it.

Four smaller passes run alongside it, each reported by name when the pack loads:
machine-written names on the game's characters are put back, fields a pack no
longer sets are dropped, settings that only repeated the game's own defaults are
dropped, and expressions that only restated the game's are dropped.

As with every change to a saved pack: it happens on load, in memory, nothing is
written until you save, you are told what changed and how many, and the original
is kept beside the manifest the first time you save afterwards.

### What a pack can do to one of the game's characters

**Replace textures on a bust the game already has.** One row per texture — base,
mask, blink, the four mouth frames, and a row for each face. Tick only what you
are replacing. **A slot you do not tick is a slot your pack never mentions**, and
the game draws it exactly as it always did. That is the promise the whole feature
rests on, which is why the tick is not a stored setting: it *is* whether your
pack carries an entry for that texture.

- **Edit Mask** opens the painter on the jiggle mask, the same one a bust of your
  own uses. It is the one texture here nobody can author by hand — three
  intensity planes packed into R/G/B — so it was the one texture with no way to
  make it.
- **Add a bust the game never gave them.** Fully your art, sitting in their
  wardrobe beside the game's, and treated as yours everywhere: it is validated
  like your own bust, tagged **new** rather than **changed**, and a name that
  collides with one of the game's is refused rather than quietly shadowing it.
- **Give them a face they never had.** Named in the character's expression list
  and drawn from the outfit's expression prefix, the same as any other face.

**A name colour**, which replaces the one the game writes them in rather than
sitting beside it. **A typing voice** — cadence and pitch range — starting from
the character's real numbers rather than a generic default, so opening the panel
describes the character instead of overwriting them.

**Everything above applies in the game's own scenes.** A character you re-voiced
sounds re-voiced when the game plays its own conversation, and their name is
written in your colour there too.

### Knowing which of it is yours

- A **changed** tag on a character the pack has altered, and on each bust of
  theirs individually — a character can have sixty-five outfits and one replaced
  texture, and "something in here changed" does not say which row to open.
  **new** marks a bust your pack drew for them.
- A **reset** beside every field that has moved, and one that puts the whole
  character back. A field showing the game's own value has nothing to reset, and
  says so by having no button rather than by offering one that does nothing.
- None of this appears on a character your pack invented, where every field is
  yours and a tag on all of them says nothing.

### The game's own speech, written down

`Shared/VanillaSpeech.cs` — **84 of the game's characters**, of whom 54 can pull
a face and 36 have a name colour of their own. The editor offers a character's
real defaults and the runtime reproduces them, from one file compiled into both.

None of it is readable from the game's files: the Actor assets carry no
references an extractor can follow, and the name colours live in a private list
on a component that does not exist until a conversation has started. It is
extracted from a running game by `Tools/regen_vanilla_speech.py`, which refuses
to write if what it reads stops being uniform.

The faces turned out not to work by name at all. Each character owns **a number**
under the game's `Expressions` variable, and every bust on screen watches for any
global variable to change before switching to it — so a face switched on by hand
lasted until the next variable was written, by anything, and then reverted.
Writing the number is what makes a face stay.

`Shared/VanillaBustExpressions.cs` records which busts have faces: 209 have all
four, 76 have none, and not one has some other combination.

### Renaming something renames it everywhere

Change a character's key, a bust's name, a dialogue's key — anything the rest of
the pack refers to — and **every reference to it is rewritten with it**. Actions,
conditions, and the text of lines, across the whole pack. Before this, renaming
meant hunting through the UI for everything that named the old spelling, and
missing one produced a reference to something that no longer existed.

The **reset** on a borrowed character's key uses the same machinery, so putting a
key back is as safe as changing it.

### Searching inside a dropdown

**Type in any dropdown and it narrows to the names that contain what you typed** —
not only the ones that start with it, so `na` finds both **Anna** and **Nadia**.
It is not a separate mode or a separate box: it is how the dropdowns work now.

It filters on **what you actually typed**, never on what autocompletion put in
the box for you. Otherwise typing `An`, having `Anna` completed over it, and then
seeing the list collapse to that one entry would take **Adrian** away at exactly
the moment you were reaching for it.

### A variable check stops looking like a comparison

The store picker under a variable's Value was labelled **Compare to** and shown
on every check, which had people believing a variable check always compares one
variable against another. It never was an operand — it picks which store a
`$name` in the value is read from, and a plain value ignores it entirely.

It is called **Read from** now, the same thing the Set-variable editor already
called it, and it appears only once there is a `$name` to look up. A grey line
under the Value box says what a value accepts, so the ability to compare against
another variable still announces itself.

### Fixed

- **A re-voiced character sounded exactly as before in the game's own scenes**,
  and their name was still written in the game's colour. Both settings reached
  only the Actor the plugin synthesises for its own lines, and the game speaks
  through its own Actor assets and its own speech UI — so both did nothing
  anywhere except inside a pack-built conversation, which from the author's
  chair is indistinguishable from the setting being ignored. The pack's voice is
  now written onto the game's own Actor, and speaker colours are painted wherever
  a conversation appears from. Both put back what they found when the scene
  unloads: these are the game's assets, and a pack that has been unloaded should
  not still be speaking through them.
- **Every `neutral` expression reported a warning.** Reading a character's faces
  treated an expression mapped to an empty name as a face whose art was missing —
  and an empty name is how every pack written so far spells `neutral`, which
  means no face at all.
- **Clicking between characters and outfits could raise a rename prompt** and
  quietly repoint references. Selecting a character wrote the outfit selection
  without taking the snapshot the rename check compares against, so the next
  commit read the difference as a rename and rewrote every reference to the old
  name.
- **Double-clicking a validation issue about one of the game's characters did
  nothing.** The jump asked the character tree for the row, and the game's cast
  is filed under a heading that starts closed — a row inside a closed heading has
  never been built, so the lookup found nothing and the jump gave up before
  selecting, scrolling or flashing anything. It worked throughout for your own
  characters, whose heading opens by default, which is why it went unseen.
- **"Replace textures on this bust" came back unticked** every time a pack was
  reopened, hiding replacements that were still in the manifest and still being
  applied. The tick reads the pack now rather than starting blank.
- **Clicking a character landed on whichever bust was first** rather than the one
  they enter in. The same as each other until a pack says otherwise — at which
  point it showed a bust the author had never edited.
- **The plugin shipped its scene dumps.** F10, F11 and F12 wrote megabytes of
  reflected scene state into the player's game folder, and every release up to
  and including 1.2.0 carried them. They are compiled only into a Debug build
  now, and a check reads the bytes of the packaged DLL before a release goes out,
  because nothing in the ordinary test run can see it.

## 1.2.0

Two things the roadmap named for this release, and a third that grew out of
watching people install mods: **a UI tab**, **scenes that move**, and **a way to
hand a pack to somebody that they cannot get wrong**.

### Before you update

**Packs now live in a `Mods` folder in the game folder**, beside the game's
`.exe`. The old location — `BepInEx/plugins/SMSModForge/ModPacks/` — is still
read and always will be, so nothing you have installed stops working. It is
simply no longer the place anybody is sent: four levels inside BepInEx is a lot
to ask of somebody who just wants to play, and getting it wrong produces a game
that starts perfectly and does not have the mod in it.

If a pack ends up in **both** folders, the `Mods` copy is the one loaded, and
the menu says so in amber. That is worth reading rather than dismissing: editing
the copy that lost is an evening spent wondering why nothing changes.

**A pack's version now moves when you publish, and only when you publish.**
Saving does not move it, and neither does exporting. A version says what players
were given, and an afternoon of saving gives them nothing.

**Opening a pack stamps it** with the ModForge version that will write it, so
the game can tell you when a pack needs a newer runtime than you have. As with
every change to a saved pack, it is reported when the pack loads and the
original is kept beside the manifest the first time you save.

### A UI tab

**Screens the pack owns, and changes to the ones the game already has**, in one
tab because from an author's side they are the same job.

- **Start one from a shape** — a blank panel, a window with a title bar and a
  close button, a dialog, a list, a tooltip. Or **+ Vanilla** to build on one of
  the game's own screens, which fills the tree with what is actually in it.
- **A tree of objects** with the properties of whichever is selected underneath:
  name, position, size, art, text and font. Objects nest, and an object is
  positioned against its parent, so moving a panel takes its contents along.
- **A preview against the game's own canvas**, at the size the game uses, with
  the selected object picked out. An object off the edge here is off the edge in
  the game.
- **Only your differences are stored** on a screen built from a vanilla one. An
  object you never touch is not in your pack at all, which is why **Reset** on
  an object makes it stop being a change — and why a pack that alters one label
  in the shop stays a pack that alters one label, rather than a copy of the shop
  that will not survive the game being patched.
- **Switch one on** with the Set-Active action, category **UI**, the same way
  you switch on a scene.

### Scenes that move

**A scene's art can now be a GIF or a video** — `.gif`, `.mp4`, `.m4v`, `.mov`
or `.webm` — decided by the file's extension, because that is the thing an
author controls. Rename something to `.png` and it stays a still.

- **GIFs are decoded to frames when the pack is saved**, not in the game while a
  scene is opening. Frames carry their own delays, so a title card held for a
  second in front of a fast loop plays the way it was authored.
- **Videos are played by the engine's own player**, and if one carries an audio
  track the editor notices and offers a volume slider. No track, no slider.
- **Animation is a Scenes feature**, and says so: point an animated file at a
  bust or a level layer and validation explains why rather than letting it fail
  quietly in the game.
- **Art of any resolution is fitted to the scene square**, the same as the
  runtime does. A 512×512 scene used to preview at twice the size of its
  neighbours and then play at the same size as them.

In the editor, **a GIF scene animates in the preview** — an animation you can
only see by exporting and starting the game is one you cannot judge — while a
video is held on its first frame, because that costs a decoder rather than a
blit. **When Windows has no decoder for a format the game plays perfectly
well**, a still is lifted out of the file directly so there is something to
look at.

The file picker on the Scenes tab now offers all of that, rather than PNGs only.

### Publishing

**File ▸ Publish for players** packages a release. It checks the pack over
first, says so if anything is broken and lets you overrule it, moves the
version, writes the archive, and opens the folder it wrote to.

What it writes is not a pack file. It is a picture of the game folder with the
pack already in the right place:

    MyPack-1.2.0.zip
      Mods/MyPack.smspack

Extract that into the game folder and the install is finished. There is no step
left to get wrong, because there is no step — and nothing else is in the
archive, so installing a second pack never asks anybody to overwrite anything.
The download carries the version so three of them can be told apart; the
installed file does not, so an update replaces the pack rather than leaving two
for the game to load.

The runtime plugin is deliberately not included. Two packs shipping different
builds of it would overwrite each other's runtime, and the breakage would land
on a player who did nothing wrong.

**Export is still there and unchanged** — it is the quick loop for trying your
own work in the game, and it moves nothing.

### The number on your pack

**A version, shown beside the pack's name in the game's menu.** It moves by
itself, by what actually changed since your last release: something new moves
the middle number, a change to something that was already there moves the last
one. The first is yours alone, typed in when you decide a release is a new thing
rather than more of the old one — and a version you type is published exactly as
typed.

Packs written before this get **0.1.0** rather than 0.0.0. They have content in
them, often a great deal, and calling that "nothing yet" would be wrong the
moment an author looked at the field.

**A warning if a version would go backwards**, which is the one version mistake
a player actually notices.

### What the game's menu tells you

The banner now names the runtime doing the reading — **Mods · ModForge 1.2.0** —
because every judgement below it is relative to that number. Each pack is listed
with whatever is wrong with it:

- **red** — it will not work as authored: an archive that will not open, one
  built for a different build of the game, or one made with a **newer ModForge
  than this runtime**, which may use things this runtime has never heard of.
- **amber** — it works, but it is not what you think: built for an older
  ModForge, no ModForge version recorded at all, or **installed twice**.

Everything is loaded anyway. A pack that half-works is more use than one that
refuses, and the row is what explains the difference.

### Conditions and actions

**Thirteen types became three.** The operator and the store used to be encoded
in the type name, which is why there were ten ways to compare a variable; both
are fields now.

- **Variable** — one condition with a **Source** (Pack or Vanilla) and a
  **Comparison** (equals, greater than, less than, exists), replacing
  `VariableEquals`, `VariableGreaterThan` and the seven others like them.
- **Set Active** — one action with a category, replacing the separate
  `ActivateScene`.
- **Emit Signal** — one action with an optional delay, replacing
  `EmitSignalDelayed`.

Existing packs are converted when they load, reported, and backed up the first
time you save. Nothing needs doing by hand.

**Checking a boolean is now two radio buttons**, True and False, instead of a
comparison and a Negate box that had to be reasoned about together.

### The editor

- **The long dropdowns are grouped.** The game's 1,644 variables are filed under
  the 60 lists that own them; levels split into this pack's and the game's; your
  own variables follow the folders you put them in. A flat list of that length is
  a scroll bar with no landmarks.
- **The export size warning** now speaks up at 150 MB rather than 512, allows
  twice as many files before it counts them as suspicious, and can be turned off
  for a pack that is genuinely that large.

### Vanilla bust art

**The busts shipped with the editor were being mangled.** They travel to a
smaller size for the build and back again for the screen, and both halves of
that trip point-sampled at a non-integer ratio — which does not read as low
resolution art, it deforms faces and gives a character one eye larger than the
other. Both halves interpolate now, and the shipped copies are reduced by 1.25
rather than 1.5. They are recognisably the art again.

### Tutorials

- **A screen of your own** — a new tutorial for the UI tab, which shipped
  without one.
- **First steps now ends with the release**: exporting to test, publishing to
  hand it over, and what the version means.
- **Two checks that no amount of following the tutorials could have caught**:
  one that fails if any tab has no tutorial visiting it, and one that fails if a
  tutorial names something the editor no longer offers. Both found real
  problems, which is why they exist.

### Fixed

- **The editor no longer jumps to another tab** when you press a list button
  like **+ Rule** or **+ Variable**. Those toolbars were focus scopes, so WPF
  handed keyboard focus back at the end of the click — and what it handed it
  back to was a tab header, which selects its own tab when focused. It only
  misfired when the header it remembered was not the tab you were looking at,
  which is why it came and went. Focus now stays on the button you pressed.

### Added

- **The editor checks for a new version when it starts**, and offers it with
  the release notes in a window — rendered, not raw, so headings read as
  headings instead of hashes: the version, what changed, how big the
  download is, and whether the plugin will be updated too. Say yes and it
  downloads, closes (asking about an unsaved pack exactly as the X does),
  puts the new build in place, starts it again — and the editor that comes back
  says which version it updated from, so the restart is confirmation rather
  than something to infer from the title bar.
- **Options ▸ Check for updates on start** — on by default. Off means no
  request is made at all, not one whose answer is ignored. The prompt carries
  the same setting as a checkbox, so you can turn it off from the window that
  prompted you — however you then close that window.
- **Options ▸ Install updates without asking** — off by default. On, an update
  installs itself and says so in a line at the top of the window rather than
  opening anything.
- **Options ▸ Starmaker Story folder** lets the updater replace the plugin in
  your game folder at the same time, so the pair cannot drift apart. It writes
  only the plugin and what it needs — never BepInEx itself, and never
  ModPacks. A file that cannot be replaced — something has it open — no longer
  stops the rest, and the update says which file and why in a window rather
  than a line that the next step overwrites.
- **Options ▸ Starmaker Story folder** lets the updater replace the plugin in
  your game folder at the same time. If it has never been set, the prompt offers to set it there
  and then, rather than naming a menu you would have to close the prompt to
  reach.
- **Options ▸ Check for updates now**, which answers either way.

- **An `InputKey` condition** — gate anything on a keyboard key or a mouse
  button. Four phases: **Pressed** and **Released** are moments, true once per
  press; **Down** and **Up** are states, true for as long as they hold.
- The key is chosen from grouped dropdowns rather than typed. The game reads
  keys by position rather than by the letter on the cap, so the groups that
  move between keyboard layouts say so in their headings, and the ones that do
  not — mouse, arrows, modifiers, function keys, numpad — come first.
- Validate flags a missing key, a key name the picker does not carry, and an
  edge phase on a dialogue node's own conditions, which are checked once when
  the conversation reaches the node and so will almost never catch one.
- **Set-Active gains a Toggle**, alongside Activate and Deactivate: flip the
  target to the opposite of whatever it currently is. It reads the object's own
  state, so a parent hidden around it does not count as off.
- **Runtime names derive from display names** for NPCs, places, scenes, music,
  SFX, wallpapers and integration rules, the way characters and dialogues
  already did. A unit loaded from disk never re-derives, since its key is what
  everything else refers to it by.
- The runtime name is now a **read-only display** rather than a field to fill
  in. It stays selectable, because the runtime names units by key in its log,
  validation messages are keyed on it, and a cross-pack reference is written as
  pack.key — so it is worth reading even though there is nothing to type.
- **Every music dropdown lists the game's own tracks** as well as the pack's —
  all 38 of them, under **This pack** / **The game’s own** headings. Buttons,
  navigator buttons and the SwitchMusic action all name a child of the same
  object, so pointing one at the game’s own music was always legal; until now
  you had to know how it was spelled. The fields stay typable: the list comes
  from a dump of an older build, so anything added since can still be entered
  by hand.
- **A new map button starts on `Music`**, the game’s ordinary track: it crosses
  the world map into somewhere new, so it should say what plays there. **A new
  navigator button still starts empty** — it is a door within one area, and the
  area’s music should carry across it. An empty box means the game does not
  touch the music at all, which is also what clearing either box does. Buttons
  already saved with an empty Music are left exactly as they are.
- Renaming a track refreshes the music dropdowns. SFX already did this; music
  did not, so a renamed track went on being offered under its old key until the
  app restarted.
- Validate reports a reference that names **an effect or scene this pack does
  not have** — from PlaySFX, or a Set-Active aimed at a scene. Music references
  are deliberately not checked: they name a child of 12_AudioPlayer, and the
  game's own tracks live there beside the pack's.

### Fixed

- **Resizable panes are proportional.** Every tab's splitter columns were fixed
  pixel widths, and a drag wrote another fixed width, so a layout arranged on a
  maximised window kept those exact widths on a small one — the content column
  was squeezed to nothing and the right of the tab sat off screen. They are
  shares now, so panes scale with the window. Saved layouts store the ratio
  rather than the pixels; a layout saved by 1.1.0 is ignored once, and the
  window will not size below what its widest tab needs.
- The app icon carried a single 32x32 frame, so everywhere Windows wanted
  something bigger it was upscaling 32 pixels. It now holds 16 through 256,
  each resampled from the 512x512 source.
- Context menus painted a light band down their left edge, over the start of
  every label — the stock popup template drew its own icon gutter behind the
  themed one.
- The issues list marks the silenced issues when it shows them, and its context
  menu enables each entry by what the selected row actually is. "Stop ignoring
  this" was previously offered on every row, including ones that were not
  ignored, which read as an action with no way to reach it.
- Double-clicking an issue about an action or condition jumps to **that** one.
  The issue's location named the list but not the position, and the row was
  found by matching its type, so with two Variable actions on a node both
  issues flashed the first. Ignore entries deliberately stay position-free, so
  inserting an action above a silenced one does not un-silence it.
- The same jump works for an action inside a DiceRoll branch. Those rows live
  inside the DiceRoll's own row rather than in the node's action list, so the
  search that finds every other action could not see them and the jump fell
  back to flashing the whole node.
- **"Ignore every issue like it" now works on every check.** It needs a code to
  key a pack-wide rule on, and only 7 of 115 checks had one, so the option was
  greyed out almost everywhere. All 115 are coded now.

## 1.1.0

The first update since the initial release. It is mostly about **learning the
tool** — the standing complaint was that ModForge is hard to pick up — plus the
editor fixes and format corrections that came out of actually sitting down and
following the tutorials end to end.

### Before you update

**Re-saving a pack that uses a `GameObjectActive` condition requires the 1.1.0
plugin.** That condition used to store its target in `path`; it now stores
`kind` + `target` (see *Conditions* below). The 1.1.0 plugin still reads `path`,
so packs authored in 1.0.0 keep working untouched — but a pack re-saved in the
1.1.0 editor drops `path`, and a 1.0.0 plugin reading it finds nothing and
treats the condition as failed, so gated lines silently stop playing. Ship the
editor and the plugin together.

Nothing else changes on disk. The manifest is still `smsmodforge/modpack/v1`.

### Learning and documentation

- The in-app reference now opens with a **Start here** section that builds the
  mental model before the per-control detail: what a pack is, what order to work
  in, how things point at each other, and what to check when something does not
  work.
- **Addressing** and **text substitution** are documented for the first time.
  Place tokens (`vanilla:`, `place:`, `self:`, `pack:`) and the hierarchy paths
  that Set-Active and friends accept were visible on screen and explained
  nowhere.
- The mask painter documentation was wrong in ways that mattered: a place mask
  has **one** layer in alpha, not three; and the layer names and the setting
  names genuinely disagree, which is now named rather than repeated.
- The jiggle settings use **one vocabulary** across Characters and NPCs instead
  of two different sets of labels for the same six values.
- Every control **explains itself on hover**. The reference is no longer the
  only place a field is described.
- The README describes the tool as it is. The old one named tabs and actions
  that do not exist.

### Tutorials

- Rebuilt as a **linear progression, grouped by tab**, from a pack with nothing
  in it. 14 tutorials now, covering every tab — Media, NPCs, the world map,
  Scenes, Music, SFX and Wallpapers had none at all.
- Practice art and audio **ship with the editor** and are copied into your pack,
  so no tutorial points at a file only its author has.
- A tutorial you finished tells you when it has been **rewritten since**, rather
  than staying quietly ticked.
- Tutorial steps are covered by an **automated walk-through** that proves each
  one can actually be completed, and that no step is already satisfied the
  moment it opens.

### Editor

- **Art of any size is fitted** to the frame it was authored for, evenly, rather
  than stretched — busts to 256×256 and place art to 2048×1136. The preview does
  exactly what the game does.
- **Validate** reports art that is the wrong size or the wrong shape, and any
  warning can be silenced by type or one at a time, per pack.
- Warnings for things that used to fail silently: a `[PV:name]` naming a
  variable nothing declares (it resolves to an empty string and vanishes from
  the line), a `[PV:` that never closes (it is printed to the player as typed), a
  line that spells out **Mom**, **Dad** or **Brother** instead of `{M}`, `{D}`,
  `{B}`, and a Choice node with no options.
- A **token cheatsheet** under the Dialogues sidebar: `{PC}`, and `{M}` `{D}`
  `{B}` for what this player calls Anna, Josef and Adrian.
- **Music** is a dropdown of the pack's own tracks on map buttons and navigator
  buttons, instead of a name typed from memory.
- **Node editor**: Expression and Outfit are hidden while the player is
  speaking, the jump destination only appears when the mode is Jump (and is a
  dropdown of the dialogue's tags, renamed **Jump to**), and Timeout only
  appears when Duration is Timeout.
- A dialogue's **runtime name is derived from its display name**, the way a
  character's already was.
- **Jiggle** gains a Default button on every field, for busts and NPCs.
- The **mask editor** saves beside the art it was painted over, named after it —
  `AnnaBase.png` proposes `AnnaBaseMask.png`.
- The **prefix fields** say they are paths, and trail a grey hint showing what
  the game will actually load. Picking a blink PNG fills in the folder.
- Expressions moved into the **Sprites** box; they were never a separate
  feature.
- The level preview's gizmo gains a **Reflection** part, shown when the NPC has
  one, alongside Body / Shadow / Blink / Wet.
- The scroll wheel works over the dimmed area during a tutorial.

### Conditions and actions

- **`GameObjectActive`** uses the same Category + Target row the Set-Active
  action uses — Bust, GameObjects (scoped to a level), Scene or Direct Path —
  and resolves the same way at runtime. It now finds inactive objects and scopes
  a level-overlay lookup to the level named, so a same-named object in the room
  being left can no longer answer for the one being entered. `$varName` works
  here too.
- A Choice's options no longer offer **Actions on start**. When GC2 considers an
  option "started" is not something a pack can rely on — it may be when the menu
  is drawn rather than when the option is picked — so on-finish is the only
  offered hook, and older packs carrying such actions are flagged.

### Fixes

- Adding a node with **+ Child** or **+ Sibling** copied the kind of the node it
  came from. Under a Choice that made every option a Choice, and everything
  under those read as options too — a follow-up line lost its Actions-on-start
  box and would have been built as a one-entry menu. New nodes are plain lines;
  Validate flags the packs that already carry the damage.
- Two preview dropdowns had a tooltip written as element content, so the tooltip
  text became the **first item in the list**. Mouth frame therefore ran a frame
  behind, and Expression defaulted to the tooltip string.
- Previews reload after a mask is saved.
- The Characters toolbar buttons stay on screen, and **+ Outfit** is disabled
  until a character that can have one is selected.
- The player character exists in a new pack, and dialogue lines show a speaker.

### Repository

- `Tools/` is tracked. It was ignored wholesale — 29 files, including the
  thumbnail generator that produces art the release ships and the six Unity
  extractors that refresh the vanilla catalogs. A fresh clone could not rebuild
  a release.
- `DocCoverage.py` audits the in-app reference against the editor's own
  functions, so "document everything" is checkable.

## 1.0.0

Initial release.
