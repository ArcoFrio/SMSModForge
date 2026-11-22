using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The guide's examples followed from their words alone, with <see cref="UiDriver"/> (1.10.0): each test is one
/// example's steps as written on the Guide page, driven only by what the screen says. A step whose words the screen
/// does not answer fails with what the screen shows instead - the guide's mistake to put right, not the test's.
/// <para/>
/// Written by an agent that read only the guide's words (the author asked for one to "attempt to follow some of the
/// written tutorials on the Guides tab"). Its first run found two editor bugs - a conversation's Required level list
/// opened onto the Beach alone, and Reset character not shown after a game character's first change - and the words
/// that did not match the screen (pronouns, runtime names in lists, steps needing a conversation that "Before you
/// start" never made, a folded section). Each step still tries the words first (<see cref="OrElse"/>): a step that
/// needs what a reader would work out instead is a FINDING, and a test ends failing on any (<see cref="Done"/>).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GuideFollowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SMSModForge-follow-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _findings = new();
    public GuideFollowTests(ITestOutputHelper o) => _out = o;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// <summary>The driver itself, on the first step of "A greeting once a day": a variable made from its words.</summary>
    [Fact]
    public void TheDriverFollowsAStepByItsWords()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            ui.Tab("Variables");
            ui.Click("+ Variable");
            ui.Type("Name", "GreetedToday");
            ui.Pick("Type", "Bool");
            ui.Tick("Persisted");
            _out.WriteLine(ui.Screen());
            Assert.Equal("GreetedToday", ui.ValueOf("Name"));
            Assert.Equal("ticked", ui.ValueOf("Persisted"));
            // Control: words that are on no control fail, and say what is on screen.
            var miss = Assert.Throws<UiDriverMiss>(() => ui.Click("+ Unicorn"));
            Assert.Contains("on screen", miss.Message);
        });
    }

    /// <summary>"Before you start": a character and a place, as its first step says.</summary>
    [Fact]
    public void Before()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            _out.WriteLine($"your character: {who}, your place: {where}");
            // Nothing for Validate to say: the page has the reader choose the character's Pronouns.
            Done(ui);
        });
    }

    /// <summary>"A greeting once a day".</summary>
    [Fact]
    public void OnceADay()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            // s1: "On the Variables tab, click + Variable. Name it GreetedToday, set Type to Bool and pick Starts false.
            //      Set Refresh mode to Daily, and tick Persisted so the save keeps it."
            ui.Tab("Variables");
            ui.Click("+ Variable");
            ui.Type("Name", "GreetedToday");
            ui.Pick("Type", "Bool");
            ui.Click("Starts false");
            ui.Pick("Refresh mode", "Daily");
            ui.Tick("Persisted");
            // s2: "On the Dialogues tab, click + Dialogue and name it "Morning hello"."
            ui.Tab("Dialogues");
            ui.Click("+ Dialogue");
            ui.Type("Display name", "Morning hello");   // "name it": the box beside Display name
            // "Under Start conditions, the first row reads Required — this dialogue only starts in this level:
            //  choose your place in it."
            ChooseYourPlace(ui, where);
            // "Then click + Add condition: Variable, GreetedToday, equals, false."
            ui.Click("+ Add condition", near: "Start conditions");
            ui.Pick("Type", "Variable", near: "Start conditions");
            ui.Pick("Name", "GreetedToday", near: "Start conditions");
            ui.Pick("Comparison", "equals", near: "Start conditions");
            ui.Click("False", near: "Start conditions");   // Value is a True / False choice for a Bool

            // s3: "Click + New Node. Choose your character as the Actor and write the Text: "Good morning! You're early today.""
            ui.Click("+ New Node");
            ChooseYourCharacter(ui, who);
            ui.Type("Text", "Good morning! You're early today.");
            // s4: "With that line chosen, add an action under Actions on finish: Variable, Set, GreetedToday, true."
            ui.Click("+ Add action", near: "Actions on finish");
            ui.Pick("Type", "Variable", near: "Actions on finish");
            ui.Pick("Operation", "Set", near: "Actions on finish");
            ui.Pick("Name", "GreetedToday", near: "Actions on finish");
            ui.Click("True", near: "Actions on finish");   // Value: True / False

            // What it promises: a line said once a day.
            Assert.Equal("Good morning! You're early today.", ui.ValueOf("Text"));
            Assert.Equal("place:Gallery", ui.ValueOf("Level", near: "Start conditions"));
            Assert.Equal("GreetedToday", ui.ValueOf("Name", near: "Actions on finish"));
            Done(ui);
        });
    }

    /// <summary>"A choice she remembers".</summary>
    [Fact]
    public void RememberedChoice()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            // s1: "On the Variables tab, click + Variable, name it LikesJazz, set Type to Bool, pick Starts false and tick Persisted."
            ui.Tab("Variables");
            ui.Click("+ Variable");
            ui.Type("Name", "LikesJazz");
            ui.Pick("Type", "Bool");
            ui.Click("Starts false");
            ui.Tick("Persisted");
            // s2: "Make a conversation "Music taste" for your place, as in A greeting once a day, step 2 (without the
            //      GreetedToday condition)."
            ConversationForYourPlace(ui, "Music taste", where);
            // "Click + New Node, set Kind to Choice, choose your character as the Actor and write "Do you like jazz?"."
            ui.Click("+ New Node");
            ui.Click("Choice");   // Kind: a row of choices (Text, Choice, Random)
            ChooseYourCharacter(ui, who);
            ui.Type("Text", "Do you like jazz?");
            // "Click + Child and write "Love it."; then, with that answer chosen, click + Sibling and write "Not really.""
            ui.Click("+ Child");
            ui.Type("Text", "Love it.");
            ui.Click("+ Sibling");
            ui.Type("Text", "Not really.");
            // s3: "Choose "Love it." and add an action under Actions on finish: Variable, Set, LikesJazz, true."
            ui.Select("Love it.");
            ui.Click("+ Add action", near: "Actions on finish");
            ui.Pick("Type", "Variable", near: "Actions on finish");
            ui.Pick("Operation", "Set", near: "Actions on finish");
            ui.Pick("Name", "LikesJazz", near: "Actions on finish");
            ui.Click("True", near: "Actions on finish");
            // s4: "Choose the question and click + Sibling for a line after it: "I'll put some jazz on for you.""
            ui.Select("Do you like jazz?");
            ui.Click("+ Sibling");
            ui.Type("Text", "I'll put some jazz on for you.");
            // "Under Conditions, add Variable, LikesJazz, equals, true."
            ui.Click("+ Add condition", near: "Conditions");
            ui.Pick("Type", "Variable", near: "Conditions");
            ui.Pick("Name", "LikesJazz", near: "Conditions");
            ui.Pick("Comparison", "equals", near: "Conditions");
            ui.Click("True", near: "Conditions");

            // What it promises: a question with two answers, and a line only one of them hears.
            foreach (var row in ui.Rows()) _out.WriteLine("ROW: " + row);
            ui.Select("Love it.");
            Assert.Equal("LikesJazz", ui.ValueOf("Name", near: "Actions on finish"));
            ui.Select("Do you like jazz?");   // control: the question itself sets nothing
            Assert.Throws<UiDriverMiss>(() => ui.ValueOf("Name", near: "Actions on finish"));
            ui.Select("I'll put some jazz on for you.");
            Assert.Equal("LikesJazz", ui.ValueOf("Name", near: "Conditions"));
            Done(ui);
        });
    }

    /// <summary>"Something else when it rains".</summary>
    [Fact]
    public void RainyLine()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            // s1: "Make a conversation "Weather talk" for your place, as in A greeting once a day, step 2."
            ConversationForYourPlace(ui, "Weather talk", where);
            // "Click + New Node, choose your character and write "Did you get caught in the rain?"."
            ui.Click("+ New Node");
            ChooseYourCharacter(ui, who);
            ui.Type("Text", "Did you get caught in the rain?");
            // "Under Conditions, add Weather, Weather is Raining."
            ui.Click("+ Add condition", near: "Conditions");
            ui.Pick("Type", "Weather", near: "Conditions");
            ui.Pick("Weather is", "Raining", near: "Conditions");
            // s2: "Click + Sibling and write "What a lovely day.". Add the same condition, Weather, Raining, and tick its Negate."
            ui.Click("+ Sibling");
            ui.Type("Text", "What a lovely day.");
            ui.Click("+ Add condition", near: "Conditions");
            ui.Pick("Type", "Weather", near: "Conditions");
            ui.Pick("Weather is", "Raining", near: "Conditions");
            ui.Tick("Negate", near: "Conditions");

            // What it promises: one line for rain, one for every other day.
            foreach (var row in ui.Rows()) _out.WriteLine("ROW: " + row);
            Assert.Equal("ticked", ui.ValueOf("Negate", near: "Conditions"));
            ui.Select("Did you get caught in the rain?");
            Assert.Equal("Raining", ui.ValueOf("Weather is", near: "Conditions"));
            Assert.Equal("unticked", ui.ValueOf("Negate", near: "Conditions"));
            Done(ui);
        });
    }

    /// <summary>"Music of your own in a place".</summary>
    [Fact]
    public void PlaceMusic()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (_, where) = CharacterAndPlace(ui);
            // s1: "On the Music tab, click + Music, name it "Gallery Jazz" and choose an audio file (.ogg, .wav or .mp3)
            //      inside your pack folder for Audio path."
            ui.Tab("Music");
            ui.Click("+ Music");
            ui.Type("Display name", "Gallery Jazz");   // "name it": the box beside Display name
            ui.Type("Audio path", "TutorialArt/Audio/Neon Rain.mp3");   // the box beside the "..." picker

            // s2: "On the Places tab, choose your place. Under On enter, click + Add action group, then + Add action in
            //      the group: SwitchMusic, Music key GalleryJazz."
            ui.Tab("Places");
            ChooseYourPlaceInTheList(ui, where);
            ui.Click("+ Add action group", near: "On enter");
            ui.Click("+ Add action", near: "On enter");
            ui.Pick("Type", "SwitchMusic", near: "On enter");
            PickNamed(ui, "Music key", "GalleryJazz", "On enter", "placeMusic s2");

            // What it promises: the track starts as the player walks in.
            Assert.Equal("SwitchMusic", ui.ValueOf("Type", near: "On enter"));
            Assert.Equal("GalleryJazz", ui.ValueOf("Music key", near: "On enter"));
            Done(ui);
        });
    }

    /// <summary>"A button on screen that plays a sound".</summary>
    [Fact]
    public void ScreenButton()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            CharacterAndPlace(ui);
            // s1: "On the SFX tab, click + SFX, name it "Chime" and choose a sound file for Audio path."
            ui.Tab("SFX");
            ui.Click("+ SFX");
            NameIt(ui, "Chime");
            ui.Type("Audio path", "TutorialArt/Audio/15419__pagancow__dorm-door-opening.wav");
            // s2: "On the UI tab, click + UI, name it "Chime button" and tick On screen from the start."
            ui.Tab("UI");
            ui.Click("+ UI");
            NameIt(ui, "Chime button");
            ui.Tick("On screen from the start");
            // s3: "Click + Object and give it the Text "Ring". Open What clicking does (click its heading), then click
            //      + Action: PlaySFX, Clip Chime."
            ui.Click("+ Object");
            ui.Type("Text", "Ring");
            ui.Open("What clicking does");
            ui.Click("+ Action", near: "What clicking does");
            ui.Pick("Type", "PlaySFX", near: "What clicking does");
            PickNamed(ui, "Clip", "Chime", "What clicking does", "screenButton s3");

            // What it promises: an object the player clicks to hear the chime.
            Assert.Equal("Ring", ui.ValueOf("Text"));
            Assert.Equal("Chime", ui.ValueOf("Clip", near: "What clicking does"));
            Done(ui);
        });
    }

    /// <summary>"A visitor on rainy days".</summary>
    [Fact]
    public void RainyVisitor()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (_, where) = CharacterAndPlace(ui);
            // s1: "On the NPCs tab, click + NPC, name it "Wet visitor" and choose a picture for its Pose sprite."
            ui.Tab("NPCs");
            ui.Click("+ NPC");
            NameIt(ui, "Wet visitor");
            ui.Type("Pose sprite", "TutorialArt/NPCs/Visitor/Visitor0.png");
            // s2: "On the Places tab, choose your place and click + Add NPC. Choose Wet visitor as its NPC, then drag it
            //      in the preview to where it should stand."
            ui.Tab("Places");
            ChooseYourPlaceInTheList(ui, where);
            ui.Click("+ Add NPC");
            PickNamed(ui, "NPC", "Wet visitor", null, "rainyVisitor s2");
            // (dragging in the preview: the mouse is not driven here; where it stands does not change what it does)
            // s3: "On the placement, under Active when, add a condition: Weather, Raining."
            //     The page shows several Active when headings (each GameObject's, then the placement's): the
            //     placement's is the one below its NPC: box.
            ui.Click("+ Condition", near: "NPC: > Active when");
            ui.Pick("Type", "Weather", near: "NPC: > Active when");
            ui.Pick("Weather is", "Raining", near: "NPC: > Active when");

            // What it promises: the NPC stands there only while it rains.
            Assert.Equal("Raining", ui.ValueOf("Weather is", near: "NPC: > Active when"));
            Done(ui);
        });
    }

    /// <summary>"Count the player's visits".</summary>
    [Fact]
    public void CountVisits()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            // s1: "On the Variables tab, click + Variable, name it Visits, set Type to Int and Default value to 0, and
            //      tick Persisted so the save keeps it."
            ui.Tab("Variables");
            ui.Click("+ Variable");
            ui.Type("Name", "Visits");
            ui.Pick("Type", "Int");
            ui.Type("Default value", "0");
            ui.Tick("Persisted");
            // s2: "On the Integration tab, click + Rule, name it "Count visits" and set Trigger mode to OnRisingEdge."
            ui.Tab("Integration");
            ui.Click("+ Rule");
            NameIt(ui, "Count visits");
            ui.Pick("Trigger mode", "OnRisingEdge");
            // "Add the condition LevelActive, Level your place, and the action Variable, Increment, Visits, Delta 1."
            ui.Click("+ Add condition");
            ui.Pick("Type", "LevelActive", near: "Conditions");
            ui.Pick("Level", where, near: "Conditions");
            ui.Click("+ Add action");
            ui.Pick("Type", "Variable", near: "Actions");
            ui.Pick("Operation", "Increment", near: "Actions");
            ui.Pick("Name", "Visits", near: "Actions");
            ui.Type("Delta", "1", near: "Actions");
            // s3: "In one of your conversations (none yet? make one as in A greeting once a day, steps 2 and 3), choose a
            //      line, click + Sibling and write "You again! I'm starting to like this.". Give it the condition
            //      Variable, Visits, greater or equal, 3."
            AConversationOfYours(ui, who, where, "countVisits s3");
            ui.Select("Hello.");
            ui.Click("+ Sibling");
            ui.Type("Text", "You again! I'm starting to like this.");
            ui.Click("+ Add condition", near: "Conditions");
            ui.Pick("Type", "Variable", near: "Conditions");
            ui.Pick("Name", "Visits", near: "Conditions");
            ui.Pick("Comparison", "greater or equal", near: "Conditions");
            ui.Type("Value", "3", near: "Conditions");

            // What it promises: a counter the rule raises on each arrival, and a line for regulars.
            Assert.Equal("Visits", ui.ValueOf("Name", near: "Conditions"));
            Assert.Equal("3", ui.ValueOf("Value", near: "Conditions"));
            Done(ui);
        });
    }

    /// <summary>"A quest with two tasks".</summary>
    [Fact]
    public void TwoTaskQuest()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            QuestWithTwoTasks(ui, who, where, log: true);

            // What it promises: a quest with two tasks, started and finished from lines.
            ui.Tab("Quests");
            var rows = ui.Rows();
            foreach (var row in rows) _out.WriteLine("ROW: " + row);
            Assert.Contains(rows, r => r.Contains("Dust the vase."));
            Assert.Contains(rows, r => r.Contains("Hang the poster."));
            Done(ui);
        });
    }

    /// <summary>"A wallpaper as a reward" - after "A quest with two tasks", as its summary says.</summary>
    [Fact]
    public void RewardWallpaper()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            QuestWithTwoTasks(ui, who, where);
            // s1: "On the Wallpapers tab, click + Wallpaper, name it and choose a 1920x1080 picture for its Sprite path."
            ui.Tab("Wallpapers");
            ui.Click("+ Wallpaper");
            NameIt(ui, "Tidy reward");
            ui.Type("Sprite path", "TutorialArt/Wallpapers/DummyWPP.png");
            // s2: "Under Unlock conditions, add QuestState: Tidy the gallery, Dust the vase., completed."
            ui.Click("+ Add condition", near: "Unlock conditions");
            ui.Pick("Type", "QuestState", near: "Unlock conditions");
            ui.Pick("Quest", "Tidy the gallery", near: "Unlock conditions");
            ui.Pick("Task", "Dust the vase.", near: "Unlock conditions");
            ui.Pick("Is", "completed", near: "Unlock conditions");

            // What it promises: a wallpaper hidden until the task is done.
            Assert.Equal("completed", ui.ValueOf("Is", near: "Unlock conditions"));
            Done(ui);
        });
    }

    /// <summary>"A room that opens later" - after "A quest with two tasks", as its step 4 needs.</summary>
    [Fact]
    public void SecretRoom()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            QuestWithTwoTasks(ui, who, where);
            // s1: "On the Places tab, click + Place, name it "Back room" and, under Level art, choose its Back sprite and
            //      Front sprite: 2048x1136 pictures, the back one the room, the front one what stands before it (mostly
            //      see-through)."
            ui.Tab("Places");
            ui.Click("+ Place");
            NameIt(ui, "Back room");
            ui.Type("Back sprite", "TutorialArt/Locations/RoomB.png", near: "Level art");
            ui.Type("Front sprite", "TutorialArt/Locations/Room.png", near: "Level art");
            // s2: "Under Navigator buttons, click + Add navigator button and set its Target to your other place, or any
            //      place the player should go back to."
            ui.Click("+ Add navigator button", near: "Navigator buttons");
            ui.Pick("Target", where, near: "Navigator buttons");
            // s3: "On the VFX tab, click + Effect, name it "Welcome Sparkle" and choose the template Sparkle ring."
            ui.Tab("VFX");
            ui.Click("+ Effect");
            NameIt(ui, "Welcome Sparkle");
            ui.Pick("Template", "Sparkle ring");
            _out.WriteLine("template: " + ui.ValueOf("Template"));
            // s4: "On the Map Buttons tab, click + Map button, set Target place to Back room and write its Label. Then add
            //      the condition QuestState: Tidy the gallery, Dust the vase., completed."
            ui.Tab("Map Buttons");
            ui.Click("+ Map button");
            ui.Pick("Target place", "Back room");
            ui.Type("Label", "Back room");
            ui.Click("+ Add condition");
            ui.Pick("Type", "QuestState");
            ui.Pick("Quest", "Tidy the gallery");
            ui.Pick("Task", "Dust the vase.");
            ui.Pick("Is", "completed");
            // s5: "Back on the Places tab, choose Back room. Under On enter, click + Add action group, then + Add action:
            //      PlayEffect, Effect WelcomeSparkle."
            ui.Tab("Places");
            ui.Select("Back room");
            ui.Click("+ Add action group", near: "On enter");
            ui.Click("+ Add action", near: "On enter");
            ui.Pick("Type", "PlayEffect", near: "On enter");
            PickNamed(ui, "Effect", "WelcomeSparkle", "On enter", "secretRoom s5");

            // What it promises: a room with a way back, a sparkle on arrival, a map button once the task is done.
            Assert.Equal("self:Gallery", ui.ValueOf("Target", near: "Navigator buttons"));
            Assert.Equal("WelcomeSparkle", ui.ValueOf("Effect", near: "On enter"));
            ui.Tab("Map Buttons");
            Assert.Equal("self:BackRoom", ui.ValueOf("Target place"));
            Assert.Equal("completed", ui.ValueOf("Is"));
            Done(ui);
        });
    }

    /// <summary>"A quest with two tasks", as written - also what the two examples after it start from.</summary>
    private void QuestWithTwoTasks(UiDriver ui, string who, string where, bool log = false)
    {
        // s1: "On the Quests tab, click + Quest and give it the Title "Tidy the gallery"."
        ui.Tab("Quests");
        ui.Click("+ Quest");
        ui.Type("Title", "Tidy the gallery");
        // s2: "Click + Task and write "Dust the vase." in its Text. Click + Task again for "Hang the poster."."
        ui.Click("+ Task");
        ui.Type("Text", "Dust the vase.");
        ui.Click("+ Task");
        ui.Type("Text", "Hang the poster.");
        // s3: "On the first line of one of your conversations (none yet? make one as in A greeting once a day, steps 2
        //      and 3), add an action under Actions on start: Quest, Tidy the gallery, start quest."
        AConversationOfYours(ui, who, where, "twoTaskQuest s3");
        ui.Click("+ Add action", near: "Actions on start");
        ui.Pick("Type", "Quest", near: "Actions on start");
        ui.Pick("Quest", "Tidy the gallery", near: "Actions on start");
        ui.Pick("Do", "start quest", near: "Actions on start");
        // s4: "On that same line, add under Actions on finish: Quest, Tidy the gallery, complete task, Dust the vase."
        ui.Click("+ Add action", near: "Actions on finish");
        ui.Pick("Type", "Quest", near: "Actions on finish");
        ui.Pick("Quest", "Tidy the gallery", near: "Actions on finish");
        ui.Pick("Do", "complete task", near: "Actions on finish");
        ui.Pick("Task", "Dust the vase.", near: "Actions on finish");
        // "On the line after it (click + Sibling if there is none), do the same for Hang the poster."
        ui.Click("+ Sibling");
        ui.Type("Text", "All done!");
        ui.Click("+ Add action", near: "Actions on finish");
        ui.Pick("Type", "Quest", near: "Actions on finish");
        ui.Pick("Quest", "Tidy the gallery", near: "Actions on finish");
        ui.Pick("Do", "complete task", near: "Actions on finish");
        ui.Pick("Task", "Hang the poster.", near: "Actions on finish");
        if (log)
            foreach (var row in ui.Rows()) _out.WriteLine("ROW: " + row);
    }

    /// <summary>"A new name for one of the game's characters".</summary>
    [Fact]
    public void GameCharacter()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            CharacterAndPlace(ui);
            // s1: "On the Characters tab, open THE GAME'S CHARACTERS and choose one - here, the Cop."
            ui.Tab("Characters");
            ui.Open("THE GAME'S CHARACTERS");   // a list heading, shut until clicked: "THE GAME'S CHARACTERS (118)"
            ui.Select("Cop");
            // s2: "In Identity, type the new Character name: "Officer Kane"."
            ui.Type("Character name", "Officer Kane", near: "Identity");

            // What it promises: the Cop under a new name; "Reset character puts them back as they were" (s2.why).
            Assert.Equal("Officer Kane", ui.ValueOf("Character name", near: "Identity"));
            // Right after the rename, without choosing anybody else first (the banner did not show until then).
            Assert.True(ui.SeesButton("Reset character"), "no Reset character after the rename");
            Done(ui);
        });
    }

    /// <summary>"A photo of your own for the Starmaker".</summary>
    [Fact]
    public void StarmakerScene()
    {
        WindowHarness.Run(window =>
        {
            var ui = new UiDriver(window, _out);
            ui.StartNewPack(_dir);
            var (who, where) = CharacterAndPlace(ui);
            // s1: "On the Scenes tab, click + Scene, name it "Beach Photo" and choose its picture under Scene image."
            ui.Tab("Scenes");
            ui.Click("+ Scene");
            NameIt(ui, "Beach Photo");
            ui.Type("Scene image", "TutorialArt/Scenes/Dummy/DummyScene01.png");   // the Sprite path box under that heading
            // s2: "Tick Treat as a Starmaker photo and set Sound to Flash."
            ui.Tick("Treat as a Starmaker photo");
            ui.Pick("Sound", "Flash");
            Assert.Equal("ticked", ui.ValueOf("Treat as a Starmaker photo"));
            // s3: "On a line of yours (none yet? make a conversation as in A greeting once a day, steps 2 and 3), add
            //      under Actions on start: SetGameObjectActive, Category Scene, Target BeachPhoto, Switch Activate."
            AConversationOfYours(ui, who, where, "starmakerScene s3");
            ui.Click("+ Add action", near: "Actions on start");
            ui.Pick("Type", "SetGameObjectActive", near: "Actions on start");
            ui.Pick("Category", "Scene", near: "Actions on start");
            PickNamed(ui, "Target", "BeachPhoto", "Actions on start", "starmakerScene s3");
            ui.Pick("Switch", "Activate", near: "Actions on start");
            // "On the line after it (click + Sibling if there is none), add DeactivateAllScenes under Actions on start."
            ui.Click("+ Sibling");
            ui.Type("Text", "That's enough photos.");
            ui.Click("+ Add action", near: "Actions on start");
            ui.Pick("Type", "DeactivateAllScenes", near: "Actions on start");

            // What it promises: the photo shown from one line and put away from the next.
            Assert.Equal("DeactivateAllScenes", ui.ValueOf("Type", near: "Actions on start"));
            ui.Select("Hello.");
            Assert.Equal("Scene", ui.ValueOf("Category", near: "Actions on start"));
            Assert.Equal("BeachPhoto", ui.ValueOf("Target", near: "Actions on start"));
            Assert.Equal("Activate", ui.ValueOf("Switch", near: "Actions on start"));
            Done(ui);
        });
    }

    // ── Helpers: what a reader does when the words and the screen part ───────────────────────────

    /// <summary>"name it ...": the guide names no box, so the reader types into the tab's name box, called Display
    /// name on most tabs and Name on others.</summary>
    private static void NameIt(UiDriver ui, string name)
    {
        try { ui.Type("Display name", name); }
        catch (UiDriverMiss) { ui.Type("Name", name); }
    }

    /// <summary>"On the Places tab, choose your place": the list under Your places names it by its runtime name.
    /// Named by the reader ("Before you start" has them type Gallery), that is the name they gave.</summary>
    private void ChooseYourPlaceInTheList(UiDriver ui, string where)
        => OrElse("the Your places list names the place by its runtime name, not its Display name",
                  () => ui.Select(where),
                  () => ui.Select("place 1"));

    /// <summary>"In one of your conversations" / "On a line of yours": "Before you start" makes none, so the reader
    /// makes one as A greeting once a day shows (steps 2 and 3): a conversation for the place, and one line.</summary>
    private void AConversationOfYours(UiDriver ui, string who, string where, string example)
    {
        // "(none yet? make one as in A greeting once a day, steps 2 and 3)"
        ConversationForYourPlace(ui, "Chat", where);
        ui.Click("+ New Node");
        ChooseYourCharacter(ui, who);
        ui.Type("Text", "Hello.");
    }

    /// <summary>"Make a conversation "..." for your place, as in A greeting once a day, step 2": + Dialogue, its
    /// name, and the Required row's Level set to your place.</summary>
    private void ConversationForYourPlace(UiDriver ui, string name, string where)
    {
        ui.Tab("Dialogues");
        ui.Click("+ Dialogue");
        ui.Type("Display name", name);
        ChooseYourPlace(ui, where);
    }

    /// <summary>The example's last look: Validate has nothing to say, and no step needed a reader's workaround.</summary>
    private void Done(UiDriver ui)
    {
        var issues = ui.Issues();
        foreach (var issue in issues) _out.WriteLine("ISSUE: " + issue);
        Assert.True(issues.Count == 0, "Validate: " + string.Join(" | ", issues));
        Assert.True(_findings.Count == 0, "steps the screen did not answer as written:" + NL + string.Join(NL, _findings));
    }

    private static readonly string NL = Environment.NewLine;

    /// <summary>"Choose your character as the Actor": the list names characters by their runtime name
    /// ("NewCharacter"), not the Character name the reader typed or saw ("New Character").</summary>
    private void ChooseYourCharacter(UiDriver ui, string who)
        => OrElse("the Actor list shows the character's runtime name, not its Character name",
                  () => ui.Pick("Actor", who),
                  () => ui.Pick("Actor", who.Replace(" ", "")));

    /// <summary>A thing of the pack picked by the name the guide gives it ("Music key Gallery jazz"), where the list
    /// shows the runtime name made from it ("GalleryJazz"): the reader recognises their name without its spaces.</summary>
    private void PickNamed(UiDriver ui, string label, string name, string? near, string example)
        => OrElse($"{example}: the {label} list shows the runtime name \"{name.Replace(" ", "")}\", the guide says \"{name}\"",
                  () => ui.Pick(label, name, near),
                  () => ui.Pick(label, name.Replace(" ", ""), near));

    /// <summary>The guide's own action first; when the screen does not answer it, a FINDING in the log and what a
    /// reader looking at the screen would do instead.</summary>
    private void OrElse(string finding, Action asWritten, Action whatAReaderDoes)
    {
        try { asWritten(); }
        catch (UiDriverMiss miss)
        {
            _out.WriteLine("FINDING: " + finding + "\n  " + miss.Message.Replace("\n", "\n  "));
            _findings.Add(finding);
            whatAReaderDoes();
        }
    }

    /// <summary>"Under Start conditions, the first row reads Required — this dialogue only starts in this level:
    /// choose your place in it." Its Level list opens on the game's Beach alone (the box holds vanilla:14_Beach and
    /// the list narrows to what the box says): a reader types their place's name to find it.</summary>
    private void ChooseYourPlace(UiDriver ui, string where)
        => OrElse("the Required row's Level list, opened, offers only the Beach: your place is not in it until you type",
                  () => ui.Pick("Level", where, near: "Start conditions"),
                  () => ui.SearchAndPick("Level", where, where, near: "Start conditions"));

    // ── What every example starts from ─────────────────────────────────────────────────────────────

    /// <summary>"Before you start", step 1, as written: a character and a place. Returns their names - what "your
    /// character" and "your place" mean in the steps.</summary>
    private (string character, string place) CharacterAndPlace(UiDriver ui)
    {
        // "To make them now: on the Characters tab, click + Voice (a speaking part that needs no art), type a
        //  Character name such as Mia and choose her Pronouns."
        ui.Tab("Characters");
        ui.Click("+ Voice");
        ui.Type("Character name", "Mia");
        ui.Pick("Pronouns", "Female");
        // "On the Places tab, click + Place, type a Display name such as Gallery, and under Level art give it a Back
        //  sprite and a Front sprite: 2048x1136 PNG pictures inside your pack's folder (the tutorials copy practice
        //  ones into its TutorialArt folder)."
        ui.Tab("Places");
        ui.Click("+ Place");
        ui.Type("Display name", "Gallery");
        ui.Type("Back sprite", "TutorialArt/Locations/RoomB.png", near: "Level art");
        ui.Type("Front sprite", "TutorialArt/Locations/Room.png", near: "Level art");
        return ("Mia", "Gallery");
    }
}
