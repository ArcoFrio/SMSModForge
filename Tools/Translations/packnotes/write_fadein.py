"""The bust preview's Fade in toggle (1.9.1, asked by ArcoFrio: "the Bust fade-in for characters, with a toggle
below as well that defaults to true"), in the 8 shipped languages. Writes
Tools/Translations/packnotes/<code>/fadein.txt for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

KEYS = ["characters.fadeInToggle", "characters.fadeInToggle.tip"]

T = {
"de": [
"Einblenden",
"Die Büste so erscheinen lassen, wie das Spiel sie zeigt: Eine dunkle Silhouette blendet über eine Sekunde ein, dann blendet die Figur aus ihr über drei Sekunden ein. Spielt jedes Mal, wenn ein Outfit gezeigt wird, und wenn dies angehakt wird. Nur Vorschau.",
],
"es": [
"Aparición gradual",
"Muestra el busto apareciendo como lo muestra el juego: una silueta oscura aparece durante un segundo y luego el personaje surge de ella durante tres. Se reproduce cada vez que se muestra un atuendo y al marcar esto. Solo vista previa.",
],
"fr": [
"Fondu d'entrée",
"Montrer le buste qui apparaît comme le jeu le montre : une silhouette sombre apparaît en fondu en une seconde, puis le personnage en émerge en trois. Se joue chaque fois qu'une tenue est affichée, et quand ceci est coché. Aperçu seulement.",
],
"ja": [
"フェードイン",
"ゲームと同じようにバストが現れる様子を表示します。暗いシルエットが 1 秒かけて現れ、そこから 3 秒かけてキャラクターが現れます。衣装を表示するたびと、これにチェックを入れたときに再生されます。プレビューのみ。",
],
"ko": [
"페이드 인",
"게임에서처럼 흉상이 나타나는 모습을 보여 줍니다. 어두운 실루엣이 1초에 걸쳐 나타난 뒤, 그 실루엣에서 캐릭터가 3초에 걸쳐 나타납니다. 의상을 보여 줄 때마다, 그리고 이것을 체크할 때 재생됩니다. 미리보기 전용.",
],
"pt-BR": [
"Aparecer aos poucos",
"Mostra o busto aparecendo como o jogo mostra: uma silhueta escura aparece ao longo de um segundo, e então o personagem surge dela ao longo de três. Toca toda vez que um traje é mostrado e quando isto é marcado. Só pré-visualização.",
],
"ru": [
"Плавное появление",
"Показывать появление бюста так же, как в игре: тёмный силуэт проявляется за секунду, затем из него за три секунды проявляется персонаж. Воспроизводится каждый раз, когда показывается наряд, и при включении этого флажка. Только предпросмотр.",
],
"zh-Hans": [
"淡入",
"按游戏中的方式显示半身像出现：先用一秒钟淡入一个深色剪影，再用三秒钟让角色从剪影中显现出来。每次显示一套服装时、以及勾选此项时都会播放。仅用于预览。",
],
}

for code, texts in T.items():
    assert len(texts) == len(KEYS), (code, len(texts))
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "fadein.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in zip(KEYS, texts):
            f.write(key + " = " + text + "\n")
    print(code, len(texts))
