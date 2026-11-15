"""The toggle at the top of an open list of variables (1.9.0): whether each one's note is drawn
under its name and searched. In the 8 shipped languages. Writes
Tools/Translations/packnotes/<code>/notestoggle.txt for SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

BOX = "dropdown.variableNotes"
TIP = "dropdown.variableNotes.tip"

T = {
"de": {
BOX: "Notizen zeigen",
TIP: "Unter jedem Namen, wofür die Variable da ist: was das Spiel mit jeder seiner eigenen macht, und die Beschreibung, die du jeder deiner Variablen gegeben hast. Solange das Häkchen gesetzt ist, findet die Eingabe auch diese Wörter. Gilt für jede Liste von Variablen und wird gemerkt.",
},
"es": {
BOX: "Mostrar notas",
TIP: "Bajo cada nombre, para qué sirve la variable: qué hace el juego con cada una de las suyas, y la descripción que le diste a cada una de las tuyas. Mientras esté marcada, escribir encuentra también esas palabras. Se recuerda para todas las listas de variables.",
},
"fr": {
BOX: "Afficher les notes",
TIP: "Sous chaque nom, à quoi sert la variable : ce que le jeu fait de chacune des siennes, et la description que vous avez donnée à chacune des vôtres. Tant que la case est cochée, la saisie trouve aussi ces mots. Retenu pour toutes les listes de variables.",
},
"ja": {
BOX: "メモを表示",
TIP: "各名前の下に、その変数が何のためのものかを表示します。ゲーム自身の変数ならゲームがそれをどう使うか、あなたの変数なら付けた説明です。チェックしている間は、入力でそれらの言葉も検索できます。すべての変数の一覧に適用され、記憶されます。",
},
"ko": {
BOX: "메모 보기",
TIP: "각 이름 아래에 그 변수가 무엇을 위한 것인지 보여 줍니다. 게임 자체의 변수는 게임이 그것으로 무엇을 하는지, 내 변수는 내가 적은 설명입니다. 체크되어 있는 동안에는 입력으로 그 단어들도 찾습니다. 모든 변수 목록에 적용되며 기억됩니다.",
},
"pt-BR": {
BOX: "Mostrar notas",
TIP: "Sob cada nome, para que serve a variável: o que o jogo faz com cada uma das dele, e a descrição que você deu a cada uma das suas. Enquanto estiver marcada, digitar também encontra essas palavras. Vale para todas as listas de variáveis e é lembrado.",
},
"ru": {
BOX: "Показывать заметки",
TIP: "Под каждым именем — для чего эта переменная: что игра делает с каждой из своих и какое описание вы дали каждой из своих. Пока флажок стоит, ввод находит и эти слова. Действует для всех списков переменных и запоминается.",
},
"zh-Hans": {
BOX: "显示备注",
TIP: "在每个名字下方显示这个变量的用途：游戏自己的变量写的是游戏怎么用它，你的变量写的是你给它的描述。勾选时，输入也会搜索这些文字。对所有变量列表都有效，并会被记住。",
},
}

for code, rows in T.items():
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "notestoggle.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in rows.items():
            f.write(key + " = " + text + "\n")
    print(code, len(rows))
