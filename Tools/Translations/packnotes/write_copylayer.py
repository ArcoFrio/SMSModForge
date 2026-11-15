"""Copying one layer of another outfit's mask (1.9.0, asked by ArcoFrio), in the 8 shipped languages.
Writes Tools/Translations/packnotes/<code>/copylayer.txt for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

KEYS = ["maskStudio.copyLayer.all", "maskStudio.copyLayer.only", "maskStudio.copyLayer.tip",
        "maskStudio.copy.askOne", "maskStudio.copy.askOneUnsaved"]

T = {
"de": [
"Alle Ebenen",
"Nur {layer}",
"Alle Ebenen der Maske dieses Outfits kopieren oder nur eine davon über dieselbe Ebene hier. Die anderen Ebenen bleiben, wie sie sind.",
"Die Ebene {layer} von „{outfit}“ über die Ebene {layer} hier laden?\\n\\nDie anderen Ebenen bleiben, wie sie sind.\\n\\nStrg+Z holt sie zurück.",
"Die Ebene {layer} von „{outfit}“ über die Ebene {layer} hier laden?\\n\\nDiese Ebene wird ersetzt, auch mit den Änderungen, die du daran nicht gespeichert hast. Die anderen Ebenen bleiben, wie sie sind.\\n\\nStrg+Z holt sie zurück.",
],
"es": [
"Todas las capas",
"Solo {layer}",
"Copia todas las capas de la máscara de ese atuendo, o solo una de ellas sobre la misma capa aquí. Las demás capas quedan como están.",
"¿Cargar la capa {layer} de «{outfit}» encima de la capa {layer} de aquí?\\n\\nLas demás capas quedan como están.\\n\\nCtrl+Z la devuelve.",
"¿Cargar la capa {layer} de «{outfit}» encima de la capa {layer} de aquí?\\n\\nEsa capa se reemplaza, incluidos los cambios que no has guardado en ella. Las demás capas quedan como están.\\n\\nCtrl+Z la devuelve.",
],
"fr": [
"Tous les calques",
"{layer} seulement",
"Copier tous les calques du masque de cette tenue, ou un seul d'entre eux par-dessus le même calque ici. Les autres calques restent tels quels.",
"Charger le calque {layer} de « {outfit} » par-dessus le calque {layer} d'ici ?\\n\\nLes autres calques restent tels quels.\\n\\nCtrl+Z le rétablit.",
"Charger le calque {layer} de « {outfit} » par-dessus le calque {layer} d'ici ?\\n\\nCe calque est remplacé, y compris les modifications que vous n'y avez pas enregistrées. Les autres calques restent tels quels.\\n\\nCtrl+Z le rétablit.",
],
"ja": [
"すべてのレイヤー",
"{layer} のみ",
"その衣装のマスクのすべてのレイヤーをコピーするか、1つだけをここの同じレイヤーの上にコピーします。ほかのレイヤーはそのままです。",
"「{outfit}」の {layer} レイヤーを、ここの {layer} レイヤーの上に読み込みますか？\\n\\nほかのレイヤーはそのままです。\\n\\nCtrl+Z で元に戻せます。",
"「{outfit}」の {layer} レイヤーを、ここの {layer} レイヤーの上に読み込みますか？\\n\\nそのレイヤーは、保存していない変更も含めて置き換わります。ほかのレイヤーはそのままです。\\n\\nCtrl+Z で元に戻せます。",
],
"ko": [
"모든 레이어",
"{layer}만",
"그 의상 마스크의 모든 레이어를 가져오거나, 하나만 여기의 같은 레이어 위에 가져옵니다. 다른 레이어는 그대로 둡니다.",
"'{outfit}'의 {layer} 레이어를 여기의 {layer} 레이어 위에 불러올까요?\\n\\n다른 레이어는 그대로 둡니다.\\n\\nCtrl+Z로 되돌릴 수 있습니다.",
"'{outfit}'의 {layer} 레이어를 여기의 {layer} 레이어 위에 불러올까요?\\n\\n그 레이어는 저장하지 않은 변경까지 포함해 바뀝니다. 다른 레이어는 그대로 둡니다.\\n\\nCtrl+Z로 되돌릴 수 있습니다.",
],
"pt-BR": [
"Todas as camadas",
"Só {layer}",
"Copia todas as camadas da máscara dessa roupa, ou só uma delas por cima da mesma camada aqui. As outras camadas ficam como estão.",
"Carregar a camada {layer} de \"{outfit}\" por cima da camada {layer} daqui?\\n\\nAs outras camadas ficam como estão.\\n\\nCtrl+Z desfaz.",
"Carregar a camada {layer} de \"{outfit}\" por cima da camada {layer} daqui?\\n\\nEssa camada é substituída, inclusive as mudanças que você não salvou nela. As outras camadas ficam como estão.\\n\\nCtrl+Z desfaz.",
],
"ru": [
"Все слои",
"Только {layer}",
"Копировать все слои маски этого наряда или только один из них поверх того же слоя здесь. Остальные слои остаются как есть.",
"Загрузить слой {layer} из «{outfit}» поверх слоя {layer} здесь?\\n\\nОстальные слои остаются как есть.\\n\\nCtrl+Z вернёт как было.",
"Загрузить слой {layer} из «{outfit}» поверх слоя {layer} здесь?\\n\\nЭтот слой будет заменён вместе с несохранёнными изменениями на нём. Остальные слои остаются как есть.\\n\\nCtrl+Z вернёт как было.",
],
"zh-Hans": [
"全部图层",
"仅 {layer}",
"复制那套服装遮罩的全部图层，或只把其中一层复制到这里的同一图层上。其他图层保持不变。",
"要把“{outfit}”的 {layer} 图层加载到这里的 {layer} 图层之上吗？\\n\\n其他图层保持不变。\\n\\nCtrl+Z 可以撤销。",
"要把“{outfit}”的 {layer} 图层加载到这里的 {layer} 图层之上吗？\\n\\n该图层会被替换，包括你在上面未保存的改动。其他图层保持不变。\\n\\nCtrl+Z 可以撤销。",
],
}

for code, texts in T.items():
    assert len(texts) == len(KEYS), (code, len(texts))
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "copylayer.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in zip(KEYS, texts):
            f.write(key + " = " + text + "\n")
    print(code, len(texts))
