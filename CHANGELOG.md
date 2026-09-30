# Changelog

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
