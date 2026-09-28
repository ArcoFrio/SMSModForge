# Machine translation of SMSModForge/Languages/en.txt

Output: mt/<code>/NN.txt, plain `key = text` lines only, one per source key of chunk NN.
Codes: es, pt-BR, fr, de, ru, ja, ko, zh-Hans.
Assemble: `set SMSMODFORGE_REFRESH_TRANSLATIONS=<scratchpad>\mt\out` then
`dotnet test --filter RefreshShippedTranslations` (writes SMSModForge/Languages/<code>.txt),
then `dotnet test --filter EveryShippedTranslationReadsCleanly`.
Header lines go in mt/out/<code>/00.txt: language.name, language.machineTranslated = yes, language.translators = (empty).

## Rules
- Keep exactly: {gaps}, <tags>, [TOKENS:x], \n, key names. Placeholder names never translated.
- Access keys: Latin languages put `_` before a letter of the translated word. ja/ko/zh: `ファイル(_F)` - the English letter in brackets at the end.
- Plural forms: es/pt-BR/fr/de: .one/.other. ru: .one/.few/.many (no .other). ja/ko/zh-Hans: .other only.
  fr/pt-BR: "one" also covers 0.
- The game's own names (quests, places, variables, "Starmaker Story", "Game Creator", "BepInEx", "Unity", "ModForge",
  "GameObject", "NPC", "SFX" when used as a name, file names, tokens like vanilla:Beach) stay as they are.
- Names of action/condition types (SetVariable, Wait, Weather...) stay English.
- Keep ▸ and ' quotes around UI names; in ja/zh use 「」 for quoted UI names; ko uses ''.
- Tone: short, plain, second person informal where the language allows (es tú, pt você, fr vous, de du, ru вы, ja です/ます, ko 해요체/합니다 → use 합니다 for UI? use polite -요 / 합니다 consistently: 합니다), zh neutral.

## Terms            es | pt-BR | fr | de | ru | ja | ko | zh-Hans
pack (mod pack):     pack | pack | pack | Paket | пак | パック | 팩 | 模组包
editor:              editor | editor | éditeur | Editor | редактор | エディター | 에디터 | 编辑器
dialogue:            diálogo | diálogo | dialogue | Dialog | диалог | 会話 | 대화 | 对话
line (of dialogue):  línea | fala | réplique | Zeile | реплика | セリフ | 대사 | 台词
node:                nodo | nó | nœud | Knoten | узел | ノード | 노드 | 节点
choice:              opción | escolha | choix | Auswahl | выбор | 選択肢 | 선택지 | 选项
character:           personaje | personagem | personnage | Figur | персонаж | キャラクター | 캐릭터 | 角色
actor:               actor | ator | acteur | Akteur | актёр | アクター | 액터 | 演员
outfit:              atuendo | roupa | tenue | Outfit | наряд | 衣装 | 의상 | 服装
bust (portrait):     busto | busto | buste | Büste | портрет | 立ち絵 | 스탠딩 | 立绘
expression:          expresión | expressão | expression | Ausdruck | выражение | 表情 | 표정 | 表情
place:               lugar | lugar | lieu | Ort | локация | 場所 | 장소 | 地点
level:               nivel | nível | niveau | Level | уровень | レベル | 레벨 | 关卡
navigator button:    botón del navegador | botão do navegador | bouton du navigateur | Navigator-Schaltfläche | кнопка навигатора | ナビゲーターボタン | 내비게이터 버튼 | 导航按钮
world map:           mapa del mundo | mapa-múndi | carte du monde | Weltkarte | карта мира | ワールドマップ | 월드맵 | 世界地图
map button:          botón del mapa | botão do mapa | bouton de carte | Kartenschaltfläche | кнопка карты | マップボタン | 지도 버튼 | 地图按钮
scene:               escena | cena | scène | Szene | сцена | シーン | 씬 | 场景
variable:            variable | variável | variable | Variable | переменная | 変数 | 변수 | 变量
integration rule:    regla de integración | regra de integração | règle d'intégration | Integrationsregel | правило интеграции | 統合ルール | 통합 규칙 | 集成规则
condition:           condición | condição | condition | Bedingung | условие | 条件 | 조건 | 条件
action:              acción | ação | action | Aktion | действие | アクション | 액션 | 动作
quest:               misión | missão | quête | Quest | квест | クエスト | 퀘스트 | 任务
task:                tarea | tarefa | tâche | Aufgabe | задача | タスク | 작업 | 目标
subtask:             subtarea | subtarefa | sous-tâche | Unteraufgabe | подзадача | サブタスク | 하위 작업 | 子目标
journal:             diario | diário | journal | Tagebuch | журнал | ジャーナル | 일지 | 日志
screen (UI):         pantalla | tela | écran | Bildschirm | экран | 画面 | 화면 | 界面
wallpaper:           fondo de pantalla | papel de parede | fond d'écran | Hintergrundbild | обои | 壁紙 | 배경화면 | 壁纸
music:               música | música | musique | Musik | музыка | 音楽 | 음악 | 音乐
sound effect (SFX):  efecto de sonido | efeito sonoro | effet sonore | Soundeffekt | звуковой эффект | 効果音 | 효과음 | 音效
the game's own (vanilla): del juego | do jogo | du jeu | des Spiels | игры | ゲーム本来の | 게임 기본 | 游戏原有的
tab:                 pestaña | aba | onglet | Tab | вкладка | タブ | 탭 | 选项卡
export:              exportar | exportar | exporter | exportieren | экспортировать | エクスポート | 내보내기 | 导出
publish:             publicar | publicar | publier | veröffentlichen | опубликовать | 公開 | 게시 | 发布
validate:            validar | validar | valider | prüfen | проверить | 検証 | 검사 | 验证
save (file):         guardar | salvar | enregistrer | speichern | сохранить | 保存 | 저장 | 保存
save (game save):    partida guardada | save | sauvegarde | Spielstand | сохранение | セーブデータ | 세이브 | 存档
player:              jugador | jogador | joueur | Spieler | игрок | プレイヤー | 플레이어 | 玩家
plugin:              plugin | plugin | plugin | Plugin | плагин | プラグイン | 플러그인 | 插件
mask:                máscara | máscara | masque | Maske | маска | マスク | 마스크 | 遮罩
sprite:              sprite | sprite | sprite | Sprite | спрайт | スプライト | 스프라이트 | 精灵图
signal:              señal | sinal | signal | Signal | сигнал | シグナル | 신호 | 信号
tutorial:            tutorial | tutorial | tutoriel | Tutorial | обучение | チュートリアル | 튜토리얼 | 教程
translation:         traducción | tradução | traduction | Übersetzung | перевод | 翻訳 | 번역 | 翻译
tooltip:             descripción emergente | dica | info-bulle | Tooltip | подсказка | ツールチップ | 툴팁 | 工具提示
folder:              carpeta | pasta | dossier | Ordner | папка | フォルダー | 폴더 | 文件夹
rule:                regla | regra | règle | Regel | правило | ルール | 규칙 | 规则
timer:               temporizador | temporizador | minuterie | Timer | таймер | タイマー | 타이머 | 计时器
counter:             contador | contador | compteur | Zähler | счётчик | カウンター | 카운터 | 计数器
component:           componente | componente | composant | Komponente | компонент | コンポーネント | 컴포넌트 | 组件
weather:             clima | clima | météo | Wetter | погода | 天気 | 날씨 | 天气
preview:             vista previa | pré-visualização | aperçu | Vorschau | предпросмотр | プレビュー | 미리보기 | 预览
issue (validation):  problema | problema | problème | Problem | проблема | 問題 | 문제 | 问题
