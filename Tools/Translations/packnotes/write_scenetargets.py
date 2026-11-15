"""A Scene target can be one of the game's own scenes too (1.9.0), so the warning about one that names
neither says both. In the 8 shipped languages. Writes Tools/Translations/packnotes/<code>/scenetargets.txt
for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

K = "validation.action.unknownScene"
N = "note.kind.scene"

T = {
"de": {N: "Eine der Szenen dieses Pakets oder eine des Spiels", K: "„{name}“ ist weder eine der Szenen dieses Pakets noch eine des Spiels. Wähle die Szene erneut aus der Liste."},
"es": {N: "Una de las escenas de este pack, o una del juego", K: "«{name}» no es una de las escenas de este pack ni una del juego. Vuelve a elegir la escena de la lista."},
"fr": {N: "Une des scènes de ce pack, ou une de celles du jeu", K: "« {name} » n'est ni l'une des scènes de ce pack, ni l'une de celles du jeu. Choisissez de nouveau la scène dans la liste."},
"ja": {N: "このパックのシーンの一つ、またはゲームのシーンの一つ", K: "「{name}」はこのパックのシーンでも、ゲームのシーンでもありません。一覧からシーンを選び直してください。"},
"ko": {N: "이 팩의 씬 중 하나, 또는 게임의 씬 중 하나", K: "'{name}'은(는) 이 팩의 씬도, 게임의 씬도 아닙니다. 목록에서 씬을 다시 고르세요."},
"pt-BR": {N: "Uma das cenas deste pack, ou uma do jogo", K: "\"{name}\" não é uma das cenas deste pack nem uma das do jogo. Escolha a cena de novo na lista."},
"ru": {N: "Одна из сцен этого пака или одна из сцен игры", K: "«{name}» — не сцена этого пака и не сцена игры. Выберите сцену из списка заново."},
"zh-Hans": {N: "本模组包的某个场景，或游戏的某个场景", K: "「{name}」既不是这个模组包里的场景，也不是游戏的场景。请从列表中重新选择场景。"},
}

for code, rows in T.items():
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "scenetargets.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in rows.items():
            f.write(key + " = " + text + "\n")
    print(code, len(rows))
