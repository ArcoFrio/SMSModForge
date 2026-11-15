"""Why the 1.9.0 setup's download failed (setup.download.problem.*), in the 8 shipped
languages. Each is said after the failure page's 'What happened:', so it starts the way
setup.download.noPlugin does in that language. Writes
Tools/Translations/setupproblems/<code>/setupproblems.txt for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

T = {
"es": {
"setup.download.problem.noRelease": "GitHub no muestra ninguna versión de ModForge",
"setup.download.problem.noChecksum": "la versión no da una suma de comprobación para {file}, así que nada podría confirmar que es el archivo de la versión",
"setup.download.problem.size": "{file} llegó con {got} bytes, no los {want} que indica la versión",
"setup.download.problem.changed": "{file} no es el archivo de la versión: su contenido cambió por el camino",
"setup.download.problem.busy": "GitHub está limitando cuántas veces puede preguntarle esta red; inténtalo de nuevo dentro de una hora",
"setup.download.problem.http": "GitHub respondió con el error {code}",
"setup.download.problem.network": "falló la conexión con GitHub",
"setup.download.problem.timeout": "GitHub dejó de responder",
},
"pt-BR": {
"setup.download.problem.noRelease": "o GitHub não mostra nenhuma versão do ModForge",
"setup.download.problem.noChecksum": "a versão não dá uma soma de verificação para {file}, então nada poderia confirmar que é o arquivo da versão",
"setup.download.problem.size": "{file} chegou com {got} bytes, não os {want} que a versão indica",
"setup.download.problem.changed": "{file} não é o arquivo da versão: o conteúdo mudou no caminho",
"setup.download.problem.busy": "o GitHub está limitando quantas vezes esta rede pode consultá-lo; tente de novo daqui a uma hora",
"setup.download.problem.http": "o GitHub respondeu com o erro {code}",
"setup.download.problem.network": "a conexão com o GitHub falhou",
"setup.download.problem.timeout": "o GitHub parou de responder",
},
"de": {
"setup.download.problem.noRelease": "GitHub führt keine Version von ModForge",
"setup.download.problem.noChecksum": "die Version nennt keine Prüfsumme für {file}, also könnte nichts bestätigen, dass es die Datei der Version ist",
"setup.download.problem.size": "{file} kam mit {got} Bytes an, nicht mit den {want}, die die Version angibt",
"setup.download.problem.changed": "{file} ist nicht die Datei der Version: Ihr Inhalt hat sich unterwegs verändert",
"setup.download.problem.busy": "GitHub begrenzt, wie oft dieses Netzwerk es fragen darf; versuche es in einer Stunde noch einmal",
"setup.download.problem.http": "GitHub hat mit dem Fehler {code} geantwortet",
"setup.download.problem.network": "die Verbindung zu GitHub ist fehlgeschlagen",
"setup.download.problem.timeout": "GitHub antwortet nicht mehr",
},
"fr": {
"setup.download.problem.noRelease": "GitHub ne liste aucune version de ModForge",
"setup.download.problem.noChecksum": "la version ne donne pas de somme de contrôle pour {file}, donc rien ne pourrait confirmer que c'est le fichier de la version",
"setup.download.problem.size": "{file} est arrivé avec {got} octets, pas les {want} qu'indique la version",
"setup.download.problem.changed": "{file} n'est pas le fichier de la version : son contenu a changé en route",
"setup.download.problem.busy": "GitHub limite le nombre de fois où ce réseau peut l'interroger ; réessayez dans une heure",
"setup.download.problem.http": "GitHub a répondu avec l'erreur {code}",
"setup.download.problem.network": "la connexion à GitHub a échoué",
"setup.download.problem.timeout": "GitHub a cessé de répondre",
},
"ru": {
"setup.download.problem.noRelease": "на GitHub нет ни одного выпуска ModForge",
"setup.download.problem.noChecksum": "в выпуске нет контрольной суммы для {file}, поэтому ничто не может подтвердить, что это файл выпуска",
"setup.download.problem.size": "{file} пришёл размером {got} байт, а не {want}, как указано в выпуске",
"setup.download.problem.changed": "{file} — не файл выпуска: его содержимое изменилось по дороге",
"setup.download.problem.busy": "GitHub ограничивает, как часто эта сеть может к нему обращаться; попробуйте снова через час",
"setup.download.problem.http": "GitHub ответил ошибкой {code}",
"setup.download.problem.network": "не удалось подключиться к GitHub",
"setup.download.problem.timeout": "GitHub перестал отвечать",
},
"ja": {
"setup.download.problem.noRelease": "GitHub に ModForge のリリースがありません",
"setup.download.problem.noChecksum": "このリリースには {file} のチェックサムがないため、リリースのファイルであることを確かめられません",
"setup.download.problem.size": "{file} は {got} バイトで届きました。リリースに記載されたサイズは {want} バイトです",
"setup.download.problem.changed": "{file} はリリースのファイルではありません。途中で内容が変わりました",
"setup.download.problem.busy": "GitHub がこのネットワークからの問い合わせ回数を制限しています。1 時間後にもう一度試してください",
"setup.download.problem.http": "GitHub がエラー {code} を返しました",
"setup.download.problem.network": "GitHub への接続に失敗しました",
"setup.download.problem.timeout": "GitHub から応答がなくなりました",
},
"ko": {
"setup.download.problem.noRelease": "GitHub에 ModForge 릴리스가 없습니다",
"setup.download.problem.noChecksum": "이 릴리스에는 {file}의 체크섬이 없어서 릴리스의 파일인지 확인할 수 없습니다",
"setup.download.problem.size": "{file}이(가) {got}바이트로 받아졌습니다. 릴리스에 적힌 크기는 {want}바이트입니다",
"setup.download.problem.changed": "{file}은(는) 릴리스의 파일이 아닙니다. 받는 도중에 내용이 바뀌었습니다",
"setup.download.problem.busy": "GitHub가 이 네트워크의 요청 횟수를 제한하고 있습니다. 한 시간 뒤에 다시 시도하세요",
"setup.download.problem.http": "GitHub가 오류 {code}로 응답했습니다",
"setup.download.problem.network": "GitHub에 연결하지 못했습니다",
"setup.download.problem.timeout": "GitHub가 응답을 멈췄습니다",
},
"zh-Hans": {
"setup.download.problem.noRelease": "GitHub 上没有 ModForge 的任何版本",
"setup.download.problem.noChecksum": "这个版本没有提供 {file} 的校验和，所以无法确认它是这个版本的文件",
"setup.download.problem.size": "{file} 下载后是 {got} 字节，而不是版本列出的 {want} 字节",
"setup.download.problem.changed": "{file} 不是这个版本的文件：它的内容在传输途中被改变了",
"setup.download.problem.busy": "GitHub 正在限制这个网络的请求次数；请一小时后再试",
"setup.download.problem.http": "GitHub 返回了错误 {code}",
"setup.download.problem.network": "连接 GitHub 失败",
"setup.download.problem.timeout": "GitHub 停止了响应",
},
}

for code, rows in T.items():
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "setupproblems.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in rows.items():
            f.write(key + " = " + text + "\n")
    print(code, len(rows))
