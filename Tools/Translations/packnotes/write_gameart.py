"""Why the game's art is drawn at a lower resolution than in the game (1.9.0): one note each under
the Scenes, Characters and Places previews while they show the game's own. In the 8 shipped
languages. Writes Tools/Translations/packnotes/<code>/gameart.txt for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

S = "preview.gameArt.scene"
B = "preview.gameArt.bust"
L = "preview.gameArt.level"

T = {
"de": {
S: "Das Bild des Spiels ist eine kleine Kopie, 102 Pixel pro Seite statt der 256 im Spiel, mit Absicht: Der Editor hat verkleinerte Kopien der Grafiken des Spiels dabei, damit sein Download klein bleibt und er die Bilder des Spiels nicht weitergibt. Deine eigenen Bilder werden so gezeigt, wie sie sind.",
B: "Die Büste des Spiels wird aus einer Kopie mit vier Fünfteln der Auflösung des Spiels gezeichnet, mit Absicht: Der Editor hat verkleinerte Kopien der Grafiken des Spiels dabei, damit sein Download klein bleibt und er die Bilder des Spiels nicht weitergibt. Deine eigenen Grafiken werden so gezeigt, wie sie sind.",
L: "Das Level des Spiels wird aus einer Kopie mit einem Viertel der Auflösung des Spiels gezeichnet, mit Absicht: Der Editor hat verkleinerte Kopien der Grafiken des Spiels dabei, damit sein Download klein bleibt und er die Bilder des Spiels nicht weitergibt. Deine eigenen Grafiken werden so gezeigt, wie sie sind.",
},
"es": {
S: "La imagen del juego es una copia pequeña, de 102 píxeles por lado frente a los 256 del juego, a propósito: el editor lleva copias reducidas del arte del juego para que su descarga sea pequeña y no repartir las imágenes del juego. Tus propias imágenes se muestran tal como son.",
B: "El busto del juego se dibuja a partir de una copia a cuatro quintos de la resolución del juego, a propósito: el editor lleva copias reducidas del arte del juego para que su descarga sea pequeña y no repartir las imágenes del juego. Tu propio arte se muestra tal como es.",
L: "El nivel del juego se dibuja a partir de una copia a un cuarto de la resolución del juego, a propósito: el editor lleva copias reducidas del arte del juego para que su descarga sea pequeña y no repartir las imágenes del juego. Tu propio arte se muestra tal como es.",
},
"fr": {
S: "L'image du jeu est une petite copie, de 102 pixels de côté au lieu des 256 du jeu, exprès : l'éditeur embarque des copies réduites des graphismes du jeu pour que son téléchargement reste léger et pour ne pas distribuer les images du jeu. Vos propres images sont montrées telles qu'elles sont.",
B: "Le buste du jeu est dessiné d'après une copie aux quatre cinquièmes de la résolution du jeu, exprès : l'éditeur embarque des copies réduites des graphismes du jeu pour que son téléchargement reste léger et pour ne pas distribuer les images du jeu. Vos propres graphismes sont montrés tels qu'ils sont.",
L: "Le niveau du jeu est dessiné d'après une copie au quart de la résolution du jeu, exprès : l'éditeur embarque des copies réduites des graphismes du jeu pour que son téléchargement reste léger et pour ne pas distribuer les images du jeu. Vos propres graphismes sont montrés tels qu'ils sont.",
},
"ja": {
S: "ゲームの絵は小さなコピーで、ゲームでは 256 ピクセル四方のところを 102 ピクセル四方にしています。これは意図的なものです。エディターはダウンロードを小さく保ち、ゲームの絵を配らないように、ゲームのアートを縮小したコピーだけを持っています。あなた自身の絵はそのままの大きさで表示されます。",
B: "ゲームの立ち絵は、ゲームの 5 分の 4 の解像度のコピーから描いています。これは意図的なものです。エディターはダウンロードを小さく保ち、ゲームの絵を配らないように、ゲームのアートを縮小したコピーだけを持っています。あなた自身のアートはそのまま表示されます。",
L: "ゲームのレベルは、ゲームの 4 分の 1 の解像度のコピーから描いています。これは意図的なものです。エディターはダウンロードを小さく保ち、ゲームの絵を配らないように、ゲームのアートを縮小したコピーだけを持っています。あなた自身のアートはそのまま表示されます。",
},
"ko": {
S: "게임의 그림은 작은 사본으로, 게임에서는 한 변이 256픽셀인 것을 102픽셀로 줄였습니다. 일부러 그렇게 한 것입니다. 에디터는 다운로드를 작게 유지하고 게임의 그림을 나눠 주지 않도록 게임 아트의 축소된 사본만 가지고 있습니다. 내 그림은 원래 그대로 보입니다.",
B: "게임의 스탠딩은 게임 해상도의 5분의 4인 사본으로 그립니다. 일부러 그렇게 한 것입니다. 에디터는 다운로드를 작게 유지하고 게임의 그림을 나눠 주지 않도록 게임 아트의 축소된 사본만 가지고 있습니다. 내 아트는 원래 그대로 보입니다.",
L: "게임의 레벨은 게임 해상도의 4분의 1인 사본으로 그립니다. 일부러 그렇게 한 것입니다. 에디터는 다운로드를 작게 유지하고 게임의 그림을 나눠 주지 않도록 게임 아트의 축소된 사본만 가지고 있습니다. 내 아트는 원래 그대로 보입니다.",
},
"pt-BR": {
S: "A imagem do jogo é uma cópia pequena, de 102 pixels de lado em vez dos 256 do jogo, de propósito: o editor traz cópias reduzidas da arte do jogo para manter o download pequeno e não distribuir as imagens do jogo. As suas próprias imagens aparecem como são.",
B: "O busto do jogo é desenhado a partir de uma cópia com quatro quintos da resolução do jogo, de propósito: o editor traz cópias reduzidas da arte do jogo para manter o download pequeno e não distribuir as imagens do jogo. A sua própria arte aparece como é.",
L: "O nível do jogo é desenhado a partir de uma cópia com um quarto da resolução do jogo, de propósito: o editor traz cópias reduzidas da arte do jogo para manter o download pequeno e não distribuir as imagens do jogo. A sua própria arte aparece como é.",
},
"ru": {
S: "Картинка игры — это маленькая копия, 102 пикселя по стороне вместо 256 в игре, и так задумано: редактор хранит уменьшенные копии графики игры, чтобы его загрузка была небольшой и чтобы не раздавать картинки игры. Ваши собственные картинки показываются такими, какие они есть.",
B: "Портрет из игры рисуется по копии в четыре пятых разрешения игры, и так задумано: редактор хранит уменьшенные копии графики игры, чтобы его загрузка была небольшой и чтобы не раздавать картинки игры. Ваша собственная графика показывается такой, какая она есть.",
L: "Уровень игры рисуется по копии в четверть разрешения игры, и так задумано: редактор хранит уменьшенные копии графики игры, чтобы его загрузка была небольшой и чтобы не раздавать картинки игры. Ваша собственная графика показывается такой, какая она есть.",
},
"zh-Hans": {
S: "游戏的图片是一份小副本，每边 102 像素，而游戏里是 256 像素，这是有意的：编辑器只带游戏美术的缩小副本，以便让下载保持很小，也不分发游戏的图片。你自己的图片按原样显示。",
B: "游戏的半身像是用游戏分辨率五分之四的副本画出来的，这是有意的：编辑器只带游戏美术的缩小副本，以便让下载保持很小，也不分发游戏的图片。你自己的美术按原样显示。",
L: "游戏的关卡是用游戏分辨率四分之一的副本画出来的，这是有意的：编辑器只带游戏美术的缩小副本，以便让下载保持很小，也不分发游戏的图片。你自己的美术按原样显示。",
},
}

for code, rows in T.items():
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "gameart.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in rows.items():
            f.write(key + " = " + text + "\n")
    print(code, len(rows))
