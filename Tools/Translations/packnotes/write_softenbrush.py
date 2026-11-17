"""The mask painter's soften brush (1.9.0, asked by ArcoFrio) and the layer button renamed "Soften all",
in the 8 shipped languages. Writes Tools/Translations/packnotes/<code>/softenbrush.txt for
SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

KEYS = ["maskStudio.soften", "maskStudio.soften.tip", "maskStudio.status.soften", "maskStudio.toolSoften",
        "maskStudio.toolSoften.tip", "maskStudio.keys.soften", "docs.maskPainter.brushEraserSizeHardness",
        "docs.maskPainter.invertSoften.term", "docs.maskPainter.invertSoften", "docs.maskPainter.brushEraser.term",
        "docs.maskPainter.brushEraser"]

T = {
"de": [
"Alles weicher",
"Verwischt die ganze Ebene, die gemalt wird, ein wenig und glättet harte Kanten, an denen der Effekt reißen würde. Für mehr noch einmal drücken; der Weichzeichner tut dasselbe nur dort, wo du malst.",
"Weichzeichner auf {layer}",
"Weich (S)",
"Ein Pinsel, der glättet, worüber er fährt, statt zu malen: Jeder Strich zieht die Ebene so weit zu einer verwischten Fassung ihrer selbst, wie die Deckkraft sagt. Für mehr noch einmal darübergehen.",
"S  Weichzeichner",
"Ein Pinsel, ein Radierer und ein Weichzeichner, mit Größe, Härte und Deckkraft. Der Weichzeichner glättet, worüber er fährt, statt zu malen, so weit, wie die Deckkraft sagt. Zoom ändert, wie nah du arbeitest, nicht, was gemalt wird.",
"Umkehren / Alles weicher",
"Umkehren tauscht stark und schwach auf der Ebene, auf der du bist. Alles weicher verwischt sie ganz ein wenig und glättet die harten Kanten, an denen ein Effekt reißen würde; für mehr noch einmal drücken. Beides lässt sich rückgängig machen.",
"B / E / S",
"Pinsel, Radierer oder Weichzeichner.",
],
"es": [
"Suavizar todo",
"Desenfoca un poco toda la capa que se está pintando y suaviza los bordes duros donde el efecto se rasgaría. Púlsalo otra vez para más; el pincel Suavizar hace lo mismo solo donde pintas.",
"Pincel suavizador en {layer}",
"Suavizar (S)",
"Un pincel que suaviza por donde pasa en lugar de pintar: cada trazo acerca la capa a una versión desenfocada de sí misma, tanto como diga la opacidad. Pasa otra vez por un sitio para más.",
"S  pincel suavizador",
"Un pincel, una goma y un pincel suavizador, con Tamaño, Dureza y Opacidad. El pincel suavizador suaviza por donde pasa en lugar de pintar, tanto como diga la opacidad. El zoom cambia lo cerca que trabajas, no lo que se pinta.",
"Invertir / Suavizar todo",
"Invertir intercambia fuerte y débil en la capa en la que estás. Suavizar todo la desenfoca un poco entera y suaviza los bordes duros donde un efecto se rasgaría; púlsalo otra vez para más. Ambos se pueden deshacer.",
"B / E / S",
"Pincel, goma o pincel suavizador.",
],
"fr": [
"Tout adoucir",
"Floute un peu tout le calque en cours et lisse les bords durs où l'effet se déchirerait. Appuyez encore pour davantage ; le pinceau Adoucir fait de même seulement là où vous peignez.",
"Pinceau adoucissant sur {layer}",
"Adoucir (S)",
"Un pinceau qui lisse ce qu'il traverse au lieu de peindre : chaque trait rapproche le calque d'une version floutée de lui-même, autant que le dit l'opacité. Repassez au même endroit pour davantage.",
"S  pinceau adoucissant",
"Un pinceau, une gomme et un pinceau adoucissant, avec Taille, Dureté et Opacité. Le pinceau adoucissant lisse ce qu'il traverse au lieu de peindre, autant que le dit l'opacité. Le zoom change la distance à laquelle vous travaillez, pas ce qui est peint.",
"Inverser / Tout adoucir",
"Inverser échange fort et faible sur le calque où vous êtes. Tout adoucir le floute un peu en entier et lisse les bords durs où un effet se déchirerait ; appuyez encore pour davantage. Les deux s'annulent.",
"B / E / S",
"Pinceau, gomme ou pinceau adoucissant.",
],
"ja": [
"全体をぼかす",
"塗っているレイヤー全体を少しぼかし、効果が裂けそうな硬い境目をなめらかにします。もう一度押すとさらにぼけます。ぼかしブラシは、塗った所だけに同じことをします。",
"ぼかしブラシ: {layer}",
"ぼかし (S)",
"塗る代わりに、通った所をなめらかにするブラシです。ストロークごとに、不透明度の分だけレイヤーをぼかした自分自身に近づけます。同じ所をもう一度なぞるとさらにぼけます。",
"S  ぼかしブラシ",
"ブラシ、消しゴム、ぼかしブラシで、サイズ、硬さ、不透明度を設定できます。ぼかしブラシは塗る代わりに、通った所を不透明度の分だけなめらかにします。ズームは作業の近さを変えるだけで、塗られるものは変えません。",
"反転 / 全体をぼかす",
"反転は、今いるレイヤーの強弱を入れ替えます。全体をぼかすはレイヤー全体を少しぼかし、効果が裂けそうな硬い境目をなめらかにします。もう一度押すとさらにぼけます。どちらも元に戻せます。",
"B / E / S",
"ブラシ、消しゴム、またはぼかしブラシ。",
],
"ko": [
"전체 부드럽게",
"칠하고 있는 레이어 전체를 살짝 흐리게 해, 효과가 찢어질 만한 딱딱한 경계를 매끄럽게 합니다. 다시 누르면 더 부드러워집니다. 부드럽게 브러시는 칠한 곳에만 같은 일을 합니다.",
"{layer}에 부드럽게 브러시",
"부드럽게 (S)",
"칠하는 대신 지나간 곳을 매끄럽게 하는 브러시입니다. 획마다 불투명도만큼 레이어를 흐리게 한 자신에 가깝게 당깁니다. 같은 곳을 다시 지나가면 더 부드러워집니다.",
"S  부드럽게 브러시",
"브러시, 지우개, 부드럽게 브러시가 있고 크기, 경도, 불투명도를 정합니다. 부드럽게 브러시는 칠하는 대신 지나간 곳을 불투명도만큼 매끄럽게 합니다. 확대/축소는 작업하는 거리만 바꾸고, 칠해지는 것은 바꾸지 않습니다.",
"반전 / 전체 부드럽게",
"반전은 지금 있는 레이어의 강약을 맞바꿉니다. 전체 부드럽게는 레이어 전체를 살짝 흐리게 해, 효과가 찢어질 만한 딱딱한 경계를 매끄럽게 합니다. 다시 누르면 더 부드러워집니다. 둘 다 되돌릴 수 있습니다.",
"B / E / S",
"브러시, 지우개 또는 부드럽게 브러시.",
],
"pt-BR": [
"Suavizar tudo",
"Desfoca um pouco a camada inteira sendo pintada, suavizando bordas duras onde o efeito rasgaria. Aperte de novo para mais; o pincel Suavizar faz o mesmo só onde você pinta.",
"Pincel suavizador em {layer}",
"Suavizar (S)",
"Um pincel que suaviza por onde passa em vez de pintar: cada traço puxa a camada para uma versão desfocada dela mesma, tanto quanto a opacidade diz. Passe de novo num lugar para mais.",
"S  pincel suavizador",
"Um pincel, uma borracha e um pincel suavizador, com Tamanho, Dureza e Opacidade. O pincel suavizador suaviza por onde passa em vez de pintar, tanto quanto a opacidade diz. O zoom muda o quão perto você trabalha, não o que é pintado.",
"Inverter / Suavizar tudo",
"Inverter troca forte e fraco na camada em que você está. Suavizar tudo a desfoca um pouco inteira, suavizando as bordas duras onde um efeito rasgaria; aperte de novo para mais. Os dois podem ser desfeitos.",
"B / E / S",
"Pincel, borracha ou pincel suavizador.",
],
"ru": [
"Смягчить всё",
"Немного размывает весь рисуемый слой, сглаживая резкие края, где эффект рвался бы. Нажмите ещё раз, чтобы сильнее; кисть «Смягчение» делает то же только там, где вы рисуете.",
"Кисть смягчения на слое «{layer}»",
"Смягчение (S)",
"Кисть, которая сглаживает то, по чему проходит, вместо рисования: каждый мазок приближает слой к его размытой версии настолько, насколько велика непрозрачность. Пройдите по месту ещё раз, чтобы сильнее.",
"S  кисть смягчения",
"Кисть, ластик и кисть смягчения, с размером, жёсткостью и непрозрачностью. Кисть смягчения сглаживает то, по чему проходит, вместо рисования, настолько, насколько велика непрозрачность. Масштаб меняет то, насколько близко вы работаете, а не то, что рисуется.",
"Инвертировать / Смягчить всё",
"Инвертировать меняет сильное и слабое местами на слое, где вы находитесь. Смягчить всё немного размывает его целиком, сглаживая резкие края, где эффект рвался бы; нажмите ещё раз, чтобы сильнее. Оба действия можно отменить.",
"B / E / S",
"Кисть, ластик или кисть смягчения.",
],
"zh-Hans": [
"全部柔化",
"把正在绘制的整个图层稍微模糊，抚平效果可能撕裂的生硬边缘。再按一次会更柔和；柔化画笔只在你画过的地方做同样的事。",
"柔化画笔：{layer}",
"柔化 (S)",
"一支不上色、而是把经过之处抚平的画笔：每一笔都按不透明度把图层拉向它自己的模糊版本。在同一处再画一次会更柔和。",
"S  柔化画笔",
"画笔、橡皮擦和柔化画笔，可调大小、硬度和不透明度。柔化画笔不上色，而是按不透明度把经过之处抚平。缩放只改变你工作的远近，不改变画上去的内容。",
"反相 / 全部柔化",
"反相会在你所在的图层上互换强弱。全部柔化会把整个图层稍微模糊，抚平效果可能撕裂的生硬边缘；再按一次会更柔和。两者都可以撤销。",
"B / E / S",
"画笔、橡皮擦或柔化画笔。",
],
}

REWORDED = {"maskStudio.soften.tip", "maskStudio.toolSoften.tip", "docs.maskPainter.brushEraserSizeHardness"}

for code, texts in T.items():
    assert len(texts) == len(KEYS), (code, len(texts))
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "softenbrush.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in zip(KEYS, texts):
            if key in REWORDED: continue   # reworded in 1.9.1: written by write_softenedge.py
            f.write(key + " = " + text + "\n")
    print(code, len(texts))
