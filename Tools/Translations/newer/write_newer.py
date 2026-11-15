"""The newer-pack warning added in 1.9.0, in the 8 shipped languages. Writes
Tools/Translations/newer/<code>/newer.txt for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

T = {
"es": {
"open.newerPack.title": "Guardado con un ModForge más nuevo",
"open.newerPack": "Este pack se guardó por última vez con ModForge {pack}, y este es ModForge {editor}. Puede usar cosas que esta versión no conoce: no se muestran aquí, y guardar el pack con esta versión las perdería. Actualiza ModForge antes de cambiar este pack.",
"save.newerPack.title": "¿Guardar con un ModForge más antiguo?",
"save.newerPack.ask": "Este pack se guardó por última vez con ModForge {pack}, y este es ModForge {editor}. Guardarlo aquí pierde todo lo que {pack} añadió y esta versión no conoce. ¿Guardar de todos modos?",
},
"pt-BR": {
"open.newerPack.title": "Salvo por um ModForge mais novo",
"open.newerPack": "Este pack foi salvo pela última vez pelo ModForge {pack}, e este é o ModForge {editor}. Ele pode usar coisas que esta versão não conhece: elas não aparecem aqui, e salvar o pack com esta versão as perderia. Atualize o ModForge antes de mudar este pack.",
"save.newerPack.title": "Salvar com um ModForge mais antigo?",
"save.newerPack.ask": "Este pack foi salvo pela última vez pelo ModForge {pack}, e este é o ModForge {editor}. Salvá-lo aqui perde tudo o que o {pack} acrescentou e esta versão não conhece. Salvar mesmo assim?",
},
"de": {
"open.newerPack.title": "Mit einem neueren ModForge gespeichert",
"open.newerPack": "Dieses Paket wurde zuletzt mit ModForge {pack} gespeichert, und dies ist ModForge {editor}. Es kann Dinge verwenden, die diese Version nicht kennt: Sie werden hier nicht angezeigt, und das Paket mit dieser Version zu speichern, würde sie verlieren. Aktualisiere ModForge, bevor du dieses Paket änderst.",
"save.newerPack.title": "Mit einem älteren ModForge speichern?",
"save.newerPack.ask": "Dieses Paket wurde zuletzt mit ModForge {pack} gespeichert, und dies ist ModForge {editor}. Hier zu speichern verliert alles, was {pack} hinzugefügt hat und diese Version nicht kennt. Trotzdem speichern?",
},
"fr": {
"open.newerPack.title": "Enregistré par un ModForge plus récent",
"open.newerPack": "Ce pack a été enregistré pour la dernière fois par ModForge {pack}, et voici ModForge {editor}. Il peut utiliser des choses que cette version ne connaît pas : elles ne sont pas affichées ici, et enregistrer le pack avec cette version les perdrait. Mettez ModForge à jour avant de modifier ce pack.",
"save.newerPack.title": "Enregistrer avec un ModForge plus ancien ?",
"save.newerPack.ask": "Ce pack a été enregistré pour la dernière fois par ModForge {pack}, et voici ModForge {editor}. L'enregistrer ici perd tout ce que {pack} a ajouté et que cette version ne connaît pas. Enregistrer quand même ?",
},
"ru": {
"open.newerPack.title": "Сохранено более новым ModForge",
"open.newerPack": "Этот пак в последний раз сохранён ModForge {pack}, а это ModForge {editor}. Он может использовать то, чего эта версия не знает: здесь это не показывается, и при сохранении пака этой версией оно потеряется. Обновите ModForge, прежде чем менять этот пак.",
"save.newerPack.title": "Сохранить более старым ModForge?",
"save.newerPack.ask": "Этот пак в последний раз сохранён ModForge {pack}, а это ModForge {editor}. При сохранении здесь потеряется всё, что добавил {pack} и чего не знает эта версия. Всё равно сохранить?",
},
"ja": {
"open.newerPack.title": "新しい ModForge で保存されています",
"open.newerPack": "このパックは最後に ModForge {pack} で保存されましたが、これは ModForge {editor} です。このバージョンが知らないものを使っている可能性があります。それらはここには表示されず、このバージョンでパックを保存すると失われます。このパックを変更する前に ModForge を更新してください。",
"save.newerPack.title": "古い ModForge で保存しますか？",
"save.newerPack.ask": "このパックは最後に ModForge {pack} で保存されましたが、これは ModForge {editor} です。ここで保存すると、{pack} が追加したもののうちこのバージョンが知らないものはすべて失われます。それでも保存しますか？",
},
"ko": {
"open.newerPack.title": "더 새로운 ModForge로 저장됨",
"open.newerPack": "이 팩은 마지막으로 ModForge {pack}(으)로 저장되었고, 지금은 ModForge {editor}입니다. 이 버전이 모르는 것을 쓰고 있을 수 있습니다. 그런 것은 여기에 보이지 않으며, 이 버전으로 팩을 저장하면 사라집니다. 이 팩을 바꾸기 전에 ModForge를 업데이트하세요.",
"save.newerPack.title": "더 오래된 ModForge로 저장할까요?",
"save.newerPack.ask": "이 팩은 마지막으로 ModForge {pack}(으)로 저장되었고, 지금은 ModForge {editor}입니다. 여기서 저장하면 {pack}이(가) 추가한 것 중 이 버전이 모르는 것은 모두 사라집니다. 그래도 저장할까요?",
},
"zh-Hans": {
"open.newerPack.title": "由更新的 ModForge 保存",
"open.newerPack": "这个模组包最后由 ModForge {pack} 保存，而这是 ModForge {editor}。它可能用到这个版本不认识的内容：这些内容不会在这里显示，用这个版本保存模组包会丢失它们。修改这个模组包之前，请先更新 ModForge。",
"save.newerPack.title": "用较旧的 ModForge 保存？",
"save.newerPack.ask": "这个模组包最后由 ModForge {pack} 保存，而这是 ModForge {editor}。在这里保存会丢失 {pack} 添加的、这个版本不认识的所有内容。仍要保存吗？",
},
}

for code, rows in T.items():
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "newer.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in rows.items():
            f.write(key + " = " + text + "\n")
    print(code, len(rows))
