"""Preview quality as frame rate only (1.9.1, from a user's feedback passed on by ArcoFrio: Ultra "a lot of CPU usage
with no visual benefit"): the four levels' tooltips and the docs line, reworded now that no level supersamples,
in the 8 shipped languages. Writes Tools/Translations/packnotes/<code>/quality.txt for
SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

KEYS = ["previewQuality.ultra.tip", "previewQuality.high.tip", "previewQuality.medium.tip", "previewQuality.low.tip",
        "docs.optionsMenu.tradePreviewSmoothnessAgainst"]

T = {
"de": [
"60 Bilder pro Sekunde: die flüssigste Bewegung und die meiste Arbeit für den Computer.",
"30 Bilder pro Sekunde: flüssige Bewegung bei halb so viel Arbeit wie Ultra.",
"20 Bilder pro Sekunde, die Voreinstellung: leicht und flüssig genug für das meiste Wackeln.",
"15 Bilder pro Sekunde: am leichtesten, mit ruckeligerer Bewegung.",
"Flüssigkeit der Vorschau gegen Prozessorlast abwägen: Die Vorschauen werden bei Ultra 60-mal pro Sekunde gezeichnet, bei Hoch 30-mal, bei Mittel 20-mal und bei Niedrig 15-mal. Was sie zeigen, ist bei jeder Qualität gleich; nur wie flüssig es sich bewegt, ändert sich.",
],
"es": [
"60 fotogramas por segundo: el movimiento más fluido y el mayor trabajo para el ordenador.",
"30 fotogramas por segundo: movimiento fluido con la mitad del trabajo de Ultra.",
"20 fotogramas por segundo, la predeterminada: ligera y lo bastante fluida para la mayoría de los rebotes.",
"15 fotogramas por segundo: la más ligera, con un movimiento más entrecortado.",
"Cambia suavidad de la vista previa por carga del procesador: las vistas previas se dibujan 60 veces por segundo en Ultra, 30 en Alta, 20 en Media y 15 en Baja. Lo que muestran es igual en todas las calidades; solo cambia lo fluido que se mueve.",
],
"fr": [
"60 images par seconde : le mouvement le plus fluide, et le plus de travail pour l'ordinateur.",
"30 images par seconde : un mouvement fluide, pour la moitié du travail d'Ultra.",
"20 images par seconde, la valeur par défaut : légère, et assez fluide pour la plupart des rebonds.",
"15 images par seconde : la plus légère, avec un mouvement plus saccadé.",
"Arbitrer entre fluidité de l'aperçu et charge du processeur : les aperçus sont dessinés 60 fois par seconde en Ultra, 30 en Élevée, 20 en Moyenne et 15 en Basse. Ce qu'ils montrent est le même à chaque qualité ; seule la fluidité du mouvement change.",
],
"ja": [
"毎秒 60 フレーム。最もなめらかな動きで、コンピューターの負荷も最大です。",
"毎秒 30 フレーム。なめらかな動きを、最高の半分の負荷で描きます。",
"毎秒 20 フレーム（既定）。軽く、たいていの揺れには十分なめらかです。",
"毎秒 15 フレーム。最も軽く、動きはぎこちなくなります。",
"プレビューのなめらかさとプロセッサーの負荷のバランスを選びます。プレビューは、最高で毎秒 60 回、高で 30 回、中で 20 回、低で 15 回描かれます。どの品質でも表示される内容は同じで、変わるのは動きのなめらかさだけです。",
],
"ko": [
"초당 60프레임: 가장 부드러운 움직임이며, 컴퓨터가 가장 많이 일합니다.",
"초당 30프레임: 최상의 절반 작업으로 부드러운 움직임을 냅니다.",
"초당 20프레임, 기본값: 가볍고, 대부분의 흔들림에는 충분히 부드럽습니다.",
"초당 15프레임: 가장 가볍고, 움직임이 더 끊깁니다.",
"미리보기의 부드러움과 프로세서 부하 사이를 조절합니다. 미리보기는 최상에서 초당 60번, 높음에서 30번, 보통에서 20번, 낮음에서 15번 그려집니다. 보이는 내용은 어느 품질에서나 같고, 움직임이 얼마나 부드러운지만 달라집니다.",
],
"pt-BR": [
"60 quadros por segundo: o movimento mais suave, e o maior trabalho para o computador.",
"30 quadros por segundo: movimento suave, com metade do trabalho da Ultra.",
"20 quadros por segundo, a padrão: leve, e suave o bastante para a maioria dos balanços.",
"15 quadros por segundo: a mais leve, com movimento mais picotado.",
"Troca suavidade da pré-visualização por carga do processador: as pré-visualizações são desenhadas 60 vezes por segundo na Ultra, 30 na Alta, 20 na Média e 15 na Baixa. O que elas mostram é igual em todas as qualidades; só muda o quão suave é o movimento.",
],
"ru": [
"60 кадров в секунду: самое плавное движение и самая большая нагрузка на компьютер.",
"30 кадров в секунду: плавное движение при половине нагрузки Ультра.",
"20 кадров в секунду, по умолчанию: легко и достаточно плавно для большинства покачиваний.",
"15 кадров в секунду: самое лёгкое, движение прерывистее.",
"Выбор между плавностью предпросмотра и нагрузкой на процессор: предпросмотры рисуются 60 раз в секунду на Ультра, 30 на Высоком, 20 на Среднем и 15 на Низком. Показывают они одно и то же при любом качестве; меняется только плавность движения.",
],
"zh-Hans": [
"每秒 60 帧：动作最流畅，电脑的负担也最大。",
"每秒 30 帧：动作流畅，负担只有“极高”的一半。",
"每秒 20 帧，默认：轻快，对大多数晃动来说足够流畅。",
"每秒 15 帧：最轻快，动作较卡顿。",
"用处理器负担换取预览的流畅度：预览在“极高”时每秒绘制 60 次，“高”30 次，“中”20 次，“低”15 次。无论哪种质量，显示的内容都一样；变的只是动作有多流畅。",
],
}

for code, texts in T.items():
    assert len(texts) == len(KEYS), (code, len(texts))
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "quality.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in zip(KEYS, texts):
            f.write(key + " = " + text + "\n")
    print(code, len(texts))
