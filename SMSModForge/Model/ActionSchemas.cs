using System;
using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Per-action-type parameter schemas consumed by the editor's row renderer.
/// Each key in <see cref="ByType"/> is a value from <see cref="NodeActionTypes"/>;
/// the corresponding <see cref="ParamSchema"/> array enumerates the params the
/// editor should expose (in display order).
/// <para/>
/// The runtime plugin reads params by string key from <see cref="NodeActionDef.Params"/>
/// — the schemas here only affect how the editor renders the row, never the on-disk
/// JSON. An action type with no schema (or no params) renders a bare type combo and
/// nothing else. An unknown action type falls back to <see cref="Empty"/>.
/// </summary>
public static class ActionSchemas
{
    /// <summary>Sentinel returned by <see cref="For"/> when an action type has no
    /// schema entry — keeps callers from having to null-check.</summary>
    public static readonly ParamSchema[] Empty = Array.Empty<ParamSchema>();

    /// <summary>Lookup from <see cref="NodeActionDef.Type"/> to its schema.</summary>
    public static readonly Dictionary<string, ParamSchema[]> ByType = new()
    {
        // ── Variable manipulation ──────────────────────────────────────
        [NodeActionTypes.SetVariable] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "",
                "action.setVariable.name.tip"),
            new ParamSchema("value", "param.label.value", ParamType.String, "",
                "action.setVariable.value.tip",
                emptyIsAValue: true),
        },
        [NodeActionTypes.IncrementVariable] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "",
                "action.incrementVariable.name.tip"),
            new ParamSchema("delta", "param.label.delta", ParamType.Float, "1",
                "action.incrementVariable.delta.tip"),
        },

        // ── Actor / bust visuals ───────────────────────────────────────
        [NodeActionTypes.CharacterFocus] = new[]
        {
            new ParamSchema("focused", "param.label.focused", ParamType.Bool, "true",
                "action.setSpriteFocus.focused.tip"),
        },
        [NodeActionTypes.LeaveBust] = new[]
        {
            new ParamSchema("actor", "param.label.actor", ParamType.ActorRef, "",
                "action.leaveBust.actor.tip"),
        },

        // ── Scene-graph + signal primitives ────────────────────────────
        [NodeActionTypes.SetGameObjectActive] = new[]
        {
            new ParamSchema("path", "param.label.goPath", ParamType.GameObjectPath, "",
                "action.setGameObjectActive.path.tip"),
            new ParamSchema("active", "param.label.active", ParamType.String, "true",
                "action.setGameObjectActive.active.tip"),
        },
        // Targeting params (kind/target/overlayLevel) are supplied by the
        // shared category row, same as SetGameObjectActive — only what is
        // unique to this action is declared here.
        [NodeActionTypes.SetSprite] = new[]
        {
            new ParamSchema("sprite", "param.label.sprite", ParamType.SpriteRef, "",
                "action.setSprite.sprite.tip"),
            new ParamSchema("mask", "param.label.maskOptional", ParamType.SpriteRef, "",
                "action.setSprite.mask.tip"),
        },
        [NodeActionTypes.EmitSignal] = new[]
        {
            new ParamSchema("signal", "param.label.signal", ParamType.SignalRef, "",
                "action.emitSignal.signal.tip"),
            new ParamSchema("seconds", "param.label.delayS", ParamType.Float, "0",
                "action.emitSignal.seconds.tip"),
        },
        [NodeActionTypes.TransitionLevels] = new[]
        {
            new ParamSchema("fromLevel", "param.label.fromLevel", ParamType.LevelRef, "",
                "action.transitionLevels.fromLevel.tip"),
            new ParamSchema("toLevel", "param.label.toLevel", ParamType.LevelRef, "",
                "action.transitionLevels.toLevel.tip"),
            new ParamSchema("signal", "param.label.doneSignal", ParamType.SignalRef, "",
                "action.transitionLevels.signal.tip"),
            new ParamSchema("seconds", "param.label.totalTimeS", ParamType.Float, "1.5",
                "action.transitionLevels.seconds.tip"),
        },
        // Target is edited via the shared Category + Target row (see
        // NodeActionViewModel.IsGoCategoryFamily); only the action-specific
        // params live in the schema now.
        [NodeActionTypes.FadeSprite] = new[]
        {
            new ParamSchema("to", "param.label.targetAlpha", ParamType.Float, "1",
                "action.fadeSprite.to.tip"),
            new ParamSchema("seconds", "param.label.durationS", ParamType.Float, "0.5",
                "action.fadeSprite.seconds.tip"),
        },
        [NodeActionTypes.SetComponentProperty] = new[]
        {
            new ParamSchema("component", "param.label.component", ParamType.Choice, "CanvasGroup",
                "action.setComponentProperty.component.tip",
                fixedOptions: new[] { "CanvasGroup" }),
            new ParamSchema("property", "param.label.property", ParamType.Choice, "alpha",
                "action.setComponentProperty.property.tip",
                fixedOptions: new[] { "alpha", "interactable", "blocksRaycasts" }),
            new ParamSchema("value", "param.label.value", ParamType.String, "1",
                "action.setComponentProperty.value.tip",
                emptyIsAValue: true),
            new ParamSchema("seconds", "param.label.durationS", ParamType.Float, "0",
                "action.setComponentProperty.seconds.tip"),
        },

        [NodeActionTypes.MoveGameObject] = new[]
        {
            new ParamSchema("home", "param.label.returnToOriginal", ParamType.Bool, "false",
                "action.moveGameObject.home.tip"),
            new ParamSchema("relative", "param.label.relative", ParamType.Bool, "false",
                "action.moveGameObject.relative.tip"),
            new ParamSchema("x", "param.label.x", ParamType.Float, "0", "action.moveGameObject.x.tip"),
            new ParamSchema("y", "param.label.y", ParamType.Float, "0", "action.moveGameObject.y.tip"),
            new ParamSchema("seconds", "param.label.durationS", ParamType.Float, "1",
                "action.moveGameObject.seconds.tip"),
        
        },
        [NodeActionTypes.SpinGameObject] = new[]
        {
            new ParamSchema("speed", "param.label.speedS", ParamType.Float, "1",
                "action.spinGameObject.speed.tip"),
            new ParamSchema("enable", "param.label.spinning", ParamType.Bool, "true",
                "action.spinGameObject.enable.tip"),
        },

        // ── Audio ──────────────────────────────────────────────────────
        [NodeActionTypes.SwitchMusic] = new[]
        {
            new ParamSchema("music", "param.label.musicKey", ParamType.MusicRef, "",
                "action.switchMusic.music.tip"),
        },
        [NodeActionTypes.PlaySFX] = new[]
        {
            new ParamSchema("clip", "param.label.clip", ParamType.SfxRef, "",
                "action.playSFX.clip.tip"),
            new ParamSchema("volume", "param.label.volume", ParamType.Float, "1",
                "action.playSFX.volume.tip"),
            new ParamSchema("delay", "param.label.delayS", ParamType.Float, "0",
                "action.playSFX.delay.tip"),
        },

        // ── Flow control ───────────────────────────────────────────────
        // DeactivateAllScenes / ClearList (last has a single
        // list param). DeactivateAllScenes intentionally renders
        // no params.
        [NodeActionTypes.Wait] = new[]
        {
            new ParamSchema("seconds", "param.label.seconds", ParamType.Float, "0.5",
                "action.wait.seconds.tip"),
        },
        [NodeActionTypes.Transitions] = new[]
        {
            new ParamSchema(Shared.Transitions.StyleParam, "param.label.transition", ParamType.Choice,
                Shared.Transitions.FadeToBlack, "action.transitions.style.tip",
                fixedOptions: Shared.Transitions.Styles),
            new ParamSchema(Shared.Transitions.SecondsParam, "param.label.onScreenS", ParamType.Float,
                "2", "action.transitions.seconds.tip",
                shownWhen: Shared.Transitions.StyleParam, shownWhenValues: Shared.Transitions.Timed),
            new ParamSchema(Shared.Transitions.TextParam, "param.label.screenText", ParamType.String,
                Shared.Transitions.DefaultText, "action.transitions.text.tip",
                shownWhen: Shared.Transitions.StyleParam, shownWhenValues: new[] { Shared.Transitions.TextScreen }),
        },

        // ── List + variable helpers ────────────────────────────────────
        [NodeActionTypes.PickRandomFromList] = new[]
        {
            new ParamSchema("source", "param.label.source", ParamType.String, "",
                "action.pickRandomFromList.source.tip"),
            new ParamSchema("excluding", "param.label.excluding", ParamType.String, "",
                "action.pickRandomFromList.excluding.tip"),
            new ParamSchema("target", "param.label.target", ParamType.PackVarRef, "",
                "action.pickRandomFromList.target.tip"),
            new ParamSchema("fallback", "param.label.ifNoneLeft", ParamType.String, "",
                "action.pickRandomFromList.fallback.tip"),
        },
        [NodeActionTypes.AddToList] = new[]
        {
            new ParamSchema("list", "param.label.list", ParamType.ListVarRef, "",
                "action.addToList.list.tip"),
            new ParamSchema("value", "param.label.value", ParamType.String, "",
                "action.addToList.value.tip"),
            new ParamSchema("unique", "param.label.onlyIfAbsent", ParamType.Bool, "false",
                "action.addToList.unique.tip"),
        },
        [NodeActionTypes.RemoveFromList] = new[]
        {
            new ParamSchema("list", "param.label.list", ParamType.ListVarRef, "", ""),
            new ParamSchema("value", "param.label.value", ParamType.String, "",
                "action.removeFromList.value.tip"),
        },
        [NodeActionTypes.ClearList] = new[]
        {
            new ParamSchema("list", "param.label.list", ParamType.ListVarRef, "",
                "action.clearList.list.tip"),
        },
        // DiceRoll renders its own branch editor (chance + nested action per
        // branch) instead of schema-driven param rows.
        [NodeActionTypes.DiceRoll] = Empty,

        [NodeActionTypes.CountList] = new[]
        {
            new ParamSchema("fromList", "param.label.fromList", ParamType.ListVarRef, "",
                "action.countList.fromList.tip"),
            new ParamSchema("name", "param.label.target", ParamType.PackVarRef, "",
                "action.countList.name.tip"),
        },

        // ── Weather ────────────────────────────────────────────────────
        [NodeActionTypes.SetWeather] = new[]
        {
            new ParamSchema("weather", "param.label.weather", ParamType.Choice, "Rain",
                "action.setWeather.weather.tip",
                new[] { "Rain", "Snow", "Clear" }),
        },

        // ── Scenes ─────────────────────────────────────────────────────
        [NodeActionTypes.ActivateScene] = new[]
        {
            new ParamSchema("scene", "param.label.scene", ParamType.SceneRef, "",
                "action.activateScene.scene.tip"),
        },
        [NodeActionTypes.DeactivateAllScenes] = Empty,
        // No params: it's a hand-off, not a setting. See NodeActionTypes.LeaveUiFaded.
        [NodeActionTypes.LeaveUiFaded] = Empty,
    };

    /// <summary>Returns the schema for <paramref name="actionType"/>, or
    /// <see cref="Empty"/> when the type is unknown or has no params.</summary>
    public static ParamSchema[] For(string actionType)
        => actionType != null && ByType.TryGetValue(actionType, out var schema)
            ? schema
            : Empty;
}
