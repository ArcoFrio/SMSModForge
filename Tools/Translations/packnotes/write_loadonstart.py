"""Load this pack on start (1.9.0), in the 8 shipped languages. Writes
Tools/Translations/packnotes/<code>/loadonstart.txt for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

M = "menu.loadPackOnStart"
MT = "menu.loadPackOnStart.tip"
G = "start.packGone"
NT = "start.packNotReopened.title"
N = "start.packNotReopened"

T = {
"de": {
M: "Dieses Paket beim Start laden",
MT: "Öffnet das zuletzt geladene Paket, sobald ModForge startet. An, bis du das Häkchen entfernst; ohne Häkchen bleibt es für jedes Paket aus, bis du es wieder setzt.",
G: "Das Paket, das ModForge beim Start öffnet, ist nicht mehr in {path}.",
NT: "Paket nicht geöffnet",
N: "ModForge wurde beim letzten Start geschlossen, während es das Paket in {path} öffnete, deshalb hat es es nicht noch einmal geöffnet. Öffne es über Datei > Zuletzt verwendet, wenn du so weit bist; danach wird es wieder beim Start geöffnet.",
},
"es": {
M: "Cargar este pack al iniciar",
MT: "Abre el último pack que cargaste en cuanto se inicia ModForge. Activado hasta que lo desmarques; desmarcado, sigue desactivado para todos los packs hasta que lo vuelvas a marcar.",
G: "El pack que ModForge abre al iniciar ya no está en {path}.",
NT: "Pack no abierto",
N: "ModForge se cerró mientras abría el pack de {path} la última vez que se inició, así que no lo ha vuelto a abrir. Ábrelo desde Archivo > Packs recientes cuando quieras; después se volverá a abrir al iniciar.",
},
"fr": {
M: "Charger ce pack au démarrage",
MT: "Ouvre le dernier pack chargé dès que ModForge démarre. Activé jusqu'à ce que vous le décochiez ; décoché, il reste désactivé pour tous les packs jusqu'à ce que vous le recochiez.",
G: "Le pack que ModForge ouvre au démarrage n'est plus dans {path}.",
NT: "Pack non ouvert",
N: "ModForge s'est fermé pendant l'ouverture du pack de {path} lors de son dernier démarrage, il ne l'a donc pas rouvert. Ouvrez-le depuis Fichier > Fichiers récents quand vous serez prêt ; il sera ensuite de nouveau ouvert au démarrage.",
},
"ja": {
M: "起動時にこのパックを読み込む",
MT: "ModForge の起動と同時に、最後に読み込んだパックを開きます。チェックを外すまでオンのままで、外すと、もう一度チェックするまでどのパックでもオフのままです。",
G: "ModForge が起動時に開くパックは、もう {path} にありません。",
NT: "パックを開きませんでした",
N: "前回の起動時、ModForge は {path} のパックを開いている途中で終了したため、今回は開きませんでした。準備ができたら ファイル > 最近使ったファイル から開いてください。その後は、また起動時に開かれます。",
},
"ko": {
M: "시작할 때 이 팩 불러오기",
MT: "ModForge가 시작되면 마지막으로 불러온 팩을 바로 엽니다. 체크를 해제할 때까지 켜져 있고, 해제하면 다시 체크할 때까지 모든 팩에서 꺼진 상태로 남습니다.",
G: "ModForge가 시작할 때 여는 팩이 더 이상 {path}에 없습니다.",
NT: "팩을 열지 않음",
N: "지난번 시작할 때 ModForge가 {path}의 팩을 여는 도중에 종료되어, 이번에는 다시 열지 않았습니다. 준비가 되면 파일 > 최근 파일에서 여세요. 그 뒤로는 다시 시작할 때 열립니다.",
},
"pt-BR": {
M: "Carregar este pack ao iniciar",
MT: "Abre o último pack que você carregou assim que o ModForge inicia. Ligado até você desmarcar; desmarcado, fica desligado para todos os packs até você marcar de novo.",
G: "O pack que o ModForge abre ao iniciar não está mais em {path}.",
NT: "Pack não aberto",
N: "O ModForge fechou enquanto abria o pack em {path} na última vez que iniciou, então não o abriu de novo. Abra-o em Arquivo > Packs recentes quando estiver pronto; depois disso ele voltará a abrir ao iniciar.",
},
"ru": {
M: "Загружать этот пак при запуске",
MT: "Открывает последний загруженный пак сразу при запуске ModForge. Включено, пока вы не снимете флажок; без флажка остаётся выключенным для всех паков, пока вы снова его не поставите.",
G: "Пака, который ModForge открывает при запуске, больше нет в {path}.",
NT: "Пак не открыт",
N: "При прошлом запуске ModForge закрылся, пока открывал пак из {path}, поэтому не стал открывать его снова. Откройте его через Файл > Недавние файлы, когда будете готовы; после этого он снова будет открываться при запуске.",
},
"zh-Hans": {
M: "启动时加载此模组包",
MT: "ModForge 一启动就打开你上次加载的模组包。在你取消勾选之前一直开启；取消勾选后，对所有模组包都保持关闭，直到你再次勾选。",
G: "ModForge 启动时打开的模组包已不在 {path} 中。",
NT: "未打开模组包",
N: "上次启动时，ModForge 在打开 {path} 中的模组包时关闭了，所以这次没有再打开它。准备好后，请从 文件 > 最近的模组包 打开它；之后它会在启动时再次被打开。",
},
}

for code, rows in T.items():
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "loadonstart.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in rows.items():
            f.write(key + " = " + text + "\n")
    print(code, len(rows))
