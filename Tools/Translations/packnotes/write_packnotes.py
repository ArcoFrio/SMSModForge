"""A pack variable's Description is drawn under its name in the editor's lists of variables
(1.9.0), so the two texts that said it was shown "only in the editor" now say where. In the 8
shipped languages. Writes Tools/Translations/packnotes/<code>/packnotes.txt for
SMSMODFORGE_REFRESH_TRANSLATIONS."""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

TIP = "variables.noteYourselfShownOnly.tip"
DOC = "docs.variables.noteYourselfShownOnly"

T = {
"de": {
TIP: "Eine Notiz für dich selbst. Wird in den Variablenlisten des Editors unter dem Namen dieser Variable gezeigt, nie im Spiel.",
DOC: "Eine Notiz für dich selbst, nur im Editor gezeigt: unter dem Namen der Variable in jeder Liste der Variablen des Packs.",
},
"es": {
TIP: "Una nota para ti. Se muestra bajo el nombre de esta variable en las listas de variables del editor, nunca en el juego.",
DOC: "Una nota para ti, que se muestra solo en el editor: bajo el nombre de la variable en cada lista de las variables del pack.",
},
"fr": {
TIP: "Une note pour vous-même. Affichée sous le nom de cette variable dans les listes de variables de l'éditeur, jamais en jeu.",
DOC: "Une note pour vous-même, affichée seulement dans l'éditeur : sous le nom de la variable dans chaque liste des variables du pack.",
},
"ja": {
TIP: "自分用のメモです。エディターの変数の一覧で、この変数の名前の下に表示されます。ゲームには出ません。",
DOC: "自分用のメモで、エディターの中でだけ表示されます。パックの変数を並べるどの一覧でも、変数の名前の下に出ます。",
},
"ko": {
TIP: "자신을 위한 메모입니다. 에디터의 변수 목록에서 이 변수 이름 아래에 보이고, 게임에는 나오지 않습니다.",
DOC: "자신을 위한 메모로, 에디터에서만 보입니다. 팩의 변수를 보여 주는 모든 목록에서 변수 이름 아래에 나옵니다.",
},
"pt-BR": {
TIP: "Um lembrete para você. Aparece sob o nome desta variável nas listas de variáveis do editor, nunca no jogo.",
DOC: "Uma nota para você, exibida só no editor: sob o nome da variável em cada lista das variáveis do pack.",
},
"ru": {
TIP: "Заметка для себя. Видна под именем этой переменной в списках переменных редактора, никогда в игре.",
DOC: "Заметка для себя, видна только в редакторе: под именем переменной в каждом списке переменных пака.",
},
"zh-Hans": {
TIP: "给自己看的备注。显示在编辑器变量列表里这个变量的名字下方，游戏里永远不会出现。",
DOC: "给你自己看的备注，只显示在编辑器里：在每个列出模组包变量的列表中，显示在变量名下方。",
},
}

for code, rows in T.items():
    folder = os.path.join(HERE, code)
    os.makedirs(folder, exist_ok=True)
    with io.open(os.path.join(folder, "packnotes.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key, text in rows.items():
            f.write(key + " = " + text + "\n")
    print(code, len(rows))
