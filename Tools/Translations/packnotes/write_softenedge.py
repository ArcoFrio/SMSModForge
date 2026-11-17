"""The soften brush's Edge softness (1.9.1, asked by ArcoFrio: "making the edges of the painted areas look
smoother, to a max edge softness allowed in the slider"): the new slider, and the three texts reworded for it,
in the 8 shipped languages. Writes Tools/Translations/packnotes/<code>/softenedge.txt for
SMSMODFORGE_REFRESH_TRANSLATIONS. write_softenbrush.py skips the reworded three."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

KEYS = ["maskStudio.soften.tip", "maskStudio.toolSoften.tip", "maskStudio.softness", "maskStudio.softness.tip",
        "docs.maskPainter.brushEraserSizeHardness"]

T = {
"de": [
"Verwischt die ganze Ebene, die gemalt wird, ein wenig und glättet harte Kanten, an denen der Effekt reißen würde. Für mehr noch einmal drücken. Um nur einige Kanten weicher zu machen, und nie über eine eingestellte Weichheit hinaus, nimm den Weichzeichner.",
"Ein Pinsel, der die Kanten, über die er fährt, glättet, statt zu malen, bis zur für ihn eingestellten Kantenweichheit. Noch einmal über eine Kante zu gehen, macht sie nie weicher: Erhöhe die Kantenweichheit für eine weichere Kante, oder senke sie und geh über die Kante, um sie wieder schärfer zu machen.",
"Kantenweichheit",
"Wie weich der Weichzeichner eine Kante hinterlässt, in Pixeln der Maske: Eine harte Kante, über die er fährt, geht über etwa so viele Pixel von nichts zu voll über. Noch einmal darüberzugehen, macht sie nie weicher.",
"Ein Pinsel, ein Radierer und ein Weichzeichner, mit Größe und Härte. Pinsel und Radierer haben Deckkraft; der Weichzeichner hat an ihrer Stelle die Kantenweichheit: Er glättet die Kanten, über die er fährt, bis zu dieser Weichheit und nie darüber hinaus, wie oft du auch darübergehst. Zoom ändert, wie nah du arbeitest, nicht, was gemalt wird.",
],
"es": [
"Desenfoca un poco toda la capa que se está pintando y suaviza los bordes duros donde el efecto se rasgaría. Púlsalo otra vez para más. Para suavizar solo algunos bordes, y nunca más allá de una suavidad que tú fijes, usa el pincel suavizador.",
"Un pincel que suaviza los bordes por donde pasa en lugar de pintar, hasta la Suavidad del borde que tenga fijada. Pasar otra vez por un borde nunca lo suaviza más: sube la Suavidad del borde para un borde más suave, o bájala y pasa por el borde para volver a definirlo.",
"Suavidad del borde",
"Lo suave que deja un borde el pincel suavizador, en píxeles de la máscara: un borde duro por el que pasa va de nada a lleno a lo largo de aproximadamente este número de píxeles. Pasar otra vez nunca lo lleva más lejos.",
"Un pincel, una goma y un pincel suavizador, con Tamaño y Dureza. El pincel y la goma tienen Opacidad; el pincel suavizador tiene en su lugar Suavidad del borde: suaviza los bordes por donde pasa hasta esa suavidad y nunca más allá, por muchas veces que pases. El zoom cambia lo cerca que trabajas, no lo que se pinta.",
],
"fr": [
"Floute un peu tout le calque en cours et lisse les bords durs où l'effet se déchirerait. Appuyez encore pour davantage. Pour n'adoucir que certains bords, et jamais au-delà d'une douceur que vous réglez, utilisez le pinceau adoucissant.",
"Un pinceau qui lisse les bords qu'il traverse au lieu de peindre, jusqu'à la Douceur des bords réglée pour lui. Repasser sur un bord ne l'adoucit jamais davantage : augmentez la Douceur des bords pour un bord plus doux, ou baissez-la et repassez sur le bord pour le resserrer.",
"Douceur des bords",
"La douceur du bord que laisse le pinceau adoucissant, en pixels du masque : un bord dur qu'il traverse passe de rien à plein sur environ ce nombre de pixels. Repasser dessus ne va jamais plus loin.",
"Un pinceau, une gomme et un pinceau adoucissant, avec Taille et Dureté. Le pinceau et la gomme ont l'Opacité ; le pinceau adoucissant a la Douceur des bords à sa place : il lisse les bords qu'il traverse jusqu'à cette douceur et jamais au-delà, quel que soit le nombre de passages. Le zoom change la distance à laquelle vous travaillez, pas ce qui est peint.",
],
"ja": [
"塗っているレイヤー全体を少しぼかし、効果が裂けそうな硬い境目をなめらかにします。もう一度押すとさらにぼけます。一部の境目だけを、決めたぼかし幅を超えずにぼかすには、ぼかしブラシを使います。",
"塗る代わりに、通った境目を設定した「境目のぼかし幅」までなめらかにするブラシです。同じ境目をもう一度なぞっても、それ以上はぼけません。もっとぼかすには「境目のぼかし幅」を上げ、引き締め直すには下げてから境目をなぞります。",
"境目のぼかし幅",
"ぼかしブラシが境目をどれだけぼかすかを、マスクのピクセル数で表します。通った硬い境目は、およそこのピクセル数をかけて、ゼロから最大まで変わるようになります。もう一度なぞっても、それ以上は進みません。",
"ブラシ、消しゴム、ぼかしブラシで、サイズと硬さを設定できます。ブラシと消しゴムには不透明度があり、ぼかしブラシには代わりに「境目のぼかし幅」があります。ぼかしブラシは通った境目をその幅までなめらかにし、何度なぞってもそれ以上はぼかしません。ズームは作業の近さを変えるだけで、塗られるものは変えません。",
],
"ko": [
"칠하고 있는 레이어 전체를 살짝 흐리게 해, 효과가 찢어질 만한 딱딱한 경계를 매끄럽게 합니다. 다시 누르면 더 부드러워집니다. 일부 경계만, 정한 부드러움을 넘지 않게 부드럽게 하려면 부드럽게 브러시를 쓰세요.",
"칠하는 대신 지나간 경계를 정해 둔 경계 부드러움까지 매끄럽게 하는 브러시입니다. 같은 경계를 다시 지나가도 더 부드러워지지 않습니다. 더 부드러운 경계를 원하면 경계 부드러움을 올리고, 다시 또렷하게 하려면 낮춘 뒤 경계를 지나가세요.",
"경계 부드러움",
"부드럽게 브러시가 남기는 경계의 부드러움으로, 마스크 픽셀 단위입니다. 브러시가 지나간 딱딱한 경계는 대략 이 픽셀 수에 걸쳐 없음에서 가득으로 바뀝니다. 다시 지나가도 그 이상 부드러워지지 않습니다.",
"브러시, 지우개, 부드럽게 브러시가 있고 크기와 경도를 정합니다. 브러시와 지우개에는 불투명도가, 부드럽게 브러시에는 그 대신 경계 부드러움이 있습니다. 부드럽게 브러시는 지나간 경계를 그 부드러움까지 매끄럽게 하고, 몇 번을 지나가도 그 이상은 하지 않습니다. 확대/축소는 작업하는 거리만 바꾸고, 칠해지는 것은 바꾸지 않습니다.",
],
"pt-BR": [
"Desfoca um pouco a camada inteira sendo pintada, suavizando bordas duras onde o efeito rasgaria. Aperte de novo para mais. Para suavizar só algumas bordas, e nunca além de uma suavidade que você define, use o pincel suavizador.",
"Um pincel que suaviza as bordas por onde passa em vez de pintar, até a Suavidade da borda definida para ele. Passar de novo por uma borda nunca a suaviza mais: aumente a Suavidade da borda para uma borda mais suave, ou diminua e passe pela borda para deixá-la mais definida de novo.",
"Suavidade da borda",
"O quanto o pincel suavizador deixa uma borda suave, em pixels da máscara: uma borda dura por onde ele passa vai de nada a cheio ao longo de mais ou menos esse número de pixels. Passar de novo nunca vai além disso.",
"Um pincel, uma borracha e um pincel suavizador, com Tamanho e Dureza. O pincel e a borracha têm Opacidade; o pincel suavizador tem Suavidade da borda no lugar: ele suaviza as bordas por onde passa até essa suavidade e nunca além, não importa quantas vezes você passe. O zoom muda o quão perto você trabalha, não o que é pintado.",
],
"ru": [
"Немного размывает весь рисуемый слой, сглаживая резкие края, где эффект рвался бы. Нажмите ещё раз, чтобы сильнее. Чтобы смягчить лишь некоторые края и никогда не сильнее заданной мягкости, используйте кисть смягчения.",
"Кисть, которая вместо рисования сглаживает края, по которым проходит, до заданной для неё мягкости края. Повторный проход по краю никогда не смягчает его сильнее: увеличьте «Мягкость края», чтобы край стал мягче, или уменьшите её и пройдите по краю, чтобы снова сделать его чётче.",
"Мягкость края",
"Насколько мягкий край оставляет кисть смягчения, в пикселях маски: резкий край, по которому она проходит, переходит от нуля до полного примерно за столько пикселей. Повторный проход никогда не смягчает его дальше.",
"Кисть, ластик и кисть смягчения, с размером и жёсткостью. У кисти и ластика есть непрозрачность; у кисти смягчения вместо неё — мягкость края: она сглаживает края, по которым проходит, до этой мягкости и никогда сильнее, сколько бы раз вы по ним ни проходили. Масштаб меняет то, насколько близко вы работаете, а не то, что рисуется.",
],
"zh-Hans": [
"把正在绘制的整个图层稍微模糊，抚平效果可能撕裂的生硬边缘。再按一次会更柔和。若只想柔化部分边缘，且不超过你设定的柔和度，请用柔化画笔。",
"一支不上色、而是把经过的边缘抚平到所设边缘柔和度的画笔。再次经过同一条边缘不会让它更柔和：想要更柔和的边缘就调高边缘柔和度，想让边缘重新收紧就调低它，再沿边缘画一遍。",
"边缘柔和度",
"柔化画笔留下的边缘有多柔和，以遮罩的像素计：它经过的生硬边缘会在大约这么多像素内从无渐变到满。再次经过也不会更柔和。",
"画笔、橡皮擦和柔化画笔，可调大小和硬度。画笔和橡皮擦有不透明度；柔化画笔则以边缘柔和度代替：它把经过的边缘抚平到这个柔和度，无论经过多少次都不会超过。缩放只改变你工作的远近，不改变画上去的内容。",
],
}

for code, texts in T.items():
    assert len(texts) == len(KEYS), (code, len(texts))
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "softenedge.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in zip(KEYS, texts):
            f.write(key + " = " + text + "\n")
    print(code, len(texts))
